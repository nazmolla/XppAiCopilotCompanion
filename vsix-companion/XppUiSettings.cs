using System.Collections.Generic;

namespace XppAiCopilotCompanion
{
    // Backed by a VS options page/tool window state. No hidden config files.
    public sealed class XppUiSettings
    {
        public List<string> CustomMetadataRoots { get; } = new List<string>();
        public List<string> ReferenceMetadataRoots { get; } = new List<string>();
        public int TopN { get; set; } = 20;
        public int MinCustom { get; set; } = 3;
        public int MinReference { get; set; } = 3;
        public bool IncludeSamples { get; set; } = false;

        // Token budget controls
        public int MaxContextTokens { get; set; } = 6000;
        public int MaxSnippetTokensPerObject { get; set; } = 350;
        public int MaxActiveDocTokens { get; set; } = 1000;
    }
}
