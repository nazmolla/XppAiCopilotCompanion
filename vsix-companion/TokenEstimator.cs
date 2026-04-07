namespace XppAiCopilotCompanion
{
    /// <summary>
    /// Rough token estimator: ~4 chars per token for code/X++ content.
    /// Avoids pulling in a tokenizer dependency.
    /// </summary>
    public static class TokenEstimator
    {
        private const int CharsPerToken = 4;

        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return (text.Length + CharsPerToken - 1) / CharsPerToken;
        }

        public static int TokensToChars(int tokens)
        {
            return tokens * CharsPerToken;
        }

        public static string TrimToTokenBudget(string text, int maxTokens)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            int maxChars = TokensToChars(maxTokens);
            if (text.Length <= maxChars) return text;
            return text.Substring(0, maxChars);
        }
    }
}
