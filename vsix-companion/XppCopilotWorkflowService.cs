using System;

namespace XppAiCopilotCompanion
{
    // End-to-end UI workflow: get context from open VS state, ask AI service, apply or create artifacts.
    public sealed class XppCopilotWorkflowService
    {
        private readonly IXppCopilotUiController _uiController;
        private readonly IAiCodeGenerationService _ai;
        private readonly IXppObjectCreationService _objectCreator;

        public XppCopilotWorkflowService(
            IXppCopilotUiController uiController,
            IAiCodeGenerationService ai,
            IXppObjectCreationService objectCreator)
        {
            _uiController = uiController ?? throw new ArgumentNullException(nameof(uiController));
            _ai = ai ?? throw new ArgumentNullException(nameof(ai));
            _objectCreator = objectCreator ?? throw new ArgumentNullException(nameof(objectCreator));
        }

        public string GenerateAndApplyToOpenDocument(string userPrompt)
        {
            string context = _uiController.RefreshContextFromUi(userPrompt);
            var response = _ai.GenerateXppCode(new AiCodeRequest
            {
                UserPrompt = userPrompt,
                ContextPayload = context,
                SystemPrompt = XppInstructionsProvider.GetSystemPrompt()
            });

            _uiController.ApplyAiEditToOpenDocument(response.GeneratedCode ?? string.Empty);
            return response.Notes ?? "AI edit applied to active document.";
        }

        public XppObjectCreateResult GenerateAndCreateObject(
            string userPrompt,
            XppObjectType objectType,
            string objectName,
            string targetDirectory)
        {
            string context = _uiController.RefreshContextFromUi(userPrompt);
            var response = _ai.GenerateXppCode(new AiCodeRequest
            {
                UserPrompt = userPrompt,
                ContextPayload = context,
                SystemPrompt = XppInstructionsProvider.GetSystemPrompt()
            });

            var result = _objectCreator.CreateObject(new XppObjectCreateRequest
            {
                ObjectType = objectType,
                ObjectName = objectName,
                TargetDirectory = targetDirectory,
                AddToActiveProject = true,
                SuggestedCode = response.GeneratedCode
            });

            return result;
        }
    }
}
