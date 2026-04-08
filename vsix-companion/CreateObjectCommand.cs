using System;
using System.ComponentModel.Design;
using System.IO;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace XppAiCopilotCompanion
{
    internal sealed class CreateObjectCommand
    {
        private readonly AsyncPackage _package;

        private CreateObjectCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            var cmdId = new CommandID(PackageGuids.CommandSetGuid, CommandIds.CreateObject);
            commandService.AddCommand(new MenuCommand(Execute, cmdId));
        }

        public static CreateObjectCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var cmdService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new CreateObjectCommand(package, cmdService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string typeStr = PromptDialog.Show(
                "X++ Create Object",
                "Object type:\n"
                + "  Data: AxClass, AxTable, AxTableExtension, AxView, AxDataEntityView,\n"
                + "        AxCompositeDataEntityView, AxMap, AxEdt, AxEdtExtension,\n"
                + "        AxEnum, AxEnumExtension\n"
                + "  UI:   AxForm, AxFormExtension, AxTile\n"
                + "  Menu: AxMenu, AxMenuExtension, AxMenuItemDisplay,\n"
                + "        AxMenuItemOutput, AxMenuItemAction\n"
                + "  Query: AxQuery, AxQuerySimpleExtension\n"
                + "  Security: AxSecurityPrivilege, AxSecurityDuty,\n"
                + "           AxSecurityRole, AxSecurityPolicy\n"
                + "  Services: AxService, AxServiceGroup\n"
                + "  Workflow: AxWorkflowCategory, AxWorkflowType,\n"
                + "           AxWorkflowApproval, AxWorkflowTask,\n"
                + "           AxWorkflowAutomatedTask\n"
                + "  Analytics: AxSsrsReport, AxAggregateMeasurement,\n"
                + "           AxAggregateDimension, AxKpi\n"
                + "  Config: AxConfigurationKey, AxConfigurationKeyGroup,\n"
                + "          AxLicenseCode\n"
                + "  Other: AxNumberSequenceModule, AxResource",
                "AxClass");
            if (string.IsNullOrWhiteSpace(typeStr)) return;

            if (!Enum.TryParse<XppObjectType>(typeStr.Trim(), true, out var objectType))
            {
                VsShellUtilities.ShowMessageBox(_package,
                    "Invalid type. Enter any D365FO AOT metadata type (e.g. AxClass, AxTable, AxForm, AxEdt, AxEnum, AxView, AxDataEntityView, AxSecurityDuty, AxService, AxWorkflowType, etc.)",
                    "X++ AI", OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            string objectName = PromptDialog.Show(
                "X++ Create Object",
                "Object name:",
                "MyNewClass");
            if (string.IsNullOrWhiteSpace(objectName)) return;

            string userPrompt = PromptDialog.Show(
                "X++ Create Object",
                "Describe what this object should do (for AI generation):",
                "CoC extension for...");

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
                string solutionDir = vs.GetActiveSolutionDirectory();
                string targetDir = solutionDir ?? ".";

                var result = workflow.GenerateAndCreateObject(
                    userPrompt ?? string.Empty,
                    objectType,
                    objectName.Trim(),
                    targetDir);

                OutputPane.Write("Created: " + result.FilePath);
                if (result.AddedToProject)
                    OutputPane.Write("Added to active project.");
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
