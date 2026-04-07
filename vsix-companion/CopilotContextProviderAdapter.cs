using System;

namespace XppAiCopilotCompanion
{
    // Adapter layer for wiring this companion into the host Copilot provider implementation.
    // A concrete VS integration can map Copilot request fields to these methods.
    public sealed class CopilotContextProviderAdapter
    {
        private readonly ICopilotContextBridge _bridge;
        private readonly IVisualStudioSessionService _vsSession;

        public CopilotContextProviderAdapter(ICopilotContextBridge bridge, IVisualStudioSessionService vsSession)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _vsSession = vsSession ?? throw new ArgumentNullException(nameof(vsSession));
        }

        public string GetContextPayload(string workspaceRoot, string userQuery)
        {
            string query = string.IsNullOrWhiteSpace(userQuery)
                ? "current change impact analysis"
                : userQuery;

            string activeDoc = _vsSession.GetActiveDocumentPath();
            if (!string.IsNullOrWhiteSpace(activeDoc))
            {
                query = query + " active_file:" + activeDoc;
            }

            return _bridge.BuildContext(workspaceRoot, query);
        }

        public void ReplaceActiveDocumentText(string newContent)
        {
            _vsSession.ReplaceActiveDocumentText(newContent);
        }

        public void AddExistingFileToActiveProject(string filePath)
        {
            _vsSession.AddExistingFileToActiveProject(filePath);
        }
    }
}
