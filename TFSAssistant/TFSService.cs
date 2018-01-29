using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.VisualStudio.Services.Common;

namespace TFSAssistant
{
    public class TFSService : IDisposable
    {
        private static readonly ILog _log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private readonly TfsTeamProjectCollection _collection;
        private readonly VersionControlServer _sourceControl;
        private readonly Workspace _workspace;
        private readonly WorkItemStore _workitemStore;

        private readonly string _source;
        private readonly string _target;
        private readonly int _workitemID;

        public TFSService(MergeOptions opts)
        {
            _collection = new TfsTeamProjectCollection(new Uri(opts.CollectionUrl));
            _collection.EnsureAuthenticated();
            _sourceControl = _collection.GetService<VersionControlServer>();
            Workstation.Current.EnsureUpdateWorkspaceInfoCache(_sourceControl, _sourceControl.AuthorizedUser);
            _workspace = _sourceControl.GetWorkspace(opts.Workspace, _sourceControl.AuthorizedUser); // Or WindowsIdentity.GetCurrent().Name
            _workitemStore = _collection.GetService<WorkItemStore>();
            _workitemStore.RefreshCache();

            _source = opts.Source;
            _target = opts.Target;
            _workitemID = opts.Workitem;

            SubscribeEvents();
        }

        public void MergeByWorkItem()
        {
            var tuples = GetLinkAndChangesetPairs(_workitemID, _source);
            if (tuples == null || tuples.Count() == 0)
            {  
                _log.InfoFormat("There is no applicable changeset to merge to target branch '{0}' linked to workitem {1}.", _target, _workitemID);
                return;
            }

            var sourceResult = GetLatest(_source);
            if (sourceResult == false) return;

            var targetResult = GetLatest(_target);
            if (targetResult == false) return;

            var workitem = _workitemStore.GetWorkItem(_workitemID);

            // *** ENSURE list is sorted by Changeset ID's in order not to chekin to target in wrong order ***
            tuples.Sort((a, b) => a.Item2.ChangesetId.CompareTo(b.Item2.ChangesetId));

            foreach (var tuple in tuples)
            {
                var changeset = tuple.Item2;
                var link = tuple.Item1;

                var version = VersionSpec.ParseSingleSpec(changeset.ChangesetId.ToString(), _sourceControl.AuthorizedUser);
                _log.InfoFormat("Merge process for changeset {0} is starting... Source: {1} - Target: {2}", changeset.ChangesetId, _source, _target);
                var status = _workspace.Merge(_source, _target, version, version);
                var result = ValidateStatus(status);

                workitem.Links.Remove(link);
                workitem.Save();

                if (result == false)
                {
                    _log.InfoFormat("Link of changeset {0} removed from workitem {1}", changeset.ChangesetId, _workitemID);
                    _log.InfoFormat("Please copy the comment of Changeset {0}: '{1}'", changeset.ChangesetId, changeset.Comment);
                    return;
                }

                // Get pending changes of files ONLY related to this changeset
                var serverItems = changeset.Changes.Select(c => c.Item.ServerItem).ToArray();
                var pendingChanges = _workspace.GetPendingChanges(serverItems);

                // If the set of pending changes for checkin is null, the server will attempt to check in all changes in the workspace, but this operation
                // is not valid if any pending changes in the workspace are edits or adds, as content will not have been uploaded to the server.
                if (pendingChanges == null || pendingChanges.Count() == 0) continue;

                var workitemRelation = new WorkItemCheckinInfo(workitem, WorkItemCheckinAction.Associate);
                var newChangesetID = _workspace.CheckIn(pendingChanges, changeset.Comment, null, new WorkItemCheckinInfo[] { workitemRelation }, null);

                _log.InfoFormat("Changeset {0} checked in under WorkItem: {1}", newChangesetID, _workitemID);
            }
        }

        private bool GetLatest(string serverItem)
        {
            _log.InfoFormat("Getting latest version (Recursive) of branch '{0}'", serverItem);
            var getRequest = new GetRequest(serverItem, RecursionType.Full, VersionSpec.Latest);
            var status = _workspace.Get(getRequest, GetOptions.None);

            // Ensure There is no conflict
            var result = ValidateStatus(status);

            return result;
        }

        private bool ValidateStatus(GetStatus status)
        {
            if (status.NoActionNeeded && status.NumOperations == 0)
            {
                _log.Info("Result: No operations were performed.");
                return true;
            } 
            else if (status.NoActionNeeded && status.NumOperations > 0 && status.NumConflicts == 0)
            {
                _log.Info("Result: Completed without conflicts.");
                return true;
            }
            else if (status.NoActionNeeded && status.NumConflicts > 0 && status.NumResolvedConflicts > 0 && status.NumConflicts == status.NumResolvedConflicts)
            {
                _log.Info("Result: Completed with conflicts. Conflicts resolved automatically.");
                return true;
            }
            else if (status.NoActionNeeded == false && status.NumConflicts > 0 && status.HaveResolvableWarnings == false)
            {
                _log.Info("Result: Completed with conflicts. Conflicts could NOT resolved automatically. Please resolve conflicts using IDE and rerun exe; process is stateless");
                return false;
            }
            else
            {
                _log.Info("Result: *** Unhandled conflict status, process is terminating. Possible completed with conflicts, please resolve conflicts using IDE and contact administrator before rerun exe; process is stateless ***");
                _log.InfoFormat("NumOperations: {0}, NumResolvedConflicts: {1}, NumWarnings: {2}, NoActionNeeded {3}, HaveResolvableWarnings: {4}", status.NumOperations, status.NumResolvedConflicts, status.NumWarnings, status.NoActionNeeded, status.HaveResolvableWarnings);
                return false;
            }
        }

        private List<Tuple<Link, Changeset>> GetLinkAndChangesetPairs(int workitemID, string source)
        {
            var query = string.Format("SELECT * FROM WorkItems WHERE [System.Id] = {0}", workitemID);
            var workItems = _workitemStore.Query(query); // _workitemStore.GetWorkItem() throws exception if not found. This is fail safe.

            if (workItems.Count == 0)
            {
                _log.Info("No workitems found with given inputs!!");
                return null;
            }
            _log.InfoFormat("Workitem {0} found", workitemID);

            var versionControl = _collection.GetService<VersionControlServer>();
            var linkAndChangesetTuples = new List<Tuple<Link, Changeset>>();

            foreach (var link in workItems[0].Links)
            {
                var extLink = link as ExternalLink;
                if (extLink == null) continue;

                // Ensure link has a type of 'Changeset'
                var artifact = LinkingUtilities.DecodeUri(extLink.LinkedArtifactUri);
                if (String.Equals(artifact.ArtifactType, "Changeset", StringComparison.Ordinal) == false) continue;

                // Ensure changeset is related to given source branch
                var changeset = versionControl.ArtifactProvider.GetChangeset(new Uri(extLink.LinkedArtifactUri));
                if (changeset.Changes[0].Item.ServerItem.StartsWith(source) == false) continue;

                // Ensure changeset is related to this user
                if (changeset.Committer != _sourceControl.AuthorizedUser) continue;

                linkAndChangesetTuples.Add(new Tuple<Link, Changeset>(link as Link, changeset));
            }

            return linkAndChangesetTuples;
        }

        private void SubscribeEvents()
        {
            // Listen for the Source Control events.
            _sourceControl.NonFatalError += this.OnNonFatalError;
            _sourceControl.Merging += this.OnMerging;
            _sourceControl.Getting += this.OnGetting;
            _sourceControl.BeforeCheckinPendingChange += this.OnBeforeCheckinPendingChange;
        }

        private void OnNonFatalError(Object sender, ExceptionEventArgs e)
        {
            if (e.Exception != null)
            {
                _log.Error("Non-fatal exception: " + e.Exception.Message);
            }
            else
            {
                _log.Error("Non-fatal failure: " + e.Failure.Message);
            }
        }

        private void OnMerging(Object sender, MergeEventArgs e)
        {
            _log.InfoFormat("Merging items --> Source: '{0}', Target: '{1}'", e.SourceServerItem, e.TargetServerItem);
            _log.InfoFormat("Merging items --> IsConflict: {0}, IsLatest: {1}, ", e.IsConflict, e.IsLatest);
            _log.InfoFormat("Merging items --> Status: {0}, Resolution: {1}, ", e.Status, e.Resolution);
            _log.InfoFormat(string.Empty);
        }

        private void OnGetting(Object sender, GettingEventArgs e)
        {
            _log.InfoFormat("Getting: '{0}', status: {1}", e.TargetLocalItem, e.Status);
        }

        private void OnBeforeCheckinPendingChange(Object sender, ProcessingChangeEventArgs e)
        {
            _log.InfoFormat("Checking in '{0}'", e.PendingChange.LocalItem);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);  
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _collection.Dispose();
            }
        }
    }
}
