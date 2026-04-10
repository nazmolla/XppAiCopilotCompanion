using System.Collections.Generic;

namespace XppAiCopilotCompanion.MetaModel
{
    // ── Metadata sub-DTOs ──

    public sealed class EnumValueDto
    {
        public string Name { get; set; }
        public int Value { get; set; }
        public string Label { get; set; }
    }

    public sealed class FieldDto
    {
        public string Name { get; set; }
        public string FieldType { get; set; }
        public string ExtendedDataType { get; set; }
        public string EnumType { get; set; }
        public string Label { get; set; }
    }

    public sealed class IndexDto
    {
        public string Name { get; set; }
        public bool AllowDuplicates { get; set; }
        public List<string> Fields { get; set; } = new List<string>();
    }

    public sealed class FieldGroupDto
    {
        public string Name { get; set; }
        public string Label { get; set; }
        public List<string> Fields { get; set; } = new List<string>();
    }

    public sealed class RelationConstraintDto
    {
        public string Field { get; set; }
        public string RelatedField { get; set; }
    }

    public sealed class RelationDto
    {
        public string Name { get; set; }
        public string RelatedTable { get; set; }
        public List<RelationConstraintDto> Constraints { get; set; } = new List<RelationConstraintDto>();
    }

    public sealed class EntryPointDto
    {
        public string Name { get; set; }
        public string ObjectType { get; set; }
        public string ObjectName { get; set; }
        public string Grant { get; set; }
    }

    // ── Request DTOs ──

    public sealed class CreateObjectRequest
    {
        public string ObjectType { get; set; }
        public string ObjectName { get; set; }
        public string Declaration { get; set; }
        public string[] Methods { get; set; }
        public string ModelName { get; set; }

        // Strongly-typed metadata (replaces MetadataXml)
        public Dictionary<string, string> Properties { get; set; }
        public List<EnumValueDto> EnumValues { get; set; }
        public List<FieldDto> Fields { get; set; }
        public List<IndexDto> Indexes { get; set; }
        public List<FieldGroupDto> FieldGroups { get; set; }
        public List<RelationDto> Relations { get; set; }
        public List<EntryPointDto> EntryPoints { get; set; }
    }

    public sealed class UpdateObjectRequest
    {
        public string ObjectType { get; set; }
        public string ObjectName { get; set; }
        public string Declaration { get; set; }
        public string[] Methods { get; set; }
        public string[] RemoveMethodNames { get; set; }

        // Strongly-typed metadata (replaces MetadataXml)
        public Dictionary<string, string> Properties { get; set; }
        public List<EnumValueDto> EnumValues { get; set; }
        public List<FieldDto> Fields { get; set; }
        public List<IndexDto> Indexes { get; set; }
        public List<FieldGroupDto> FieldGroups { get; set; }
        public List<RelationDto> Relations { get; set; }
        public List<EntryPointDto> EntryPoints { get; set; }
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
        public string FilePath { get; set; }
        public bool IsCustom { get; set; }
        public string ModelName { get; set; }

        // Strongly-typed metadata (same shape as create/update for round-trip)
        public Dictionary<string, string> Properties { get; set; }
        public List<EnumValueDto> EnumValues { get; set; }
        public List<FieldDto> Fields { get; set; }
        public List<IndexDto> Indexes { get; set; }
        public List<FieldGroupDto> FieldGroups { get; set; }
        public List<RelationDto> Relations { get; set; }
        public List<EntryPointDto> EntryPoints { get; set; }
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
