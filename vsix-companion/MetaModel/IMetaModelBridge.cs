using System.Collections.Generic;

namespace XppAiCopilotCompanion.MetaModel
{
    /// <summary>
    /// Abstraction over the D365FO IMetaModelService API.
    /// All metadata operations (create, read, update, delete, search)
    /// go through this interface. Implemented by <see cref="MetaModelBridge"/>
    /// which wraps the real IMetaModelService available inside VS.
    /// </summary>
    public interface IMetaModelBridge
    {
        // ── Object CRUD ──

        MetaModelResult CreateObject(CreateObjectRequest request);
        MetaModelResult UpdateObject(UpdateObjectRequest request);
        MetaModelResult DeleteObject(string objectType, string objectName, string modelName);
        ReadObjectResult ReadObject(string objectType, string objectName);
        ReadObjectResult ReadObjectByPath(string filePath);

        // ── Discovery ──

        FindObjectResult FindObject(string objectName, string objectType, bool exactMatch);
        ListObjectsResult ListObjects(string modelName, string objectType, string nameFilter, int maxResults);
        ModelInfoResult GetModelInfo(string modelName);
        List<ModelSummary> ListModels();

        // ── Labels ──

        LabelResult ReadLabel(string labelFileId, string language, string labelId, string searchText, int maxResults);
        MetaModelResult CreateLabel(string labelFileId, string language, string labelId, string text, string comment);

        // ── Project ──

        MetaModelResult AddToProject(string objectType, string objectName);
        List<ProjectItemInfo> ListProjectItems();

        // ── Environment ──

        EnvironmentInfo GetEnvironmentInfo();
    }
}
