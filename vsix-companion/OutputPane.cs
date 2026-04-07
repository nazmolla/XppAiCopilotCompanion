using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace XppAiCopilotCompanion
{
    /// <summary>
    /// Writes messages to the "X++ AI Copilot" output window pane.
    /// </summary>
    internal static class OutputPane
    {
        private static readonly Guid PaneGuid = new Guid("d1e2f3a4-b5c6-7d8e-9f01-2a3b4c5d6e7f");
        private static Guid _paneRef = PaneGuid;

        public static void Write(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (outputWindow == null) return;

            outputWindow.CreatePane(ref _paneRef, "X++ AI Copilot", 1, 1);
            outputWindow.GetPane(ref _paneRef, out var pane);
            pane?.OutputStringThreadSafe(message + Environment.NewLine);
            pane?.Activate();
        }
    }
}
