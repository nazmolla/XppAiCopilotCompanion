using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.VisualStudio.Shell;

namespace XppAiCopilotCompanion
{
    [Export(typeof(IXppUiSettingsService))]
    public sealed class XppUiSettingsService : IXppUiSettingsService
    {
        public XppUiSettings GetSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var package = XppCopilotPackage.Instance;
            if (package == null)
                return new XppUiSettings();

            var page = package.GetDialogPage(typeof(XppCopilotOptionsPage)) as XppCopilotOptionsPage;
            if (page == null)
                return new XppUiSettings();

            var settings = new XppUiSettings
            {
                TopN = page.TopN,
                MinCustom = page.MinCustom,
                MinReference = page.MinReference,
                IncludeSamples = page.IncludeSamples,
                MaxContextTokens = page.MaxContextTokens,
                MaxSnippetTokensPerObject = page.MaxSnippetTokensPerObject,
                MaxActiveDocTokens = page.MaxActiveDocTokens
            };

            if (!string.IsNullOrWhiteSpace(page.CustomMetadataRoots))
            {
                settings.CustomMetadataRoots.AddRange(
                    page.CustomMetadataRoots.Split(';')
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0));
            }

            if (!string.IsNullOrWhiteSpace(page.ReferenceMetadataRoots))
            {
                settings.ReferenceMetadataRoots.AddRange(
                    page.ReferenceMetadataRoots.Split(';')
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0));
            }

            // Fall back to the active D365FO configuration when the user hasn't set paths manually.
            if (settings.CustomMetadataRoots.Count == 0 || settings.ReferenceMetadataRoots.Count == 0)
            {
                var d365Config = DynamicsConfigurationReader.ReadActiveConfiguration();

                if (settings.CustomMetadataRoots.Count == 0 && d365Config.HasCustom)
                    settings.CustomMetadataRoots.Add(d365Config.CustomMetadataFolder);

                if (settings.ReferenceMetadataRoots.Count == 0 && d365Config.HasReference)
                    settings.ReferenceMetadataRoots.AddRange(d365Config.ReferenceMetadataFolders);
            }

            return settings;
        }
    }
}
