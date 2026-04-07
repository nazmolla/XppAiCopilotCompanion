namespace XppAiCopilotCompanion
{
    public sealed class AiCodeRequest
    {
        public string UserPrompt { get; set; }
        public string ContextPayload { get; set; }

        /// <summary>
        /// X++ language instructions loaded from the embedded resource.
        /// Implementations should use this as the system message / preamble.
        /// </summary>
        public string SystemPrompt { get; set; }
    }

    public sealed class AiCodeResponse
    {
        public string GeneratedCode { get; set; }
        public string Notes { get; set; }
    }

    public interface IAiCodeGenerationService
    {
        AiCodeResponse GenerateXppCode(AiCodeRequest request);
    }
}
