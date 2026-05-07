using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace XppAiCopilotCompanion
{
    public enum XppEnvironmentType
    {
        UDE,
        Onebox,
        CHE
    }

    public sealed class XppCopilotOptionsPage : DialogPage
    {
        [Category("Environment")]
        [DisplayName("Environment Type")]
        [Description("UDE = remote dev box (registry auto-detects PackagesLocalDirectory). Onebox / CHE = local install where all packages share a single directory.")]
        [DefaultValue(XppEnvironmentType.UDE)]
        public XppEnvironmentType EnvironmentType { get; set; } = XppEnvironmentType.UDE;

        [Category("Environment")]
        [DisplayName("Packages Local Directory")]
        [Description("Override path for the shared PackagesLocalDirectory on Onebox or CHE. Leave blank to use the default: Onebox = C:\\AOSService\\PackagesLocalDirectory, CHE = K:\\AOSService\\PackagesLocalDirectory. Ignored when Environment Type is UDE.")]
        [DefaultValue("")]
        public string PackagesLocalDirectory { get; set; } = string.Empty;

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
