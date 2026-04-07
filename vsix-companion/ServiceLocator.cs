using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;

namespace XppAiCopilotCompanion
{
    /// <summary>
    /// Resolves MEF exports from the VS composition container.
    /// </summary>
    internal static class ServiceLocator
    {
        private static IComponentModel GetComponentModel()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
        }

        public static IVisualStudioSessionService GetVisualStudioSession()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetComponentModel().GetService<IVisualStudioSessionService>();
        }

        public static IXppUiSettingsService GetSettingsService()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetComponentModel().GetService<IXppUiSettingsService>();
        }

        public static IAiCodeGenerationService GetAiService()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetComponentModel().GetService<IAiCodeGenerationService>();
        }
    }
}
