using System;

namespace XppAiCopilotCompanion
{
    public sealed class XppCopilotUiController : IXppCopilotUiController
    {
        private readonly CopilotContextProviderAdapter _adapter;
        private readonly IVisualStudioSessionService _vsSession;

        public XppCopilotUiController(CopilotContextProviderAdapter adapter, IVisualStudioSessionService vsSession)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _vsSession = vsSession ?? throw new ArgumentNullException(nameof(vsSession));
        }

        public string RefreshContextFromUi(string userQuery)
        {
            string workspaceRoot = _vsSession.GetActiveSolutionDirectory();
            return _adapter.GetContextPayload(workspaceRoot, userQuery);
        }

        public void ApplyAiEditToOpenDocument(string newContent)
        {
            _adapter.ReplaceActiveDocumentText(newContent);
        }

        public void AddFileToActiveProject(string filePath)
        {
            _adapter.AddExistingFileToActiveProject(filePath);
        }
    }
}
