using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace XppAiCopilotCompanion
{
    internal sealed class RefreshContextCommand
    {
        private readonly AsyncPackage _package;

        private RefreshContextCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            var cmdId = new CommandID(PackageGuids.CommandSetGuid, CommandIds.RefreshContext);
            commandService.AddCommand(new MenuCommand(Execute, cmdId));
        }

        public static RefreshContextCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var cmdService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new RefreshContextCommand(package, cmdService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var vs = ServiceLocator.GetVisualStudioSession();
            var settings = ServiceLocator.GetSettingsService();
            var pipeline = new XppContextPipelineService(vs, settings);
            var bridge = new CopilotContextBridge(pipeline);
            var adapter = new CopilotContextProviderAdapter(bridge, vs);

            string workspaceRoot = vs.GetActiveSolutionDirectory();
            if (string.IsNullOrWhiteSpace(workspaceRoot))
            {
                OutputPane.Write("No solution open. Open a D365FO solution first.");
                return;
            }

            string payload = adapter.GetContextPayload(workspaceRoot, "refresh context");
            int estimatedTokens = TokenEstimator.EstimateTokens(payload);

            OutputPane.Write("=== X++ AI Context Refreshed ===");
            OutputPane.Write("Estimated tokens: " + estimatedTokens);
            OutputPane.Write(payload);
            OutputPane.Write("=== End Context ===");
        }
    }
}
