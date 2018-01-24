using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace TFSAssistant
{
    [Verb("merge", HelpText = "Gets latest version of both source and target branches. Then performs a merge. Then commits the merged changeset if there is no unresolved conflict.")]
    public class MergeOptions
    {
        [Option('c', "collection", Required = true, HelpText = "Collection URL. eg: 'http://127.0.0.1:8080/Tfs/DefaultCollection'")]
        public string CollectionUrl { get; set; }

        [Option('s', "workspace", Required = true, HelpText = "Workspace name. eg: 'MyPc001'")]
        public string Workspace { get; set; }

        [Option('w', "workitem", Required = true, HelpText = "Workitem ID. eg: '1234'")]
        public int Workitem { get; set; }

        [Option('s', "source", Required = true, HelpText = "ServerPath (after collection name) for source branch. eg: '$/Project/Solution/Dev'")]
        public string Source { get; set; }

        [Option('t', "target", Required = true, HelpText = "ServerPath (after collection name) for target branch. eg: '$/Project/Solution/Test'")]
        public string Target { get; set; }
    }
    
}
