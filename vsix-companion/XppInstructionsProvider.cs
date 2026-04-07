using System.IO;
using System.Reflection;

namespace XppAiCopilotCompanion
{
    /// <summary>
    /// Loads the X++ system prompt from the embedded resource compiled into this assembly.
    /// The prompt is read once and cached for the lifetime of the VS session.
    /// </summary>
    internal static class XppInstructionsProvider
    {
        private const string ResourceName = "XppAiCopilotCompanion.XppCopilotSystemPrompt.txt";
        private static string _cached;

        /// <summary>
        /// Returns the full X++ system prompt text embedded in the VSIX assembly.
        /// </summary>
        public static string GetSystemPrompt()
        {
            if (_cached != null) return _cached;

            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                    return string.Empty; // resource missing — graceful fallback

                using (var reader = new StreamReader(stream))
                {
                    _cached = reader.ReadToEnd();
                }
            }

            return _cached;
        }
    }
}
