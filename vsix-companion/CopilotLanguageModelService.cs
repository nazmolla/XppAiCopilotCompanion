using System;
using System.ComponentModel.Composition;
using System.Text;
using Microsoft.VisualStudio.Shell;

namespace XppAiCopilotCompanion
{
    /// <summary>
    /// AI service that composes the full prompt (system + context + user request) and
    /// delegates to the VS Copilot language model API when available.
    /// Falls back to returning the composed prompt for manual use in Copilot Chat.
    /// </summary>
    [Export(typeof(IAiCodeGenerationService))]
    public sealed class CopilotLanguageModelService : IAiCodeGenerationService
    {
        private readonly IVisualStudioSessionService _vsSession;

        [ImportingConstructor]
        public CopilotLanguageModelService(IVisualStudioSessionService vsSession)
        {
            _vsSession = vsSession ?? throw new ArgumentNullException(nameof(vsSession));
        }

        public AiCodeResponse GenerateXppCode(AiCodeRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            string systemPrompt = request.SystemPrompt ?? XppInstructionsProvider.GetSystemPrompt();
            string userMessage = BuildUserMessage(request);

            // Try the VS Copilot language model API
            string generated = TryCallLanguageModel(systemPrompt, userMessage);

            if (generated != null)
            {
                return new AiCodeResponse
                {
                    GeneratedCode = generated,
                    Notes = "Generated via Copilot language model."
                };
            }

            // Fallback: return the full prompt for manual use in Copilot Chat
            return new AiCodeResponse
            {
                GeneratedCode = null,
                Notes = "Copilot language model not available. Copy this prompt into Copilot Chat:\n\n"
                        + systemPrompt + "\n\n" + userMessage
            };
        }

        private static string BuildUserMessage(AiCodeRequest request)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(request.ContextPayload))
            {
                sb.AppendLine("=== PROJECT CONTEXT ===");
                sb.AppendLine(request.ContextPayload);
                sb.AppendLine("=== END CONTEXT ===");
                sb.AppendLine();
            }

            sb.AppendLine("Request: " + (request.UserPrompt ?? "Generate X++ code"));
            sb.AppendLine();
            sb.AppendLine("Return only X++ code. No markdown fences. No explanation.");
            return sb.ToString();
        }

        private string TryCallLanguageModel(string systemPrompt, string userMessage)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                // The VS Copilot language model API (ILanguageModelBroker) is
                // internal/preview in VS 2022 17.x. When it becomes stable:
                //   var broker = ServiceProvider.GlobalProvider.GetService(typeof(SLanguageModelBroker));
                //   var result = await broker.CompleteAsync(systemPrompt, userMessage);
                //   return result.Text;

                // For now, return null to trigger the fallback path.
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
