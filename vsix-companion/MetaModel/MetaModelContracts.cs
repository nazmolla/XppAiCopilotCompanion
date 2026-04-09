using System.Collections.Generic;

namespace XppAiCopilotCompanion.MetaModel
{
    // ── Request DTOs ──

    public sealed class CreateObjectRequest
    {
        public string ObjectType { get; set; }
        public string ObjectName { get; set; }
        public string Declaration { get; set; }
        public string[] Methods { get; set; }
        public string MetadataXml { get; set; }
        public string ModelName { get; set; }
    }

    public sealed class UpdateObjectRequest
    {
        public string ObjectType { get; set; }
        public string ObjectName { get; set; }
        public string Declaration { get; set; }
        public string[] Methods { get; set; }
        public string[] RemoveMethodNames { get; set; }
        public string MetadataXml { get; set; }
    }

    // ── Result DTOs ──

    public sealed class MetaModelResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string FilePath { get; set; }
    }

    public sealed class ReadObjectResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string ObjectType { get; set; }
        public string ObjectName { get; set; }
        public string Declaration { get; set; }
        public List<MethodInfo> Methods { get; set; } = new List<MethodInfo>();
        public string MetadataXml { get; set; }
        public string FilePath { get; set; }
        public bool IsCustom { get; set; }
        public string ModelName { get; set; }
    }

    public sealed class MethodInfo
    {
        public string Name { get; set; }
        public string Source { get; set; }
    }

    public sealed class FindObjectResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<FoundObject> Matches { get; set; } = new List<FoundObject>();
    }

    public sealed class FoundObject
    {
        public string ObjectType { get; set; }
        public string ObjectName { get; set; }
        public string ModelName { get; set; }
        public string PackageName { get; set; }
        public string FilePath { get; set; }
        public bool IsCustom { get; set; }
    }

    public sealed class ListObjectsResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<FoundObject> Objects { get; set; } = new List<FoundObject>();
    }

    public sealed class ModelInfoResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Publisher { get; set; }
        public string Version { get; set; }
        public string Layer { get; set; }
        public long ModelId { get; set; }
        public List<string> Dependencies { get; set; } = new List<string>();
        public Dictionary<string, int> ObjectCounts { get; set; } = new Dictionary<string, int>();
    }

    public sealed class ModelSummary
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string PackageName { get; set; }
        public bool IsCustom { get; set; }
    }

    public sealed class LabelResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<LabelEntry> Labels { get; set; } = new List<LabelEntry>();
    }

    public sealed class LabelEntry
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public string Comment { get; set; }
    }

    public sealed class ProjectItemInfo
    {
        public string Name { get; set; }
        public string ObjectType { get; set; }
        public string FilePath { get; set; }
    }

    public sealed class EnvironmentInfo
    {
        public string CustomMetadataFolder { get; set; }
        public List<string> ReferenceMetadataFolders { get; set; } = new List<string>();
        public string ActiveProjectName { get; set; }
        public string ActiveModelName { get; set; }
        public long ActiveModelId { get; set; }
        public string ActiveModelLayer { get; set; }
    }
}
