using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace XppAiCopilotCompanion
{
    internal sealed class RegisterMcpCommand
    {
        private readonly AsyncPackage _package;

        private RegisterMcpCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            var cmdId = new CommandID(PackageGuids.CommandSetGuid, CommandIds.RegisterMcp);
            commandService.AddCommand(new MenuCommand(Execute, cmdId));
        }

        public static RegisterMcpCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var cmdService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new RegisterMcpCommand(package, cmdService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var pkg = XppCopilotPackage.Instance;
                string report = pkg != null
                    ? pkg.RegisterMcpServer()
                    : "Package instance is not initialized.";

                OutputPane.Write("=== MCP Registration Report ===");
                OutputPane.Write(report);
                OutputPane.Write("=== End MCP Registration Report ===");

                VsShellUtilities.ShowMessageBox(
                    _package,
                    "MCP registration attempted. See Output window > X++ AI for full report.",
                    "X++ AI MCP",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
            catch (Exception ex)
            {
                OutputPane.Write("MCP registration failed: " + ex.Message);
                VsShellUtilities.ShowMessageBox(
                    _package,
                    ex.Message,
                    "X++ AI MCP Error",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }
    }
}
