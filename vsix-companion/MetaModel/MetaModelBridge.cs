using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Dynamics.AX.Metadata.MetaModel;
using Microsoft.Dynamics.AX.Metadata.Core.MetaModel;
using Microsoft.Dynamics.AX.Metadata.Service;
using Microsoft.Dynamics.Framework.Tools.Extensibility;
using Microsoft.Dynamics.Framework.Tools.Labels;
using Microsoft.Dynamics.Framework.Tools.MetaModel.Core;
using Microsoft.Dynamics.Framework.Tools.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using EnvDTE;
using EnvDTE80;

namespace XppAiCopilotCompanion.MetaModel
{
    /// <summary>
    /// Wraps the D365FO IMetaModelService, accessed via CoreUtility.ServiceProvider.
    /// All metadata operations use strongly-typed APIs — no raw XML manipulation.
    /// Must be called from the VS main thread (or marshalled to it).
    /// </summary>
    public sealed class MetaModelBridge : IMetaModelBridge
    {
        private IMetaModelService _metaService;
        private IMetaModelProviders _metaProviders;

        private IMetaModelService MetaService
        {
            get
            {
                if (_metaService == null)
                {
                    _metaProviders = CoreUtility.ServiceProvider
                        .GetService(typeof(IMetaModelProviders)) as IMetaModelProviders;
                    _metaService = _metaProviders?.CurrentMetaModelService;
                }
                return _metaService;
            }
        }

        private DTE2 Dte =>
            CoreUtility.ServiceProvider.GetService(typeof(DTE)) as DTE2;

        // ── Object CRUD ──

        public MetaModelResult CreateObject(CreateObjectRequest request)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (MetaService == null)
                return Fail("MetaModel service not available. Is D365FO tools extension loaded?");

            var saveInfo = GetModelSaveInfo(request.ModelName);
            if (saveInfo == null)
                return Fail("Could not resolve model '" + request.ModelName + "'. Ensure a D365FO project is open.");

            try
            {
                switch (request.ObjectType)
                {
                    case "AxClass":
                        return CreateClass(request, saveInfo);
                    case "AxTable":
                        return CreateTable(request, saveInfo);
                    case "AxForm":
                        return CreateForm(request, saveInfo);
                    case "AxEdt":
                        return CreateEdt(request, saveInfo);
                    case "AxEnum":
                        return CreateEnum(request, saveInfo);
                    case "AxMenuItemDisplay":
                        return CreateMenuItemDisplay(request, saveInfo);
                    case "AxMenuItemOutput":
                        return CreateMenuItemOutput(request, saveInfo);
                    case "AxMenuItemAction":
                        return CreateMenuItemAction(request, saveInfo);
                    case "AxQuery":
                        return CreateQuery(request, saveInfo);
                    case "AxView":
                        return CreateView(request, saveInfo);
                    case "AxDataEntityView":
                        return CreateDataEntityView(request, saveInfo);
                    case "AxSecurityPrivilege":
                        return CreateSecurityPrivilege(request, saveInfo);
                    case "AxSecurityDuty":
                        return CreateSecurityDuty(request, saveInfo);
                    case "AxSecurityRole":
                        return CreateSecurityRole(request, saveInfo);
                    case "AxService":
                        return CreateService(request, saveInfo);
                    case "AxServiceGroup":
                        return CreateServiceGroup(request, saveInfo);
                    case "AxMap":
                        return CreateMap(request, saveInfo);
                    case "AxMenu":
                        return CreateMenu(request, saveInfo);
                    case "AxTile":
                        return CreateTile(request, saveInfo);
                    case "AxConfigurationKey":
                        return CreateConfigurationKey(request, saveInfo);

                    // ── Extension types (no typed Create API — uses XML serialization) ──
                    case "AxTableExtension":
                        return CreateExtensionObject(request, saveInfo, typeof(AxTableExtension), "AxTableExtension");
                    case "AxFormExtension":
                        return CreateExtensionObject(request, saveInfo, typeof(AxFormExtension), "AxFormExtension");
                    case "AxEnumExtension":
                        return CreateExtensionObject(request, saveInfo, typeof(AxEnumExtension), "AxEnumExtension");
                    case "AxEdtExtension":
                        return CreateExtensionObject(request, saveInfo, typeof(AxEdtExtension), "AxEdtExtension");
                    case "AxViewExtension":
                        return CreateExtensionObject(request, saveInfo, typeof(AxViewExtension), "AxViewExtension");
                    case "AxMenuExtension":
                        return CreateExtensionObject(request, saveInfo, typeof(AxMenuExtension), "AxMenuExtension");
                    case "AxMenuItemDisplayExtension":
                        return CreateExtensionObject(request, saveInfo, typeof(AxMenuItemDisplayExtension), "AxMenuItemDisplayExtension");
                    case "AxMenuItemOutputExtension":
                        return CreateExtensionObject(request, saveInfo, typeof(AxMenuItemOutputExtension), "AxMenuItemOutputExtension");
                    case "AxMenuItemActionExtension":
                        return CreateExtensionObject(request, saveInfo, typeof(AxMenuItemActionExtension), "AxMenuItemActionExtension");
                    case "AxQuerySimpleExtension":
                        return CreateExtensionObject(request, saveInfo, typeof(AxQuerySimpleExtension), "AxQuerySimpleExtension");
                    case "AxSecurityDutyExtension":
                        return CreateExtensionObject(request, saveInfo, typeof(AxSecurityDutyExtension), "AxSecurityDutyExtension");
                    case "AxSecurityRoleExtension":
                        return CreateExtensionObject(request, saveInfo, typeof(AxSecurityRoleExtension), "AxSecurityRoleExtension");

                    default:
                        return Fail("Unsupported object type: " + request.ObjectType);
                }
            }
            catch (Exception ex)
            {
                return Fail("Create failed: " + ex.Message);
            }
        }

        public MetaModelResult UpdateObject(UpdateObjectRequest request)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (MetaService == null)
                return Fail("MetaModel service not available.");

            try
            {
                switch (request.ObjectType)
                {
                    case "AxClass":
                        return UpdateClass(request);
                    case "AxTable":
                        return UpdateTable(request);
                    case "AxForm":
                        return UpdateForm(request);
                    case "AxEdt":
                        return UpdateEdt(request);
                    case "AxEnum":
                        return UpdateEnum(request);
                    default:
                        return UpdateGenericByXml(request);
                }
            }
            catch (Exception ex)
            {
                return Fail("Update failed: " + ex.Message);
            }
        }

        public MetaModelResult DeleteObject(string objectType, string objectName, string modelName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (MetaService == null)
                return Fail("MetaModel service not available.");

            var saveInfo = GetModelSaveInfo(modelName);
            if (saveInfo == null)
                return Fail("Could not resolve model '" + modelName + "'.");

            try
            {
                switch (objectType)
                {
                    case "AxClass": MetaService.DeleteClass(objectName, saveInfo); break;
                    case "AxTable": MetaService.DeleteTable(objectName, saveInfo); break;
                    case "AxForm": MetaService.DeleteForm(objectName, saveInfo); break;
                    case "AxEdt": MetaService.DeleteExtendedDataType(objectName, saveInfo); break;
                    case "AxEnum": MetaService.DeleteEnum(objectName, saveInfo); break;
                    case "AxMenuItemDisplay": MetaService.DeleteMenuItemDisplay(objectName, saveInfo); break;
                    case "AxMenuItemOutput": MetaService.DeleteMenuItemOutput(objectName, saveInfo); break;
                    case "AxMenuItemAction": MetaService.DeleteMenuItemAction(objectName, saveInfo); break;
                    case "AxQuery": MetaService.DeleteQuery(objectName, saveInfo); break;
                    case "AxView": MetaService.DeleteView(objectName, saveInfo); break;
                    case "AxDataEntityView": MetaService.DeleteDataEntityView(objectName, saveInfo); break;
                    case "AxSecurityPrivilege": MetaService.DeleteSecurityPrivilege(objectName, saveInfo); break;
                    case "AxSecurityDuty": MetaService.DeleteSecurityDuty(objectName, saveInfo); break;
                    case "AxSecurityRole": MetaService.DeleteSecurityRole(objectName, saveInfo); break;
                    case "AxService": MetaService.DeleteService(objectName, saveInfo); break;
                    case "AxServiceGroup": MetaService.DeleteServiceGroup(objectName, saveInfo); break;
                    case "AxMap": MetaService.DeleteMap(objectName, saveInfo); break;
                    case "AxMenu": MetaService.DeleteMenu(objectName, saveInfo); break;
                    case "AxTile": MetaService.DeleteTile(objectName, saveInfo); break;
                    case "AxConfigurationKey": MetaService.DeleteConfigurationKey(objectName, saveInfo); break;
                    default:
                        return Fail("Unsupported delete for type: " + objectType);
                }

                return new MetaModelResult { Success = true, Message = "Deleted " + objectType + " '" + objectName + "'." };
            }
            catch (Exception ex)
            {
                return Fail("Delete failed: " + ex.Message);
            }
        }

        public ReadObjectResult ReadObject(string objectType, string objectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (MetaService == null)
                return new ReadObjectResult { Message = "MetaModel service not available." };

            try
            {
                var result = new ReadObjectResult { ObjectType = objectType, ObjectName = objectName, Success = true };

                switch (objectType)
                {
                    case "AxClass":
                        var cls = MetaService.GetClass(objectName);
                        if (cls == null) return new ReadObjectResult { Message = "Class '" + objectName + "' not found." };
                        result.Declaration = cls.Declaration;
                        if (cls.Methods != null)
                            foreach (var m in cls.Methods)
                                result.Methods.Add(new MethodInfo { Name = m.Name, Source = m.Source });
                        result.Properties = ExtractProperties(cls);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxTable":
                        var tbl = MetaService.GetTable(objectName);
                        if (tbl == null) return new ReadObjectResult { Message = "Table '" + objectName + "' not found." };
                        result.Declaration = tbl.Declaration;
                        if (tbl.Methods != null)
                            foreach (var m in tbl.Methods)
                                result.Methods.Add(new MethodInfo { Name = m.Name, Source = m.Source });
                        result.Properties = ExtractProperties(tbl);
                        result.Fields = ExtractFields(tbl);
                        result.Indexes = ExtractIndexes(tbl);
                        result.FieldGroups = ExtractFieldGroups(tbl);
                        result.Relations = ExtractRelations(tbl);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxForm":
                        var frm = MetaService.GetForm(objectName);
                        if (frm == null) return new ReadObjectResult { Message = "Form '" + objectName + "' not found." };
                        result.Declaration = frm.SourceCode?.Declaration;
                        if (frm.Methods != null)
                            foreach (var m in frm.Methods)
                                result.Methods.Add(new MethodInfo { Name = m.Name, Source = m.Source });
                        result.Properties = ExtractProperties(frm);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxEnum":
                        var enm = MetaService.GetEnum(objectName);
                        if (enm == null) return new ReadObjectResult { Message = "Enum '" + objectName + "' not found." };
                        result.Properties = ExtractProperties(enm);
                        result.EnumValues = ExtractEnumValues(enm);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxEdt":
                        var edt = MetaService.GetExtendedDataType(objectName);
                        if (edt == null) return new ReadObjectResult { Message = "EDT '" + objectName + "' not found." };
                        result.Properties = ExtractProperties(edt);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxView":
                        var view = MetaService.GetView(objectName);
                        if (view == null) return new ReadObjectResult { Message = "View '" + objectName + "' not found." };
                        result.Declaration = view.Declaration;
                        if (view.Methods != null)
                            foreach (var m in view.Methods)
                                result.Methods.Add(new MethodInfo { Name = m.Name, Source = m.Source });
                        result.Properties = ExtractProperties(view);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxQuery":
                        var qry = MetaService.GetQuery(objectName);
                        if (qry == null) return new ReadObjectResult { Message = "Query '" + objectName + "' not found." };
                        result.Properties = ExtractProperties(qry);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    default:
                        return new ReadObjectResult { Message = "Read not supported for type: " + objectType + ". Supported types: AxClass, AxTable, AxForm, AxEnum, AxEdt, AxView, AxQuery. Use xpp_find_object to search by name." };
                }

                result.IsCustom = IsCustomModel(result.ModelName);
                return result;
            }
            catch (Exception ex)
            {
                return new ReadObjectResult { Message = "Read failed: " + ex.Message };
            }
        }

        public ReadObjectResult ReadObjectByPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return new ReadObjectResult { Message = "filePath is required." };
            if (!File.Exists(filePath))
                return new ReadObjectResult { Message = "File not found: " + filePath };

            // Derive type and name from the path structure:
            // <root>\<Package>\<Model>\AxClass\MyClass.xml → type=AxClass, name=MyClass
            string objectName = Path.GetFileNameWithoutExtension(filePath);
            string folder = Path.GetFileName(Path.GetDirectoryName(filePath) ?? "") ?? "";

            if (!string.IsNullOrEmpty(folder) && folder.StartsWith("Ax", StringComparison.OrdinalIgnoreCase))
            {
                var result = ReadObject(folder, objectName);
                if (result != null)
                {
                    result.FilePath = filePath;
                    return result;
                }
            }

            return new ReadObjectResult
            {
                Message = "Cannot read type '" + folder + "' via the MetaModel API. "
                        + "Supported types: AxClass, AxTable, AxForm, AxEnum, AxEdt, AxView, AxQuery. "
                        + "Use xpp_find_object to locate objects by name."
            };
        }

        // ── Validation ──

        public ValidateObjectResult ValidateObject(ValidateObjectRequest req)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var result = new ValidateObjectResult { ObjectType = req.ObjectType, ObjectName = req.ObjectName };

            // 1. Read the object via typed API
            var readResult = ReadObject(req.ObjectType, req.ObjectName);
            if (!readResult.Success)
            {
                result.Message = "Object not found: " + (readResult.Message ?? "unknown error");
                return result;
            }

            result.Exists = true;
            result.ModelName = readResult.ModelName;

            // 2. Check if in the active project (by name in filePath or item name)
            var projectItems = ListProjectItems();
            result.InProject = projectItems.Any(p =>
                (p.FilePath != null && p.FilePath.IndexOf(req.ObjectName, StringComparison.OrdinalIgnoreCase) >= 0) ||
                string.Equals(p.Name, req.ObjectName, StringComparison.OrdinalIgnoreCase));

            // 3. Compare expected vs actual properties
            var mismatches = new List<string>();

            if (req.Properties != null)
            {
                foreach (var kv in req.Properties)
                {
                    if (readResult.Properties == null || !readResult.Properties.TryGetValue(kv.Key, out string actual))
                        mismatches.Add("Property '" + kv.Key + "': expected '" + kv.Value + "', not found on object");
                    else if (!string.Equals(actual, kv.Value, StringComparison.OrdinalIgnoreCase))
                        mismatches.Add("Property '" + kv.Key + "': expected '" + kv.Value + "', actual '" + actual + "'");
                }
            }

            if (req.Fields != null)
            {
                foreach (var f in req.Fields)
                {
                    bool found = readResult.Fields != null &&
                        readResult.Fields.Any(x => string.Equals(x.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                    if (!found)
                        mismatches.Add("Field '" + f.Name + "': expected to exist, not found");
                }
            }

            if (req.EnumValues != null)
            {
                foreach (var ev in req.EnumValues)
                {
                    bool found = readResult.EnumValues != null &&
                        readResult.EnumValues.Any(x => string.Equals(x.Name, ev.Name, StringComparison.OrdinalIgnoreCase));
                    if (!found)
                        mismatches.Add("EnumValue '" + ev.Name + "': expected to exist, not found");
                }
            }

            if (req.Indexes != null)
            {
                foreach (var idx in req.Indexes)
                {
                    bool found = readResult.Indexes != null &&
                        readResult.Indexes.Any(x => string.Equals(x.Name, idx.Name, StringComparison.OrdinalIgnoreCase));
                    if (!found)
                        mismatches.Add("Index '" + idx.Name + "': expected to exist, not found");
                }
            }

            if (req.Relations != null)
            {
                foreach (var rel in req.Relations)
                {
                    bool found = readResult.Relations != null &&
                        readResult.Relations.Any(x => string.Equals(x.Name, rel.Name, StringComparison.OrdinalIgnoreCase));
                    if (!found)
                        mismatches.Add("Relation '" + rel.Name + "': expected to exist, not found");
                }
            }

            result.Mismatches = mismatches;
            result.Valid = result.InProject && mismatches.Count == 0;
            if (result.Valid)
                result.Message = "Validation passed. Object exists in project and all specified metadata is present.";
            else if (!result.InProject && mismatches.Count == 0)
                result.Message = "Object metadata is correct but it is NOT in the active project.";
            else
                result.Message = "Validation failed: " + mismatches.Count + " mismatch(es). InProject=" + result.InProject + ".";

            return result;
        }

        // ── Discovery ──

        public FindObjectResult FindObject(string objectName, string objectType, bool exactMatch)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (MetaService == null)
                return new FindObjectResult { Message = "MetaModel service not available." };

            var result = new FindObjectResult { Success = true };

            try
            {
                var typeSearchers = GetTypeSearchers(objectType);
                foreach (var searcher in typeSearchers)
                {
                    foreach (string name in searcher.GetNames())
                    {
                        bool match = exactMatch
                            ? name.Equals(objectName, StringComparison.OrdinalIgnoreCase)
                            : name.IndexOf(objectName, StringComparison.OrdinalIgnoreCase) >= 0;

                        if (match)
                        {
                            string modelName = GetObjectModelName(searcher.TypeName, name);
                            result.Matches.Add(new FoundObject
                            {
                                ObjectType = searcher.TypeName,
                                ObjectName = name,
                                ModelName = modelName,
                                IsCustom = IsCustomModel(modelName)
                            });

                            if (result.Matches.Count >= 100) break;
                        }
                    }
                    if (result.Matches.Count >= 100) break;
                }
            }
            catch (Exception ex)
            {
                result.Message = "Search error: " + ex.Message;
            }

            if (result.Matches.Count == 0)
                result.Message = "No objects found matching '" + objectName + "'."
                    + (string.IsNullOrEmpty(objectType) ? "" : " (type: " + objectType + ")");

            return result;
        }

        public ListObjectsResult ListObjects(string modelName, string objectType, string nameFilter, int maxResults)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (MetaService == null)
                return new ListObjectsResult { Message = "MetaModel service not available." };

            var result = new ListObjectsResult { Success = true };
            bool hasNameFilter = !string.IsNullOrEmpty(nameFilter);
            bool hasModelFilter = !string.IsNullOrEmpty(modelName);

            try
            {
                var typeSearchers = GetTypeSearchers(objectType);
                foreach (var searcher in typeSearchers)
                {
                    foreach (string name in searcher.GetNames())
                    {
                        // Apply cheap name filter FIRST to avoid expensive model lookups
                        if (hasNameFilter &&
                            name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        // Only do the expensive model lookup when needed
                        string objModel = null;
                        if (hasModelFilter)
                        {
                            objModel = GetObjectModelName(searcher.TypeName, name);
                            if (!modelName.Equals(objModel, StringComparison.OrdinalIgnoreCase))
                                continue;
                        }

                        // Lazy-resolve model name if not already fetched
                        if (objModel == null)
                            objModel = GetObjectModelName(searcher.TypeName, name);

                        result.Objects.Add(new FoundObject
                        {
                            ObjectType = searcher.TypeName,
                            ObjectName = name,
                            ModelName = objModel,
                            IsCustom = IsCustomModel(objModel)
                        });

                        if (result.Objects.Count >= maxResults) break;
                    }
                    if (result.Objects.Count >= maxResults) break;
                }
            }
            catch (Exception ex)
            {
                result.Message = "List error: " + ex.Message;
            }

            return result;
        }

        public ModelInfoResult GetModelInfo(string modelName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (MetaService == null)
                return new ModelInfoResult { Message = "MetaModel service not available." };

            try
            {
                var modelInfo = MetaService.GetModel(modelName);

                if (modelInfo == null)
                    return new ModelInfoResult { Message = "Model '" + modelName + "' not found." };

                var result = new ModelInfoResult
                {
                    Success = true,
                    Name = modelInfo.Name,
                    DisplayName = modelInfo.DisplayName,
                    Publisher = modelInfo.Publisher,
                    Version = modelInfo.VersionBuild.ToString(),
                    ModelId = modelInfo.Id,
                    Layer = modelInfo.Layer.ToString()
                };

                return result;
            }
            catch (Exception ex)
            {
                return new ModelInfoResult { Message = "Model info failed: " + ex.Message };
            }
        }

        public List<ModelSummary> ListModels()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var models = new List<ModelSummary>();
            if (MetaService == null) return models;

            try
            {
                foreach (var info in MetaService.GetModels())
                {
                    models.Add(new ModelSummary
                    {
                        Name = info.Name,
                        DisplayName = info.DisplayName,
                        IsCustom = IsCustomModel(info.Name)
                    });
                }
            }
            catch { }

            return models;
        }

        // ── Labels ──

        public LabelResult ReadLabel(string labelFileId, string language, string labelId, string searchText, int maxResults)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (MetaService == null)
                return new LabelResult { Message = "MetaModel service not available." };

            try
            {
                var labelFile = MetaService.GetLabelFile(labelFileId);
                if (labelFile == null)
                    return new LabelResult { Message = "Label file '" + labelFileId + "' not found." };

                var context = new VSApplicationContext(CoreUtility.ServiceProvider);
                var factory = new LabelControllerFactory();
                var controller = factory.GetOrCreateLabelController(labelFile, context);

                var result = new LabelResult { Success = true };

                // If specific label requested
                if (!string.IsNullOrEmpty(labelId))
                {
                    if (controller.Exists(labelId))
                    {
                        string text = TryGetLabelText(controller, labelId);
                        result.Labels.Add(new LabelEntry { Id = labelId, Text = text });
                    }
                    else
                    {
                        result.Message = "Label '" + labelId + "' not found in " + labelFileId;
                    }
                    return result;
                }

                var labels = TrySearchLabels(controller, searchText, maxResults);
                foreach (var entry in labels)
                    result.Labels.Add(entry);

                result.Message = result.Labels.Count > 0
                    ? "Retrieved " + result.Labels.Count + " labels from " + labelFileId + "."
                    : "No labels found in " + labelFileId + ".";
                return result;
            }
            catch (Exception ex)
            {
                return new LabelResult { Message = "Label read failed: " + ex.Message };
            }
        }

        public MetaModelResult CreateLabel(string labelFileId, string language, string labelId, string text, string comment)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (MetaService == null)
                return Fail("MetaModel service not available.");

            try
            {
                var labelFile = MetaService.GetLabelFile(labelFileId);
                if (labelFile == null)
                    return Fail("Label file '" + labelFileId + "' not found.");

                var context = new VSApplicationContext(CoreUtility.ServiceProvider);
                var factory = new LabelControllerFactory();
                var controller = factory.GetOrCreateLabelController(labelFile, context);

                if (controller.Exists(labelId))
                    return Fail("Label '" + labelId + "' already exists in " + labelFileId);

                controller.Insert(labelId, text, comment ?? string.Empty);
                controller.Save();

                return new MetaModelResult
                {
                    Success = true,
                    Message = "Created label @" + labelFileId + ":" + labelId + " = " + text
                };
            }
            catch (Exception ex)
            {
                return Fail("Label create failed: " + ex.Message);
            }
        }

        // ── Project ──

        public MetaModelResult AddToProject(string objectType, string objectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var projectNode = GetActiveProjectNode();
                if (projectNode == null)
                    return Fail("No active D365FO project.");

                var metadataType = ResolveMetadataType(objectType);
                if (metadataType == null)
                    return Fail("Unsupported object type for project add: " + objectType);

                var reference = new MetadataReference(objectName, metadataType);
                projectNode.AddModelElementsToProject(new[] { reference });

                return new MetaModelResult
                {
                    Success = true,
                    Message = "Added " + objectType + " '" + objectName + "' to project."
                };
            }
            catch (Exception ex)
            {
                return Fail("Add to project failed: " + ex.Message);
            }
        }

        public List<ProjectItemInfo> ListProjectItems()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var items = new List<ProjectItemInfo>();

            try
            {
                var project = GetActiveProject();
                if (project?.ProjectItems == null) return items;

                foreach (ProjectItem item in project.ProjectItems)
                {
                    try
                    {
                        string name = item.Name;
                        string path = null;
                        try { path = item.Properties?.Item("FullPath")?.Value as string; }
                        catch { }

                        items.Add(new ProjectItemInfo
                        {
                            Name = name,
                            FilePath = path
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return items;
        }

        // ── Environment ──

        public EnvironmentInfo GetEnvironmentInfo()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var info = new EnvironmentInfo();

            try
            {
                var config = DynamicsConfigurationReader.ReadActiveConfiguration();
                info.CustomMetadataFolder = config.CustomMetadataFolder;
                info.ReferenceMetadataFolders = config.ReferenceMetadataFolders;

                var projectNode = GetActiveProjectNode();
                if (projectNode != null)
                {
                    info.ActiveProjectName = projectNode.Caption;
                    var modelInfo = projectNode.GetProjectsModelInfo();
                    if (modelInfo != null)
                    {
                        info.ActiveModelName = modelInfo.Name;
                        info.ActiveModelId = modelInfo.Id;
                        info.ActiveModelLayer = modelInfo.Layer.ToString();
                    }
                }
            }
            catch { }

            return info;
        }

        // ── Private helpers: Create operations ──

        private MetaModelResult CreateClass(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axClass = new AxClass { Name = req.ObjectName };
            if (!string.IsNullOrEmpty(req.Declaration))
                axClass.Declaration = StripCData(req.Declaration);
            else
                axClass.Declaration = "class " + req.ObjectName + "\n{\n}";

            AddMethods(axClass.Methods, req.Methods);
            MetaService.CreateClass(axClass, saveInfo);
            AddToProjectIfActive(req.ObjectType ?? "AxClass", req.ObjectName);
            return Ok("Created AxClass '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateTable(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axTable = new AxTable { Name = req.ObjectName };
            if (!string.IsNullOrEmpty(req.Declaration))
                axTable.Declaration = StripCData(req.Declaration);

            AddMethods(axTable.Methods, req.Methods);
            ApplyTypedMetadata(axTable, req);
            MetaService.CreateTable(axTable, saveInfo);
            AddToProjectIfActive("AxTable", req.ObjectName);
            return Ok("Created AxTable '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateForm(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axForm = new AxForm { Name = req.ObjectName };
            if (!string.IsNullOrEmpty(req.Declaration))
                axForm.SourceCode.Declaration = StripCData(req.Declaration);

            AddMethods(axForm.Methods, req.Methods);
            ApplyTypedMetadata(axForm, req);
            MetaService.CreateForm(axForm, saveInfo);
            AddToProjectIfActive("AxForm", req.ObjectName);
            return Ok("Created AxForm '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateEdt(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axEdt = new AxEdtString { Name = req.ObjectName };
            ApplyTypedMetadata(axEdt, req);
            MetaService.CreateExtendedDataType(axEdt, saveInfo);
            AddToProjectIfActive("AxEdt", req.ObjectName);
            return Ok("Created AxEdt '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateEnum(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axEnum = new AxEnum { Name = req.ObjectName };
            ApplyTypedMetadata(axEnum, req);
            MetaService.CreateEnum(axEnum, saveInfo);
            AddToProjectIfActive("AxEnum", req.ObjectName);
            return Ok("Created AxEnum '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateMenuItemDisplay(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var item = new AxMenuItemDisplay { Name = req.ObjectName };
            ApplyTypedMetadata(item, req);
            MetaService.CreateMenuItemDisplay(item, saveInfo);
            AddToProjectIfActive("AxMenuItemDisplay", req.ObjectName);
            return Ok("Created AxMenuItemDisplay '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateMenuItemOutput(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var item = new AxMenuItemOutput { Name = req.ObjectName };
            ApplyTypedMetadata(item, req);
            MetaService.CreateMenuItemOutput(item, saveInfo);
            AddToProjectIfActive("AxMenuItemOutput", req.ObjectName);
            return Ok("Created AxMenuItemOutput '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateMenuItemAction(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var item = new AxMenuItemAction { Name = req.ObjectName };
            ApplyTypedMetadata(item, req);
            MetaService.CreateMenuItemAction(item, saveInfo);
            AddToProjectIfActive("AxMenuItemAction", req.ObjectName);
            return Ok("Created AxMenuItemAction '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateQuery(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axQuery = new AxQuerySimple { Name = req.ObjectName };
            ApplyTypedMetadata(axQuery, req);
            MetaService.CreateQuery(axQuery, saveInfo);
            AddToProjectIfActive("AxQuery", req.ObjectName);
            return Ok("Created AxQuery '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateView(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axView = new AxView { Name = req.ObjectName };
            if (!string.IsNullOrEmpty(req.Declaration))
                axView.Declaration = StripCData(req.Declaration);
            AddMethods(axView.Methods, req.Methods);
            ApplyTypedMetadata(axView, req);
            MetaService.CreateView(axView, saveInfo);
            AddToProjectIfActive("AxView", req.ObjectName);
            return Ok("Created AxView '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateDataEntityView(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var entity = new AxDataEntityView { Name = req.ObjectName };
            if (!string.IsNullOrEmpty(req.Declaration))
                entity.Declaration = StripCData(req.Declaration);
            ApplyTypedMetadata(entity, req);
            MetaService.UpdateDataEntityView(entity, saveInfo);
            AddToProjectIfActive("AxDataEntityView", req.ObjectName);
            return Ok("Created AxDataEntityView '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateSecurityPrivilege(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var priv = new AxSecurityPrivilege { Name = req.ObjectName };
            ApplyTypedMetadata(priv, req);
            MetaService.CreateSecurityPrivilege(priv, saveInfo);
            AddToProjectIfActive("AxSecurityPrivilege", req.ObjectName);
            return Ok("Created AxSecurityPrivilege '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateSecurityDuty(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var duty = new AxSecurityDuty { Name = req.ObjectName };
            ApplyTypedMetadata(duty, req);
            MetaService.CreateSecurityDuty(duty, saveInfo);
            AddToProjectIfActive("AxSecurityDuty", req.ObjectName);
            return Ok("Created AxSecurityDuty '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateSecurityRole(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var role = new AxSecurityRole { Name = req.ObjectName };
            ApplyTypedMetadata(role, req);
            MetaService.CreateSecurityRole(role, saveInfo);
            AddToProjectIfActive("AxSecurityRole", req.ObjectName);
            return Ok("Created AxSecurityRole '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateService(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var svc = new AxService { Name = req.ObjectName };
            ApplyTypedMetadata(svc, req);
            MetaService.CreateService(svc, saveInfo);
            AddToProjectIfActive("AxService", req.ObjectName);
            return Ok("Created AxService '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateServiceGroup(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var grp = new AxServiceGroup { Name = req.ObjectName };
            ApplyTypedMetadata(grp, req);
            MetaService.CreateServiceGroup(grp, saveInfo);
            AddToProjectIfActive("AxServiceGroup", req.ObjectName);
            return Ok("Created AxServiceGroup '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateMap(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var map = new AxMap { Name = req.ObjectName };
            if (!string.IsNullOrEmpty(req.Declaration))
                map.Declaration = StripCData(req.Declaration);
            AddMethods(map.Methods, req.Methods);
            ApplyTypedMetadata(map, req);
            MetaService.CreateMap(map, saveInfo);
            AddToProjectIfActive("AxMap", req.ObjectName);
            return Ok("Created AxMap '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateMenu(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var menu = new AxMenu { Name = req.ObjectName };
            ApplyTypedMetadata(menu, req);
            MetaService.CreateMenu(menu, saveInfo);
            AddToProjectIfActive("AxMenu", req.ObjectName);
            return Ok("Created AxMenu '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateTile(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var tile = new AxTile { Name = req.ObjectName };
            ApplyTypedMetadata(tile, req);
            MetaService.CreateTile(tile, saveInfo);
            AddToProjectIfActive("AxTile", req.ObjectName);
            return Ok("Created AxTile '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateConfigurationKey(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var key = new AxConfigurationKey { Name = req.ObjectName };
            ApplyTypedMetadata(key, req);
            MetaService.CreateConfigurationKey(key, saveInfo);
            AddToProjectIfActive("AxConfigurationKey", req.ObjectName);
            return Ok("Created AxConfigurationKey '" + req.ObjectName + "'.");
        }

        /// <summary>
        /// Creates an extension object by serializing it to XML and writing to the
        /// model's metadata folder. The IMetaModelService has no Create/Update methods
        /// for extension types, so this mirrors how the D365FO VS tools create extensions.
        /// </summary>
        private MetaModelResult CreateExtensionObject(CreateObjectRequest req, ModelSaveInfo saveInfo,
            Type extensionType, string folderName)
        {
            try
            {
                var extObj = Activator.CreateInstance(extensionType);
                extensionType.GetProperty("Name")?.SetValue(extObj, req.ObjectName);

                // Apply typed metadata (properties, enum values, fields, etc.)
                ApplyTypedMetadata(extObj, req);

                // Resolve file path
                string filePath = GetExtensionFilePath(saveInfo, folderName, req.ObjectName);
                if (filePath == null)
                    return Fail("Could not determine metadata folder for model.");

                if (File.Exists(filePath))
                    return Fail("Extension '" + req.ObjectName + "' already exists at: " + filePath);

                // Serialize and write
                string dir = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var serializer = new XmlSerializer(extensionType);
                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    Encoding = new UTF8Encoding(false)
                };
                using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                using (var writer = XmlWriter.Create(stream, settings))
                {
                    serializer.Serialize(writer, extObj);
                }

                AddToProjectIfActive(req.ObjectType, req.ObjectName);
                return new MetaModelResult
                {
                    Success = true,
                    Message = "Created " + req.ObjectType + " '" + req.ObjectName + "'.",
                    FilePath = filePath
                };
            }
            catch (Exception ex)
            {
                return Fail("Extension create failed: " + ex.Message);
            }
        }

        private string GetExtensionFilePath(ModelSaveInfo saveInfo, string folderName, string objectName)
        {
            try
            {
                var config = DynamicsConfigurationReader.ReadActiveConfiguration();
                string customFolder = config.CustomMetadataFolder;
                if (string.IsNullOrEmpty(customFolder)) return null;

                // Find the module (package) name for the model
                string module = null;
                foreach (var m in MetaService.GetModels())
                {
                    if (m.Id == saveInfo.Id)
                    {
                        module = m.Module;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(module)) return null;

                string dir = Path.Combine(customFolder, module, module, folderName);
                return Path.Combine(dir, objectName + ".xml");
            }
            catch { return null; }
        }

        // ── Private helpers: Update operations ──

        private MetaModelResult UpdateClass(UpdateObjectRequest req)
        {
            var cls = MetaService.GetClass(req.ObjectName);
            if (cls == null)
                return Fail("Class '" + req.ObjectName + "' not found.");

            var saveInfo = GetModelSaveInfoForObject("AxClass", req.ObjectName);
            if (saveInfo == null) return Fail("Cannot resolve model for '" + req.ObjectName + "'.");

            if (!string.IsNullOrEmpty(req.Declaration))
                cls.Declaration = StripCData(req.Declaration);

            UpdateMethods(cls.Methods, req.Methods, req.RemoveMethodNames);
            MetaService.UpdateClass(cls, saveInfo);
            return Ok("Updated AxClass '" + req.ObjectName + "'.");
        }

        private MetaModelResult UpdateTable(UpdateObjectRequest req)
        {
            var tbl = MetaService.GetTable(req.ObjectName);
            if (tbl == null)
                return Fail("Table '" + req.ObjectName + "' not found.");

            var saveInfo = GetModelSaveInfoForObject("AxTable", req.ObjectName);
            if (saveInfo == null) return Fail("Cannot resolve model for '" + req.ObjectName + "'.");

            if (!string.IsNullOrEmpty(req.Declaration))
                tbl.Declaration = StripCData(req.Declaration);

            UpdateMethods(tbl.Methods, req.Methods, req.RemoveMethodNames);
            ApplyTypedMetadata(tbl, req);
            MetaService.UpdateTable(tbl, saveInfo);
            return Ok("Updated AxTable '" + req.ObjectName + "'.");
        }

        private MetaModelResult UpdateForm(UpdateObjectRequest req)
        {
            var frm = MetaService.GetForm(req.ObjectName);
            if (frm == null)
                return Fail("Form '" + req.ObjectName + "' not found.");

            var saveInfo = GetModelSaveInfoForObject("AxForm", req.ObjectName);
            if (saveInfo == null) return Fail("Cannot resolve model for '" + req.ObjectName + "'.");

            if (!string.IsNullOrEmpty(req.Declaration))
                frm.SourceCode.Declaration = StripCData(req.Declaration);

            UpdateMethods(frm.Methods, req.Methods, req.RemoveMethodNames);
            MetaService.UpdateForm(frm, saveInfo);
            return Ok("Updated AxForm '" + req.ObjectName + "'.");
        }

        private MetaModelResult UpdateEdt(UpdateObjectRequest req)
        {
            var edt = MetaService.GetExtendedDataType(req.ObjectName);
            if (edt == null)
                return Fail("EDT '" + req.ObjectName + "' not found.");

            var saveInfo = GetModelSaveInfoForObject("AxEdt", req.ObjectName);
            if (saveInfo == null) return Fail("Cannot resolve model for '" + req.ObjectName + "'.");

            ApplyTypedMetadata(edt, req);
            MetaService.UpdateExtendedDataType(edt, saveInfo);
            return Ok("Updated AxEdt '" + req.ObjectName + "'.");
        }

        private MetaModelResult UpdateEnum(UpdateObjectRequest req)
        {
            var enm = MetaService.GetEnum(req.ObjectName);
            if (enm == null)
                return Fail("Enum '" + req.ObjectName + "' not found.");

            var saveInfo = GetModelSaveInfoForObject("AxEnum", req.ObjectName);
            if (saveInfo == null) return Fail("Cannot resolve model for '" + req.ObjectName + "'.");

            ApplyTypedMetadata(enm, req);
            MetaService.UpdateEnum(enm, saveInfo);
            return Ok("Updated AxEnum '" + req.ObjectName + "'.");
        }

        private MetaModelResult UpdateGenericByXml(UpdateObjectRequest req)
        {
            return Fail("Update for type '" + req.ObjectType + "' is not yet supported via the typed API. "
                + "Supported types: AxClass, AxTable, AxForm, AxEdt, AxEnum.");
        }

        // ── Utility methods ──

        private void AddMethods(IList<AxMethod> target, string[] sources)
        {
            if (sources == null) return;
            foreach (string src in sources)
            {
                if (string.IsNullOrWhiteSpace(src)) continue;
                string clean = StripCData(src);
                string name = ExtractMethodName(clean);
                target.Add(new AxMethod { Name = name, Source = clean });
            }
        }

        private void UpdateMethods(IList<AxMethod> existing, string[] upserts, string[] removals)
        {
            if (removals != null)
            {
                foreach (string removeName in removals)
                {
                    var toRemove = existing.FirstOrDefault(m =>
                        m.Name.Equals(removeName, StringComparison.OrdinalIgnoreCase));
                    if (toRemove != null) existing.Remove(toRemove);
                }
            }

            if (upserts != null)
            {
                foreach (string src in upserts)
                {
                    if (string.IsNullOrWhiteSpace(src)) continue;
                    string clean = StripCData(src);
                    string name = ExtractMethodName(clean);
                    var found = existing.FirstOrDefault(m =>
                        m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (found != null)
                    {
                        found.Source = clean;
                    }
                    else
                    {
                        existing.Add(new AxMethod { Name = name, Source = clean });
                    }
                }
            }
        }

        private static string ExtractMethodName(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return "unknownMethod";
            var lines = source.Split('\n');
            var sig = new StringBuilder();
            foreach (string line in lines)
            {
                string l = line.Trim();
                if (l.StartsWith("///") || l.StartsWith("//") || l.StartsWith("/*")
                    || l.StartsWith("*") || l.Length == 0 || l.StartsWith("["))
                    continue;
                sig.Append(l);
                if (l.Contains("(")) break;
            }
            string s = sig.ToString();
            int paren = s.IndexOf('(');
            if (paren <= 0) return "unknownMethod";
            string before = s.Substring(0, paren).Trim();
            string[] tokens = before.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length > 0 ? tokens[tokens.Length - 1] : "unknownMethod";
        }

        private ModelSaveInfo GetModelSaveInfo(string modelName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // If modelName specified, try to resolve it
            if (!string.IsNullOrEmpty(modelName))
            {
                var info = MetaService.GetModel(modelName);
                if (info != null)
                    return new ModelSaveInfo { Id = info.Id, Layer = info.Layer };
            }

            // Fallback: use active project's model
            var projectNode = GetActiveProjectNode();
            if (projectNode != null)
            {
                var projectModel = projectNode.GetProjectsModelInfo();
                if (projectModel != null)
                    return new ModelSaveInfo { Id = projectModel.Id, Layer = projectModel.Layer };
            }

            return null;
        }

        private ModelSaveInfo GetModelSaveInfoForObject(string objectType, string objectName)
        {
            string modelName = GetObjectModelName(objectType, objectName);
            return !string.IsNullOrEmpty(modelName) ? GetModelSaveInfo(modelName) : GetModelSaveInfo(null);
        }

        private string GetObjectModelName(string objectType, string objectName)
        {
            try
            {
                ModelInfo modelInfo = null;

                switch (objectType)
                {
                    case "AxClass": modelInfo = MetaService.GetClassModelInfo(objectName).FirstOrDefault(); break;
                    case "AxTable": modelInfo = MetaService.GetTableModelInfo(objectName).FirstOrDefault(); break;
                    case "AxForm": modelInfo = MetaService.GetFormModelInfo(objectName).FirstOrDefault(); break;
                    case "AxEnum": modelInfo = MetaService.GetEnumModelInfo(objectName).FirstOrDefault(); break;
                    case "AxView": modelInfo = MetaService.GetViewModelInfo(objectName).FirstOrDefault(); break;
                    case "AxQuery": modelInfo = MetaService.GetQueryModelInfo(objectName).FirstOrDefault(); break;
                }

                return modelInfo?.Name;
            }
            catch { return null; }
        }

        private VSProjectNode GetActiveProjectNode()
        {
            var projects = Dte?.ActiveSolutionProjects as Array;
            if (projects?.Length > 0)
            {
                var project = projects.GetValue(0) as Project;
                return project?.Object as VSProjectNode;
            }
            return null;
        }

        private Project GetActiveProject()
        {
            var projects = Dte?.ActiveSolutionProjects as Array;
            if (projects?.Length > 0)
                return projects.GetValue(0) as Project;
            return null;
        }

        private void AddToProjectIfActive(string objectType, string objectName)
        {
            try
            {
                var projectNode = GetActiveProjectNode();
                if (projectNode == null) return;
                var metadataType = ResolveMetadataType(objectType);
                if (metadataType == null) return;
                var reference = new MetadataReference(objectName, metadataType);
                projectNode.AddModelElementsToProject(new[] { reference });
            }
            catch { }
        }

        private static Type ResolveMetadataType(string objectType)
        {
            switch (objectType)
            {
                case "AxClass": return typeof(AxClass);
                case "AxTable": return typeof(AxTable);
                case "AxForm": return typeof(AxForm);
                case "AxEdt": return typeof(AxEdt);
                case "AxEnum": return typeof(AxEnum);
                case "AxView": return typeof(AxView);
                case "AxQuery": return typeof(AxQuerySimple);
                case "AxMap": return typeof(AxMap);
                case "AxMenu": return typeof(AxMenu);
                case "AxTile": return typeof(AxTile);
                case "AxMenuItemDisplay": return typeof(AxMenuItemDisplay);
                case "AxMenuItemOutput": return typeof(AxMenuItemOutput);
                case "AxMenuItemAction": return typeof(AxMenuItemAction);
                case "AxDataEntityView": return typeof(AxDataEntityView);
                case "AxSecurityPrivilege": return typeof(AxSecurityPrivilege);
                case "AxSecurityDuty": return typeof(AxSecurityDuty);
                case "AxSecurityRole": return typeof(AxSecurityRole);
                case "AxService": return typeof(AxService);
                case "AxServiceGroup": return typeof(AxServiceGroup);
                case "AxConfigurationKey": return typeof(AxConfigurationKey);

                // Extension types
                case "AxTableExtension": return typeof(AxTableExtension);
                case "AxFormExtension": return typeof(AxFormExtension);
                case "AxEnumExtension": return typeof(AxEnumExtension);
                case "AxEdtExtension": return typeof(AxEdtExtension);
                case "AxViewExtension": return typeof(AxViewExtension);
                case "AxMenuExtension": return typeof(AxMenuExtension);
                case "AxMenuItemDisplayExtension": return typeof(AxMenuItemDisplayExtension);
                case "AxMenuItemOutputExtension": return typeof(AxMenuItemOutputExtension);
                case "AxMenuItemActionExtension": return typeof(AxMenuItemActionExtension);
                case "AxQuerySimpleExtension": return typeof(AxQuerySimpleExtension);
                case "AxSecurityDutyExtension": return typeof(AxSecurityDutyExtension);
                case "AxSecurityRoleExtension": return typeof(AxSecurityRoleExtension);

                default: return null;
            }
        }

        // ── Strongly-typed metadata apply methods ──

        private static void ApplyProperties(object target, Dictionary<string, string> props)
        {
            if (props == null || target == null) return;
            var type = target.GetType();
            foreach (var kvp in props)
            {
                var prop = type.GetProperty(kvp.Key);
                if (prop == null || !prop.CanWrite) continue;
                try
                {
                    var propType = prop.PropertyType;
                    var underlying = Nullable.GetUnderlyingType(propType);
                    object value;

                    if (underlying != null)
                    {
                        if (string.IsNullOrEmpty(kvp.Value))
                            value = null;
                        else if (underlying == typeof(bool))
                            value = bool.Parse(kvp.Value);
                        else if (underlying == typeof(int))
                            value = int.Parse(kvp.Value);
                        else if (underlying.IsEnum)
                            value = Enum.Parse(underlying, kvp.Value, true);
                        else
                            continue;
                    }
                    else if (propType == typeof(string))
                        value = kvp.Value;
                    else if (propType == typeof(bool))
                        value = "true".Equals(kvp.Value, StringComparison.OrdinalIgnoreCase)
                             || "1".Equals(kvp.Value) || "yes".Equals(kvp.Value, StringComparison.OrdinalIgnoreCase);
                    else if (propType == typeof(int))
                        value = int.Parse(kvp.Value);
                    else if (propType == typeof(long))
                        value = long.Parse(kvp.Value);
                    else if (propType.IsEnum)
                        value = Enum.Parse(propType, kvp.Value, true);
                    else
                        continue;

                    prop.SetValue(target, value);
                }
                catch { }
            }
        }

        private static void ApplyEnumValues(AxEnum axEnum, List<EnumValueDto> values)
        {
            if (values == null) return;
            int autoValue = 0;
            foreach (var existing in axEnum.EnumValues)
            {
                if (existing.Value >= autoValue)
                    autoValue = existing.Value + 1;
            }
            foreach (var dto in values)
            {
                if (string.IsNullOrWhiteSpace(dto.Name)) continue;
                if (axEnum.EnumValues.Contains(dto.Name)) continue;
                var ev = new AxEnumValue { Name = dto.Name, Value = dto.Value };
                if (!string.IsNullOrEmpty(dto.Label))
                    ev.Label = dto.Label;
                axEnum.EnumValues.Add(ev);
            }
        }

        private static void ApplyFields(AxTable tbl, List<FieldDto> fields)
        {
            if (fields == null) return;
            foreach (var f in fields)
            {
                if (string.IsNullOrEmpty(f.Name)) continue;
                AxTableField axField;
                switch ((f.FieldType ?? "String").ToLowerInvariant())
                {
                    case "int": axField = new AxTableFieldInt(); break;
                    case "real": axField = new AxTableFieldReal(); break;
                    case "date": axField = new AxTableFieldDate(); break;
                    case "datetime": axField = new AxTableFieldUtcDateTime(); break;
                    case "enum": axField = new AxTableFieldEnum(); break;
                    case "int64": axField = new AxTableFieldInt64(); break;
                    case "container": axField = new AxTableFieldContainer(); break;
                    case "guid": axField = new AxTableFieldGuid(); break;
                    case "time": axField = new AxTableFieldTime(); break;
                    default: axField = new AxTableFieldString(); break;
                }
                axField.Name = f.Name;
                if (!string.IsNullOrEmpty(f.Label))
                    axField.Label = f.Label;
                if (!string.IsNullOrEmpty(f.ExtendedDataType))
                {
                    var edtProp = axField.GetType().GetProperty("ExtendedDataType");
                    edtProp?.SetValue(axField, f.ExtendedDataType);
                }
                if (axField is AxTableFieldEnum enumField && !string.IsNullOrEmpty(f.EnumType))
                    enumField.EnumType = f.EnumType;
                tbl.Fields.Add(axField);
            }
        }

        private static void ApplyIndexes(AxTable tbl, List<IndexDto> indexes)
        {
            if (indexes == null) return;
            foreach (var idx in indexes)
            {
                if (string.IsNullOrEmpty(idx.Name)) continue;
                var axIdx = new AxTableIndex { Name = idx.Name, AllowDuplicates = idx.AllowDuplicates ? NoYes.Yes : NoYes.No };
                if (idx.Fields != null)
                    foreach (string fieldName in idx.Fields)
                        axIdx.Fields.Add(new AxTableIndexField { Name = fieldName, DataField = fieldName });
                tbl.Indexes.Add(axIdx);
            }
        }

        private static void ApplyFieldGroups(AxTable tbl, List<FieldGroupDto> groups)
        {
            if (groups == null) return;
            foreach (var fg in groups)
            {
                if (string.IsNullOrEmpty(fg.Name)) continue;
                var axFg = new AxTableFieldGroup { Name = fg.Name };
                if (!string.IsNullOrEmpty(fg.Label))
                    axFg.Label = fg.Label;
                if (fg.Fields != null)
                    foreach (string fieldName in fg.Fields)
                        axFg.Fields.Add(new AxTableFieldGroupField { Name = fieldName, DataField = fieldName });
                tbl.FieldGroups.Add(axFg);
            }
        }

        private static void ApplyRelations(AxTable tbl, List<RelationDto> relations)
        {
            if (relations == null) return;
            foreach (var rel in relations)
            {
                if (string.IsNullOrEmpty(rel.Name)) continue;
                var axRel = new AxTableRelation { Name = rel.Name };
                if (!string.IsNullOrEmpty(rel.RelatedTable))
                    axRel.RelatedTable = rel.RelatedTable;
                if (rel.Constraints != null)
                {
                    foreach (var c in rel.Constraints)
                    {
                        axRel.Constraints.Add(new AxTableRelationConstraintField
                        {
                            Name = (c.Field ?? "") + "_" + (c.RelatedField ?? ""),
                            Field = c.Field,
                            RelatedField = c.RelatedField
                        });
                    }
                }
                tbl.Relations.Add(axRel);
            }
        }

        private static void ApplyEntryPoints(object secObj, List<EntryPointDto> entryPoints)
        {
            if (entryPoints == null) return;
            // Use reflection to access EntryPoints/Privileges collections
            var epProp = secObj.GetType().GetProperty("EntryPoints");
            if (epProp != null)
            {
                var collection = epProp.GetValue(secObj);
                var addMethod = collection?.GetType().GetMethod("Add");
                if (addMethod != null)
                {
                    foreach (var ep in entryPoints)
                    {
                        if (string.IsNullOrEmpty(ep.Name)) continue;
                        var axEp = new AxSecurityEntryPointReference { Name = ep.Name };
                        if (!string.IsNullOrEmpty(ep.ObjectName))
                            axEp.ObjectName = ep.ObjectName;
                        try
                        {
                            if (!string.IsNullOrEmpty(ep.ObjectType))
                                axEp.ObjectType = (EntryPointType)Enum.Parse(typeof(EntryPointType), ep.ObjectType, true);
                        }
                        catch { }
                        addMethod.Invoke(collection, new object[] { axEp });
                    }
                }
            }
        }

        private void ApplyTypedMetadata(object target, CreateObjectRequest req)
        {
            ApplyProperties(target, req.Properties);
            if (target is AxEnum axEnum)
                ApplyEnumValues(axEnum, req.EnumValues);
            if (target is AxTable axTable)
            {
                ApplyFields(axTable, req.Fields);
                ApplyIndexes(axTable, req.Indexes);
                ApplyFieldGroups(axTable, req.FieldGroups);
                ApplyRelations(axTable, req.Relations);
            }
            ApplyEntryPoints(target, req.EntryPoints);
        }

        private void ApplyTypedMetadata(object target, UpdateObjectRequest req)
        {
            ApplyProperties(target, req.Properties);
            if (target is AxEnum axEnum)
                ApplyEnumValues(axEnum, req.EnumValues);
            if (target is AxTable axTable)
            {
                ApplyFields(axTable, req.Fields);
                ApplyIndexes(axTable, req.Indexes);
                ApplyFieldGroups(axTable, req.FieldGroups);
                ApplyRelations(axTable, req.Relations);
            }
            ApplyEntryPoints(target, req.EntryPoints);
        }

        // ── Strongly-typed metadata extraction (for read) ──

        private static Dictionary<string, string> ExtractProperties(object axObj)
        {
            var props = new Dictionary<string, string>();
            if (axObj == null) return props;

            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Name", "SourceCode", "Declaration", "Methods", "UnparsableSource",
                "Attributes", "Members", "Fields", "FieldGroups", "Indexes",
                "Relations", "EnumValues", "FormControl", "DataSources",
                "EntryPoints", "Privileges", "Duties", "SubRoles", "Controls",
                "Parts", "FullTextIndexes", "Mappings", "StateMachines",
                "DeleteActions", "FieldReferences", "Events"
            };

            foreach (var prop in axObj.GetType().GetProperties())
            {
                if (!prop.CanRead || excluded.Contains(prop.Name)) continue;
                var propType = prop.PropertyType;
                var underlying = Nullable.GetUnderlyingType(propType) ?? propType;
                if (underlying != typeof(string) && underlying != typeof(bool)
                    && underlying != typeof(int) && underlying != typeof(long)
                    && !underlying.IsEnum)
                    continue;
                try
                {
                    var val = prop.GetValue(axObj);
                    if (val == null) continue;
                    // Skip defaults to keep output clean
                    if (underlying == typeof(bool) && !(bool)val) continue;
                    if (underlying == typeof(int) && (int)val == 0) continue;
                    if (underlying == typeof(long) && (long)val == 0) continue;
                    if (underlying == typeof(string) && string.IsNullOrEmpty((string)val)) continue;
                    if (underlying.IsEnum && Convert.ToInt32(val) == 0) continue;
                    props[prop.Name] = val.ToString();
                }
                catch { }
            }
            return props;
        }

        private static List<EnumValueDto> ExtractEnumValues(AxEnum enm)
        {
            var result = new List<EnumValueDto>();
            if (enm?.EnumValues == null) return result;
            foreach (var v in enm.EnumValues)
                result.Add(new EnumValueDto { Name = v.Name, Value = v.Value, Label = v.Label });
            return result;
        }

        private static List<FieldDto> ExtractFields(AxTable tbl)
        {
            var result = new List<FieldDto>();
            if (tbl?.Fields == null) return result;
            foreach (var f in tbl.Fields)
            {
                string fieldType = f.GetType().Name;
                if (fieldType.StartsWith("AxTableField"))
                    fieldType = fieldType.Substring("AxTableField".Length);
                var dto = new FieldDto { Name = f.Name, FieldType = fieldType, Label = f.Label };
                var edtProp = f.GetType().GetProperty("ExtendedDataType");
                if (edtProp != null)
                    dto.ExtendedDataType = edtProp.GetValue(f) as string;
                if (f is AxTableFieldEnum enumField)
                    dto.EnumType = enumField.EnumType;
                result.Add(dto);
            }
            return result;
        }

        private static List<IndexDto> ExtractIndexes(AxTable tbl)
        {
            var result = new List<IndexDto>();
            if (tbl?.Indexes == null) return result;
            foreach (var idx in tbl.Indexes)
            {
                var dto = new IndexDto { Name = idx.Name, AllowDuplicates = idx.AllowDuplicates == NoYes.Yes };
                if (idx.Fields != null)
                    foreach (var f in idx.Fields)
                        dto.Fields.Add(f.DataField);
                result.Add(dto);
            }
            return result;
        }

        private static List<FieldGroupDto> ExtractFieldGroups(AxTable tbl)
        {
            var result = new List<FieldGroupDto>();
            if (tbl?.FieldGroups == null) return result;
            foreach (var fg in tbl.FieldGroups)
            {
                var dto = new FieldGroupDto { Name = fg.Name, Label = fg.Label };
                if (fg.Fields != null)
                    foreach (var f in fg.Fields)
                        dto.Fields.Add(f.DataField);
                result.Add(dto);
            }
            return result;
        }

        private static List<RelationDto> ExtractRelations(AxTable tbl)
        {
            var result = new List<RelationDto>();
            if (tbl?.Relations == null) return result;
            foreach (var rel in tbl.Relations)
            {
                var dto = new RelationDto { Name = rel.Name, RelatedTable = rel.RelatedTable };
                if (rel.Constraints != null)
                    foreach (var c in rel.Constraints)
                        if (c is AxTableRelationConstraintField fc)
                            dto.Constraints.Add(new RelationConstraintDto { Field = fc.Field, RelatedField = fc.RelatedField });
                result.Add(dto);
            }
            return result;
        }

        private static string TryGetLabelText(object controller, string labelId)
        {
            try
            {
                var method = controller.GetType().GetMethod("GetLabelText", new[] { typeof(string) });
                if (method == null) return string.Empty;
                return method.Invoke(controller, new object[] { labelId }) as string ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static IEnumerable<LabelEntry> TrySearchLabels(object controller, string searchText, int maxResults)
        {
            var results = new List<LabelEntry>();
            int limit = maxResults > 0 ? maxResults : 50;

            // Prefer text search when available.
            foreach (string methodName in new[] { "SearchByText", "SearchById", "GetAll" })
            {
                try
                {
                    var method = controller.GetType().GetMethod(methodName);
                    if (method == null) continue;

                    object raw;
                    if (method.GetParameters().Length == 1)
                    {
                        string arg = string.IsNullOrEmpty(searchText) ? "" : searchText;
                        raw = method.Invoke(controller, new object[] { arg });
                    }
                    else if (method.GetParameters().Length == 0)
                    {
                        raw = method.Invoke(controller, null);
                    }
                    else
                    {
                        continue;
                    }

                    if (raw is System.Collections.IEnumerable seq)
                    {
                        foreach (var item in seq)
                        {
                            if (item == null) continue;
                            string id = item.GetType().GetProperty("Id")?.GetValue(item)?.ToString();
                            string text = item.GetType().GetProperty("Text")?.GetValue(item)?.ToString();
                            if (string.IsNullOrEmpty(id))
                            {
                                id = item.GetType().GetProperty("LabelId")?.GetValue(item)?.ToString();
                            }
                            if (string.IsNullOrEmpty(text))
                            {
                                text = item.GetType().GetProperty("LabelText")?.GetValue(item)?.ToString();
                            }

                            if (!string.IsNullOrEmpty(searchText) && !string.IsNullOrEmpty(text)
                                && text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0
                                && (id == null || id.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0))
                            {
                                continue;
                            }

                            if (!string.IsNullOrEmpty(id))
                                results.Add(new LabelEntry { Id = id, Text = text ?? string.Empty });

                            if (results.Count >= limit)
                                return results;
                        }
                    }

                    if (results.Count > 0)
                        return results;
                }
                catch
                {
                    // Try next supported API shape.
                }
            }

            return results;
        }

        private bool IsCustomModel(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return false;
            string lower = modelName.ToLowerInvariant();
            string[] standard = {
                "applicationsuite", "applicationplatform", "applicationfoundation",
                "applicationcommon", "directory", "currency", "dimensions",
                "generalledger", "personnelcore", "taxengine", "retail",
                "supplychain", "projaccounting", "casemanagement"
            };
            foreach (string s in standard)
                if (lower.Contains(s)) return false;
            return true;
        }

        private static bool IsStandardPackagePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            string lower = filePath.Replace('/', '\\').ToLowerInvariant();
            string[] standardPackages = {
                "\\applicationsuite\\", "\\applicationplatform\\",
                "\\applicationfoundation\\", "\\applicationcommon\\",
                "\\directory\\", "\\currency\\", "\\dimensions\\",
                "\\generalledger\\", "\\personnelcore\\", "\\taxengine\\",
                "\\retail\\", "\\commerce\\", "\\supplychain\\"
            };
            foreach (string pkg in standardPackages)
                if (lower.Contains(pkg)) return true;
            return false;
        }

        /// <summary>
        /// Strips CDATA wrappers from a string value. The MetaModel API adds its
        /// own CDATA when serializing, so if the input already contains CDATA
        /// wrappers the result would be double-wrapped and corrupted.
        /// </summary>
        private static string StripCData(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            string trimmed = value.Trim();
            if (trimmed.StartsWith("<![CDATA[") && trimmed.EndsWith("]]>"))
                return trimmed.Substring(9, trimmed.Length - 12);
            return value;
        }

        private static MetaModelResult Ok(string message) =>
            new MetaModelResult { Success = true, Message = message };

        private static MetaModelResult Fail(string message) =>
            new MetaModelResult { Success = false, Message = message };

        // ── Type name search helpers ──

        private List<TypeSearcher> GetTypeSearchers(string objectType)
        {
            var searchers = new List<TypeSearcher>();

            if (string.IsNullOrEmpty(objectType) || objectType == "AxClass")
                searchers.Add(new TypeSearcher("AxClass", () => MetaService.GetClassNames()));
            if (string.IsNullOrEmpty(objectType) || objectType == "AxTable")
                searchers.Add(new TypeSearcher("AxTable", () => MetaService.GetTableNames()));
            if (string.IsNullOrEmpty(objectType) || objectType == "AxForm")
                searchers.Add(new TypeSearcher("AxForm", () => MetaService.GetFormNames()));
            if (string.IsNullOrEmpty(objectType) || objectType == "AxEnum")
                searchers.Add(new TypeSearcher("AxEnum", () => MetaService.GetEnumNames()));
            if (string.IsNullOrEmpty(objectType) || objectType == "AxView")
                searchers.Add(new TypeSearcher("AxView", () => MetaService.GetViewNames()));
            if (string.IsNullOrEmpty(objectType) || objectType == "AxQuery")
                searchers.Add(new TypeSearcher("AxQuery", () => MetaService.GetQueryNames()));
            if (string.IsNullOrEmpty(objectType) || objectType == "AxEdt")
                searchers.Add(new TypeSearcher("AxEdt", () => MetaService.GetExtendedDataTypeNames()));
            if (string.IsNullOrEmpty(objectType) || objectType == "AxMenuItemDisplay")
                searchers.Add(new TypeSearcher("AxMenuItemDisplay", () => MetaService.GetMenuItemDisplayNames()));
            if (string.IsNullOrEmpty(objectType) || objectType == "AxMenuItemOutput")
                searchers.Add(new TypeSearcher("AxMenuItemOutput", () => MetaService.GetMenuItemOutputNames()));
            if (string.IsNullOrEmpty(objectType) || objectType == "AxMenuItemAction")
                searchers.Add(new TypeSearcher("AxMenuItemAction", () => MetaService.GetMenuItemActionNames()));

            return searchers;
        }

        private sealed class TypeSearcher
        {
            public string TypeName { get; }
            private readonly Func<IEnumerable<string>> _getNamesFunc;

            public TypeSearcher(string typeName, Func<IEnumerable<string>> getNamesFunc)
            {
                TypeName = typeName;
                _getNamesFunc = getNamesFunc;
            }

            public IEnumerable<string> GetNames() => _getNamesFunc();
        }
    }
}
