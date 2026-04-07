using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace XppAiCopilotCompanion
{
    public sealed class XppCopilotOptionsPage : DialogPage
    {
        [Category("Metadata Roots")]
        [DisplayName("Custom Metadata Roots")]
        [Description("Semicolon-separated paths to custom X++ metadata directories. Leave empty to auto-detect from the active D365FO configuration.")]
        public string CustomMetadataRoots { get; set; } = string.Empty;

        [Category("Metadata Roots")]
        [DisplayName("Reference (MS) Metadata Roots")]
        [Description("Semicolon-separated paths to Microsoft reference metadata directories. Leave empty to auto-detect from the active D365FO configuration.")]
        public string ReferenceMetadataRoots { get; set; } = string.Empty;

        [Category("Context Budget")]
        [DisplayName("Max Context Tokens")]
        [Description("Approximate token budget for the full AI context payload.")]
        public int MaxContextTokens { get; set; } = 6000;

        [Category("Context Budget")]
        [DisplayName("Max Snippet Tokens Per Object")]
        [Description("Token limit per individual object snippet in context.")]
        public int MaxSnippetTokensPerObject { get; set; } = 350;

        [Category("Context Budget")]
        [DisplayName("Max Active Document Tokens")]
        [Description("Token budget for the currently open file in context.")]
        public int MaxActiveDocTokens { get; set; } = 1000;

        [Category("Selection")]
        [DisplayName("Top N Objects")]
        [Description("Maximum number of relevant objects to include in context.")]
        public int TopN { get; set; } = 20;

        [Category("Selection")]
        [DisplayName("Min Custom Objects")]
        [Description("Minimum custom-source objects to guarantee in context.")]
        public int MinCustom { get; set; } = 3;

        [Category("Selection")]
        [DisplayName("Min Reference Objects")]
        [Description("Minimum MS-reference objects to guarantee in context.")]
        public int MinReference { get; set; } = 3;

        [Category("Selection")]
        [DisplayName("Include Samples")]
        [Description("Include files from \\samples\\ directories.")]
        public bool IncludeSamples { get; set; } = false;
    }
}
