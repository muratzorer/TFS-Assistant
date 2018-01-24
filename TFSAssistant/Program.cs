using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using log4net;

namespace TFSAssistant
{
    class Program
    {
        private static readonly ILog _log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        static int Main(string[] args)
        {
            _log.InfoFormat("*** Starting TFS Assistant ***", Environment.NewLine);

            return CommandLine.Parser.Default.ParseArguments<MergeOptions>(args)
                .MapResult(
                  (MergeOptions opts) => RunMergeAndReturnExitCode(opts),
                  errs => 1);
        }

        private static int RunMergeAndReturnExitCode(MergeOptions opts)
        {
            try
            {
                using (TFSService service = new TFSService(opts))
                {
                    service.MergeByWorkItem();
                }

                return 0;
            }
            catch (Exception ex)
            {
                _log.Error(string.Format("HelpLink = {0}", ex.HelpLink));
                _log.Error(string.Format("Message = {0}", ex.Message));
                _log.Error(string.Format("Source = {0}", ex.Source));
                _log.Error(string.Format("StackTrace = {0}", ex.StackTrace));
                _log.Error(string.Format("TargetSite = {0}", ex.TargetSite));
                if (ex.InnerException != null)
                    _log.Error(string.Format("InnerException = {0}", ex.InnerException.Message));

                return 1;
            }
            finally
            {
                _log.InfoFormat("*** Ending TFS Assistant ***", Environment.NewLine);
                Console.WriteLine("Press enter to exit");
                Console.ReadLine();
            }   
        }
    }
}
