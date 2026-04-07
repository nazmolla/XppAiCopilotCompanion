using System.Collections.Generic;
using System.Text;

namespace XppAiCopilotCompanion
{
    public sealed class XppContextBundle
    {
        public string Query { get; set; }
        public string ActiveDocumentPath { get; set; }
        public string ActiveDocumentText { get; set; }
        public List<XppContextItem> Items { get; } = new List<XppContextItem>();

        public string ToDisplayText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("X++ Copilot Context");
            sb.AppendLine("Query: " + (Query ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(ActiveDocumentPath))
            {
                sb.AppendLine("Active File: " + ActiveDocumentPath);
            }
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(ActiveDocumentText))
            {
                sb.AppendLine("Active Document Snippet:");
                sb.AppendLine(TrimTo(ActiveDocumentText, 1800));
                sb.AppendLine();
            }

            sb.AppendLine("Relevant Objects:");
            foreach (var item in Items)
            {
                sb.AppendLine("- [" + item.Score + "] " + item.Name + " (" + item.Type + ", kind=" + item.SourceKind + ")");
                sb.AppendLine("  File: " + item.FilePath);
                sb.AppendLine("  Why: " + item.Reason);
                sb.AppendLine("  Snippet: " + TrimTo(item.Snippet, 500));
            }

            return sb.ToString();
        }

        private static string TrimTo(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= maxLen) return text;
            return text.Substring(0, maxLen) + "...";
        }
    }

    public sealed class XppContextItem
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string FilePath { get; set; }
        public string SourceKind { get; set; }
        public string Reason { get; set; }
        public int Score { get; set; }
        public string Snippet { get; set; }
    }
}
