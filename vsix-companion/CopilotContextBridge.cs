using System;
using System.IO;

namespace XppAiCopilotCompanion
{
    // Companion bridge called by a Copilot context provider.
    // Uses in-process Visual Studio services only (no scripts/cmd).
    public sealed class CopilotContextBridge : ICopilotContextBridge
    {
        private const int MaxContextChars = 24000;
        private readonly IXppContextPipelineService _pipeline;

        public CopilotContextBridge(IXppContextPipelineService pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public string BuildContext(string workspaceRoot, string query)
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot))
            {
                throw new ArgumentException("workspaceRoot is required", nameof(workspaceRoot));
            }

            string safeQuery = string.IsNullOrWhiteSpace(query) ? "current change impact analysis" : query;
            XppContextBundle bundle = _pipeline.BuildBundle(workspaceRoot, safeQuery);

            // Prepend the X++ instructions from the embedded resource so Copilot
            // always receives the language rules regardless of which repo is open.
            string instructions = XppInstructionsProvider.GetSystemPrompt();
            string payload = bundle.ToDisplayText();

            string full = string.IsNullOrWhiteSpace(instructions)
                ? payload
                : instructions + "\n\n" + payload;

            if (full.Length > MaxContextChars)
            {
                return full.Substring(0, MaxContextChars) + "\n\n[Context truncated for UI limits]";
            }

            return full;
        }
    }
}
