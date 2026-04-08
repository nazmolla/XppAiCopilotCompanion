using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace XppAiCopilotCompanion
{
    internal sealed class McpDiagnosticsCommand
    {
        private readonly AsyncPackage _package;

        private McpDiagnosticsCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            var cmdId = new CommandID(PackageGuids.CommandSetGuid, CommandIds.McpDiagnostics);
            commandService.AddCommand(new MenuCommand(Execute, cmdId));
        }

        public static McpDiagnosticsCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var cmdService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new McpDiagnosticsCommand(package, cmdService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var pkg = XppCopilotPackage.Instance;
                string report = pkg != null
                    ? pkg.GetMcpDiagnosticsReport()
                    : "Package instance is not initialized.";

                OutputPane.Write(report);

                VsShellUtilities.ShowMessageBox(
                    _package,
                    "MCP diagnostics completed. See Output window > X++ AI for details.",
                    "X++ AI MCP Diagnostics",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
            catch (Exception ex)
            {
                OutputPane.Write("MCP diagnostics failed: " + ex.Message);
                VsShellUtilities.ShowMessageBox(
                    _package,
                    ex.Message,
                    "X++ AI MCP Diagnostics Error",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }
    }
}
