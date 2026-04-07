using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace XppAiCopilotCompanion
{
    internal sealed class GenerateCodeCommand
    {
        private readonly AsyncPackage _package;

        private GenerateCodeCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            var cmdId = new CommandID(PackageGuids.CommandSetGuid, CommandIds.GenerateCode);
            commandService.AddCommand(new MenuCommand(Execute, cmdId));
        }

        public static GenerateCodeCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var cmdService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new GenerateCodeCommand(package, cmdService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string userPrompt = PromptDialog.Show(
                "X++ AI Code Generation",
                "Describe the X++ code you need:",
                "Create a CoC extension for...");
            if (string.IsNullOrWhiteSpace(userPrompt)) return;

            var vs = ServiceLocator.GetVisualStudioSession();
            var settings = ServiceLocator.GetSettingsService();
            var pipeline = new XppContextPipelineService(vs, settings);
            var bridge = new CopilotContextBridge(pipeline);
            var adapter = new CopilotContextProviderAdapter(bridge, vs);
            var controller = new XppCopilotUiController(adapter, vs);
            var ai = new CopilotLanguageModelService(vs);
            var objectCreator = new XppObjectCreationService(vs);
            var workflow = new XppCopilotWorkflowService(controller, ai, objectCreator);

            try
            {
                string notes = workflow.GenerateAndApplyToOpenDocument(userPrompt);
                OutputPane.Write(notes);
            }
            catch (Exception ex)
            {
                OutputPane.Write("Error: " + ex.Message);
                VsShellUtilities.ShowMessageBox(_package, ex.Message, "X++ AI Error",
                    OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }
    }
}
