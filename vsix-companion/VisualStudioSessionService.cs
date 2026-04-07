using System;
using System.ComponentModel.Composition;
using System.IO;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace XppAiCopilotCompanion
{
    [Export(typeof(IVisualStudioSessionService))]
    public sealed class VisualStudioSessionService : IVisualStudioSessionService
    {
        private DTE2 GetDte()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return Package.GetGlobalService(typeof(DTE)) as DTE2;
        }

        public string GetActiveDocumentPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return GetDte()?.ActiveDocument?.FullName; }
            catch { return null; }
        }

        public string GetActiveDocumentText()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var doc = GetDte()?.ActiveDocument;
                if (doc == null) return null;
                var textDoc = doc.Object("TextDocument") as TextDocument;
                if (textDoc == null) return null;
                return textDoc.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint);
            }
            catch { return null; }
        }

        public string GetActiveSolutionDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                string slnPath = GetDte()?.Solution?.FullName;
                return !string.IsNullOrEmpty(slnPath)
                    ? Path.GetDirectoryName(slnPath)
                    : Path.GetDirectoryName(GetActiveDocumentPath());
            }
            catch { return null; }
        }

        public void ReplaceActiveDocumentText(string newContent)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var doc = GetDte()?.ActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active document.");
            var textDoc = doc.Object("TextDocument") as TextDocument;
            if (textDoc == null) throw new InvalidOperationException("Not a text document.");
            var ep = textDoc.StartPoint.CreateEditPoint();
            ep.ReplaceText(textDoc.EndPoint, newContent,
                (int)vsEPReplaceTextOptions.vsEPReplaceTextAutoformat);
        }

        public void AddExistingFileToActiveProject(string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = GetDte();
            var projects = dte?.ActiveSolutionProjects as Array;
            if (projects == null || projects.Length == 0)
                throw new InvalidOperationException("No active project.");
            var project = projects.GetValue(0) as Project;
            project?.ProjectItems.AddFromFile(filePath);
        }
    }
}
