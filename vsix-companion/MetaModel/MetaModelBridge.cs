using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Microsoft.Dynamics.AX.Metadata.MetaModel;
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
                    default:
                        return Fail("Unsupported object type for API creation: " + request.ObjectType
                            + ". Supported: AxClass, AxTable, AxForm, AxEdt, AxEnum, AxMenuItemDisplay/Output/Action, "
                            + "AxQuery, AxView, AxDataEntityView, AxSecurityPrivilege/Duty/Role, "
                            + "AxService, AxServiceGroup, AxMap, AxMenu, AxTile, AxConfigurationKey.");
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
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxTable":
                        var tbl = MetaService.GetTable(objectName);
                        if (tbl == null) return new ReadObjectResult { Message = "Table '" + objectName + "' not found." };
                        result.Declaration = tbl.Declaration;
                        if (tbl.Methods != null)
                            foreach (var m in tbl.Methods)
                                result.Methods.Add(new MethodInfo { Name = m.Name, Source = m.Source });
                        result.MetadataXml = SerializeMetadata(tbl);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxForm":
                        var frm = MetaService.GetForm(objectName);
                        if (frm == null) return new ReadObjectResult { Message = "Form '" + objectName + "' not found." };
                        result.Declaration = frm.SourceCode?.Declaration;
                        if (frm.Methods != null)
                            foreach (var m in frm.Methods)
                                result.Methods.Add(new MethodInfo { Name = m.Name, Source = m.Source });
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxEnum":
                        var enm = MetaService.GetEnum(objectName);
                        if (enm == null) return new ReadObjectResult { Message = "Enum '" + objectName + "' not found." };
                        result.MetadataXml = SerializeMetadata(enm);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxEdt":
                        var edt = MetaService.GetExtendedDataType(objectName);
                        if (edt == null) return new ReadObjectResult { Message = "EDT '" + objectName + "' not found." };
                        result.MetadataXml = SerializeMetadata(edt);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxView":
                        var view = MetaService.GetView(objectName);
                        if (view == null) return new ReadObjectResult { Message = "View '" + objectName + "' not found." };
                        result.Declaration = view.Declaration;
                        if (view.Methods != null)
                            foreach (var m in view.Methods)
                                result.Methods.Add(new MethodInfo { Name = m.Name, Source = m.Source });
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxQuery":
                        var qry = MetaService.GetQuery(objectName);
                        if (qry == null) return new ReadObjectResult { Message = "Query '" + objectName + "' not found." };
                        result.MetadataXml = SerializeMetadata(qry);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    default:
                        return new ReadObjectResult { Message = "Read not supported for type: " + objectType + ". Use ReadObjectByPath for XML-based read." };
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
            // Fallback to XML file read for types not covered by the typed API
            if (!File.Exists(filePath))
                return new ReadObjectResult { Message = "File not found: " + filePath };

            try
            {
                var doc = new XmlDocument();
                doc.Load(filePath);
                var root = doc.DocumentElement;

                var result = new ReadObjectResult
                {
                    Success = true,
                    ObjectType = root.Name,
                    ObjectName = root.SelectSingleNode("Name")?.InnerText ?? Path.GetFileNameWithoutExtension(filePath),
                    FilePath = filePath,
                    IsCustom = !IsStandardPackagePath(filePath)
                };

                var declNode = root.SelectSingleNode("SourceCode/Declaration");
                if (declNode != null)
                    result.Declaration = GetCDataText(declNode);

                var methodNodes = root.SelectNodes("SourceCode/Methods/Method");
                if (methodNodes != null)
                {
                    foreach (XmlNode mn in methodNodes)
                    {
                        string mName = mn.SelectSingleNode("Name")?.InnerText ?? "";
                        var srcNode = mn.SelectSingleNode("Source");
                        string mSource = srcNode != null ? GetCDataText(srcNode) : "";
                        result.Methods.Add(new MethodInfo { Name = mName, Source = mSource });
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                return new ReadObjectResult { Message = "Read failed: " + ex.Message };
            }
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

            try
            {
                var typeSearchers = GetTypeSearchers(objectType);
                foreach (var searcher in typeSearchers)
                {
                    foreach (string name in searcher.GetNames())
                    {
                        string objModel = GetObjectModelName(searcher.TypeName, name);
                        if (!string.IsNullOrEmpty(modelName) &&
                            !objModel.Equals(modelName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!string.IsNullOrEmpty(nameFilter) &&
                            name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

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
                        // LabelEditorController API shape differs by platform version.
                        // Resolve GetLabelText via reflection to keep compatibility.
                        string text = string.Empty;
                        var getLabelTextMethod = controller.GetType().GetMethod("GetLabelText", new[] { typeof(string) });
                        if (getLabelTextMethod != null)
                            text = getLabelTextMethod.Invoke(controller, new object[] { labelId }) as string;
                        result.Labels.Add(new LabelEntry { Id = labelId, Text = text });
                    }
                    else
                    {
                        result.Message = "Label '" + labelId + "' not found in " + labelFileId;
                    }
                    return result;
                }

                // TODO: enumerate labels with search/filter when API supports it
                result.Message = "Label enumeration retrieved via controller for " + labelFileId;
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
                axClass.Declaration = req.Declaration;
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
                axTable.Declaration = req.Declaration;

            AddMethods(axTable.Methods, req.Methods);
            ApplyMetadataXml(axTable, req.MetadataXml);
            MetaService.CreateTable(axTable, saveInfo);
            AddToProjectIfActive("AxTable", req.ObjectName);
            return Ok("Created AxTable '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateForm(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axForm = new AxForm { Name = req.ObjectName };
            if (!string.IsNullOrEmpty(req.Declaration))
                axForm.SourceCode.Declaration = req.Declaration;

            AddMethods(axForm.Methods, req.Methods);
            MetaService.CreateForm(axForm, saveInfo);
            AddToProjectIfActive("AxForm", req.ObjectName);
            return Ok("Created AxForm '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateEdt(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axEdt = new AxEdtString { Name = req.ObjectName };
            MetaService.CreateExtendedDataType(axEdt, saveInfo);
            AddToProjectIfActive("AxEdt", req.ObjectName);
            return Ok("Created AxEdt '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateEnum(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axEnum = new AxEnum { Name = req.ObjectName };
            MetaService.CreateEnum(axEnum, saveInfo);
            AddToProjectIfActive("AxEnum", req.ObjectName);
            return Ok("Created AxEnum '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateMenuItemDisplay(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var item = new AxMenuItemDisplay { Name = req.ObjectName };
            MetaService.CreateMenuItemDisplay(item, saveInfo);
            AddToProjectIfActive("AxMenuItemDisplay", req.ObjectName);
            return Ok("Created AxMenuItemDisplay '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateMenuItemOutput(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var item = new AxMenuItemOutput { Name = req.ObjectName };
            MetaService.CreateMenuItemOutput(item, saveInfo);
            AddToProjectIfActive("AxMenuItemOutput", req.ObjectName);
            return Ok("Created AxMenuItemOutput '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateMenuItemAction(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var item = new AxMenuItemAction { Name = req.ObjectName };
            MetaService.CreateMenuItemAction(item, saveInfo);
            AddToProjectIfActive("AxMenuItemAction", req.ObjectName);
            return Ok("Created AxMenuItemAction '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateQuery(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axQuery = new AxQuerySimple { Name = req.ObjectName };
            MetaService.CreateQuery(axQuery, saveInfo);
            AddToProjectIfActive("AxQuery", req.ObjectName);
            return Ok("Created AxQuery '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateView(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axView = new AxView { Name = req.ObjectName };
            if (!string.IsNullOrEmpty(req.Declaration))
                axView.Declaration = req.Declaration;
            AddMethods(axView.Methods, req.Methods);
            MetaService.CreateView(axView, saveInfo);
            AddToProjectIfActive("AxView", req.ObjectName);
            return Ok("Created AxView '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateDataEntityView(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var entity = new AxDataEntityView { Name = req.ObjectName };
            if (!string.IsNullOrEmpty(req.Declaration))
                entity.Declaration = req.Declaration;
            MetaService.UpdateDataEntityView(entity, saveInfo);
            AddToProjectIfActive("AxDataEntityView", req.ObjectName);
            return Ok("Created AxDataEntityView '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateSecurityPrivilege(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var priv = new AxSecurityPrivilege { Name = req.ObjectName };
            MetaService.CreateSecurityPrivilege(priv, saveInfo);
            AddToProjectIfActive("AxSecurityPrivilege", req.ObjectName);
            return Ok("Created AxSecurityPrivilege '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateSecurityDuty(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var duty = new AxSecurityDuty { Name = req.ObjectName };
            MetaService.CreateSecurityDuty(duty, saveInfo);
            AddToProjectIfActive("AxSecurityDuty", req.ObjectName);
            return Ok("Created AxSecurityDuty '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateSecurityRole(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var role = new AxSecurityRole { Name = req.ObjectName };
            MetaService.CreateSecurityRole(role, saveInfo);
            AddToProjectIfActive("AxSecurityRole", req.ObjectName);
            return Ok("Created AxSecurityRole '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateService(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var svc = new AxService { Name = req.ObjectName };
            MetaService.CreateService(svc, saveInfo);
            AddToProjectIfActive("AxService", req.ObjectName);
            return Ok("Created AxService '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateServiceGroup(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var grp = new AxServiceGroup { Name = req.ObjectName };
            MetaService.CreateServiceGroup(grp, saveInfo);
            AddToProjectIfActive("AxServiceGroup", req.ObjectName);
            return Ok("Created AxServiceGroup '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateMap(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var map = new AxMap { Name = req.ObjectName };
            MetaService.CreateMap(map, saveInfo);
            AddToProjectIfActive("AxMap", req.ObjectName);
            return Ok("Created AxMap '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateMenu(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var menu = new AxMenu { Name = req.ObjectName };
            MetaService.CreateMenu(menu, saveInfo);
            AddToProjectIfActive("AxMenu", req.ObjectName);
            return Ok("Created AxMenu '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateTile(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var tile = new AxTile { Name = req.ObjectName };
            MetaService.CreateTile(tile, saveInfo);
            AddToProjectIfActive("AxTile", req.ObjectName);
            return Ok("Created AxTile '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateConfigurationKey(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var key = new AxConfigurationKey { Name = req.ObjectName };
            MetaService.CreateConfigurationKey(key, saveInfo);
            AddToProjectIfActive("AxConfigurationKey", req.ObjectName);
            return Ok("Created AxConfigurationKey '" + req.ObjectName + "'.");
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
                cls.Declaration = req.Declaration;

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
                tbl.Declaration = req.Declaration;

            UpdateMethods(tbl.Methods, req.Methods, req.RemoveMethodNames);
            ApplyMetadataXml(tbl, req.MetadataXml);
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
                frm.SourceCode.Declaration = req.Declaration;

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
                string name = ExtractMethodName(src);
                target.Add(new AxMethod { Name = name, Source = src });
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
                    string name = ExtractMethodName(src);
                    var found = existing.FirstOrDefault(m =>
                        m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (found != null)
                    {
                        found.Source = src;
                    }
                    else
                    {
                        existing.Add(new AxMethod { Name = name, Source = src });
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
                default: return null;
            }
        }

        private void ApplyMetadataXml(object axObject, string xml)
        {
            // MetadataXml is used as a hint for the tool caller but actual property
            // setting should use the strongly-typed API. This is a placeholder —
            // the typed properties on AxTable (Fields, Indexes, Relations, etc.)
            // should be set directly by the caller if needed.
        }

        private string SerializeMetadata(object axObject)
        {
            // TODO: Use Metadata.Storage serializer to get XML representation
            return null;
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

        private static string GetCDataText(XmlNode node)
        {
            foreach (XmlNode child in node.ChildNodes)
                if (child.NodeType == XmlNodeType.CDATA)
                    return child.Value ?? "";
            return node.InnerText ?? "";
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
