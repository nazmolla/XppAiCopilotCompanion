using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace XppAiCopilotCompanion
{
    // Pure in-process pipeline. No cmd/ps scripts.
    public sealed class XppContextPipelineService : IXppContextPipelineService
    {
        private readonly IVisualStudioSessionService _vs;
        private readonly IXppUiSettingsService _settings;

        public XppContextPipelineService(IVisualStudioSessionService vs, IXppUiSettingsService settings)
        {
            _vs = vs ?? throw new ArgumentNullException(nameof(vs));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public XppContextBundle BuildBundle(string workspaceRoot, string query)
        {
            var cfg = _settings.GetSettings() ?? new XppUiSettings();
            var bundle = new XppContextBundle
            {
                Query = query ?? string.Empty,
                ActiveDocumentPath = _vs.GetActiveDocumentPath(),
                ActiveDocumentText = _vs.GetActiveDocumentText()
            };

            var roots = ResolveRoots(workspaceRoot, cfg);
            var entries = Crawl(roots, cfg.IncludeSamples);
            var selected = Select(entries, query, bundle.ActiveDocumentPath, cfg.TopN, cfg.MinCustom, cfg.MinReference);

            foreach (var e in selected)
            {
                bundle.Items.Add(new XppContextItem
                {
                    Name = e.Name,
                    Type = e.Type,
                    FilePath = e.FilePath,
                    SourceKind = e.SourceKind,
                    Reason = e.Reason,
                    Score = e.Score,
                    Snippet = e.Snippet
                });
            }

            return bundle;
        }

        private static List<(string Path, string Kind)> ResolveRoots(string workspaceRoot, XppUiSettings cfg)
        {
            var result = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void add(string p, string kind)
            {
                if (string.IsNullOrWhiteSpace(p) || !Directory.Exists(p)) return;
                if (seen.Add(p)) result.Add((p, kind));
            }

            add(workspaceRoot, "workspace");
            foreach (var c in cfg.CustomMetadataRoots) add(c, "custom");
            foreach (var r in cfg.ReferenceMetadataRoots) add(r, "reference_ms");

            return result;
        }

        private static List<Entry> Crawl(List<(string Path, string Kind)> roots, bool includeSamples)
        {
            var list = new List<Entry>();
            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots)
            {
                foreach (var file in Directory.EnumerateFiles(root.Path, "*.xml", SearchOption.AllDirectories))
                {
                    if (file.IndexOf("\\bin\\AIContext\\", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (file.IndexOf("Microsoft.Dynamics.FinOps.ToolsVS2022", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (!includeSamples && file.IndexOf("\\samples\\", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (!seenFiles.Add(file)) continue;

                    try
                    {
                        var e = Parse(file, root.Kind);
                        if (e != null) list.Add(e);
                    }
                    catch
                    {
                        // Ignore malformed/unsupported xml objects.
                    }
                }
            }

            return list;
        }

        private static Entry Parse(string filePath, string rootKind)
        {
            var raw = File.ReadAllText(filePath);
            var doc = new XmlDocument();
            doc.LoadXml(raw);
            var root = doc.DocumentElement;
            if (root == null) return null;

            var ns = new XmlNamespaceManager(doc.NameTable);
            bool hasNs = !string.IsNullOrWhiteSpace(root.NamespaceURI);
            if (hasNs) ns.AddNamespace("d", root.NamespaceURI);

            string name = hasNs
                ? doc.SelectSingleNode("//d:Name", ns)?.InnerText
                : doc.SelectSingleNode("//Name", ns)?.InnerText;

            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(filePath);

            string declaration = hasNs
                ? doc.SelectSingleNode("//d:Declaration", ns)?.InnerText
                : doc.SelectSingleNode("//Declaration", ns)?.InnerText;

            var methodNodes = hasNs ? doc.SelectNodes("//d:Method/d:Source", ns) : doc.SelectNodes("//Method/Source", ns);
            var methodText = string.Join("\n", methodNodes.Cast<XmlNode>().Select(n => n.InnerText));
            var joined = ((declaration ?? string.Empty) + "\n" + methodText).Trim();

            return new Entry
            {
                Name = name,
                Type = root.LocalName,
                FilePath = filePath,
                SourceKind = rootKind,
                Snippet = joined.Length > 1400 ? joined.Substring(0, 1400) : joined,
                Tokens = Regex.Matches((name + " " + joined), "[A-Za-z_][A-Za-z0-9_]{2,}")
                    .Cast<Match>()
                    .Select(m => m.Value.ToLowerInvariant())
                    .Distinct()
                    .ToList()
            };
        }

        private static List<Entry> Select(List<Entry> entries, string query, string activeDoc, int topN, int minCustom, int minReference)
        {
            var qTokens = Regex.Matches(query ?? string.Empty, "[A-Za-z_][A-Za-z0-9_]{2,}")
                .Cast<Match>()
                .Select(m => m.Value.ToLowerInvariant())
                .Distinct()
                .ToList();

            foreach (var e in entries)
            {
                int overlap = qTokens.Count(t => e.Tokens.Contains(t));
                int score = overlap * 5;
                if (qTokens.Contains(e.Name.ToLowerInvariant())) score += 20;
                if (!string.IsNullOrWhiteSpace(activeDoc) && string.Equals(activeDoc, e.FilePath, StringComparison.OrdinalIgnoreCase)) score += 30;
                if (e.SourceKind == "custom") score += 6;
                if (e.SourceKind == "reference_ms") score += 6;
                if (e.SourceKind == "workspace") score += 2;

                e.Score = score;
                e.Reason = "token_overlap:" + overlap + (string.Equals(activeDoc, e.FilePath, StringComparison.OrdinalIgnoreCase) ? ", active_file" : string.Empty);
            }

            var ranked = entries.Where(e => e.Score > 0).OrderByDescending(e => e.Score).ToList();
            if (ranked.Count == 0) return new List<Entry>();

            var result = new List<Entry>();
            AddQuota(result, ranked, "custom", minCustom);
            AddQuota(result, ranked, "reference_ms", minReference);

            foreach (var e in ranked)
            {
                if (result.Count >= topN) break;
                if (!result.Contains(e)) result.Add(e);
            }

            return result.Take(topN).ToList();
        }

        private static void AddQuota(List<Entry> output, List<Entry> ranked, string kind, int minCount)
        {
            if (minCount <= 0) return;
            foreach (var e in ranked.Where(r => r.SourceKind == kind))
            {
                if (output.Count(o => o.SourceKind == kind) >= minCount) break;
                if (!output.Contains(e)) output.Add(e);
            }
        }

        private sealed class Entry
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string FilePath { get; set; }
            public string SourceKind { get; set; }
            public string Snippet { get; set; }
            public List<string> Tokens { get; set; }
            public int Score { get; set; }
            public string Reason { get; set; }
        }
    }
}
