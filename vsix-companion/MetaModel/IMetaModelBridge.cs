using System.Collections.Generic;

namespace XppAiCopilotCompanion.MetaModel
{
    /// <summary>
    /// Abstraction over the D365FO IMetaModelService API.
    /// All metadata operations (create, read, update, search)
    /// go through this interface. Implemented by <see cref="MetaModelBridge"/>
    /// which wraps the real IMetaModelService available inside VS.
    /// </summary>
    public interface IMetaModelBridge
    {
        // ── Object CRUD ──

        MetaModelResult CreateObject(CreateObjectRequest request);
        MetaModelResult UpdateObject(UpdateObjectRequest request);
        ReadObjectResult ReadObject(string objectType, string objectName);
        ReadObjectResult ReadObjectByPath(string filePath);
        ValidateObjectResult ValidateObject(ValidateObjectRequest request);

        // ── Discovery ──

        FindObjectResult FindObject(string objectName, string objectType, bool exactMatch, int maxResults);
        ListObjectsResult ListObjects(string modelName, string objectType, string nameFilter, int maxResults);
        FindReferencesResult FindReferences(string objectType, string objectName, string memberName, string referenceKind, int maxResults);
        ObjectTypeSchemaResult GetObjectTypeSchema(string objectType);
        ProxyGenerationResult GenerateProxies(ProxyGenerationRequest request);
        ModelInfoResult GetModelInfo(string modelName);
        List<ModelSummary> ListModels();
        FormLayoutResult GetFormLayout(string formName, string rootControl, int maxDepth);

        // ── Labels ──

        LabelResult ReadLabel(string labelFileId, string language, string labelId, string searchText, int maxResults);
        MetaModelResult CreateLabel(string labelFileId, string language, string labelId, string text, string comment);
        MetaModelResult CreateLabels(string labelFileId, string language, List<LabelRequestDto> labels);

        // ── Project ──

        MetaModelResult AddToProject(string objectType, string objectName);
        List<ProjectItemInfo> ListProjectItems();
        bool HasActiveProject { get; }
        string RemoveFromProjectJson(string objectType, string objectName);
        string ListProjectsJson(string searchFolder);
        string SelectProjectJson(string projectFile);
        string DeleteObjectJson(string objectType, string objectName, bool checkOnly = false);
        string BestPracticeReport(string objectType, string objectName);
        string BuildJson(string modelName, bool projectOnly, bool syncDatabase);
        MetaModelBridge.BuildPlan PrepareBuild(string modelName, bool projectOnly);
        string RunBuildJson(MetaModelBridge.BuildPlan plan, bool syncDatabase);

        // ── Environment ──

        EnvironmentInfo GetEnvironmentInfo();
    }
}
