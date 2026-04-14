using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Dynamics.AX.Metadata.MetaModel;
using Microsoft.Dynamics.AX.Metadata.Core.MetaModel;
using Microsoft.Dynamics.AX.Metadata.Service;
using Microsoft.Dynamics.AX.Framework.Xlnt.XReference;
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
        
        // Cache for GetObjectModelName with 60-second TTL
        private class CachedModelName
        {
            public string ModelName { get; set; }
            public DateTime CachedAt { get; set; }
        }
        private System.Collections.Generic.Dictionary<string, CachedModelName> _objectModelNameCache =
            new System.Collections.Generic.Dictionary<string, CachedModelName>();
        private const int ModelNameCacheTtlSeconds = 60;
        
        // Cache for ListProjectItems with 60-second TTL
        private class CachedProjectItems
        {
            public System.Collections.Generic.List<ProjectItemInfo> Items { get; set; }
            public DateTime CachedAt { get; set; }
        }
        private CachedProjectItems _projectItemsCache;
        private const int ProjectItemsCacheTtlSeconds = 60;

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

                    // ── Extension types (no typed Create API — uses serialization to disk) ──
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
                        return UpdateGenericByTypedApi(request);
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
                        result.TypedMetadataJson = BuildTypedMetadataJson(objectType, cls);
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
                        result.TypedMetadataJson = BuildTypedMetadataJson(objectType, tbl);
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
                        result.TypedMetadataJson = BuildTypedMetadataJson(objectType, frm);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxEnum":
                        var enm = MetaService.GetEnum(objectName);
                        if (enm == null) return new ReadObjectResult { Message = "Enum '" + objectName + "' not found." };
                        result.Properties = ExtractProperties(enm);
                        result.EnumValues = ExtractEnumValues(enm);
                        result.TypedMetadataJson = BuildTypedMetadataJson(objectType, enm);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxEdt":
                        var edt = MetaService.GetExtendedDataType(objectName);
                        if (edt == null) return new ReadObjectResult { Message = "EDT '" + objectName + "' not found." };
                        result.Properties = ExtractProperties(edt);
                        result.TypedMetadataJson = BuildTypedMetadataJson(objectType, edt);
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
                        result.TypedMetadataJson = BuildTypedMetadataJson(objectType, view);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    case "AxQuery":
                        var qry = MetaService.GetQuery(objectName);
                        if (qry == null) return new ReadObjectResult { Message = "Query '" + objectName + "' not found." };
                        result.Properties = ExtractProperties(qry);
                        result.DataSources = ExtractQueryDataSources(qry);
                        result.TypedMetadataJson = BuildTypedMetadataJson(objectType, qry);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;

                    default:
                        object genericObj;
                        string readError;
                        if (!TryGetObjectByTypeName(objectType, objectName, out genericObj, out readError) || genericObj == null)
                            return new ReadObjectResult { Message = readError ?? ("Read not supported for type: " + objectType + ".") };

                        PopulateReadResultFromGenericObject(result, objectType, genericObj);
                        result.ModelName = GetObjectModelName(objectType, objectName);
                        break;
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
            // <root>\<Package>\<Model>\AxClass\MyClass.axclass → type=AxClass, name=MyClass
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

        private bool TryGetObjectByTypeName(string objectType, string objectName, out object obj, out string error)
        {
            obj = null;
            error = null;
            if (string.IsNullOrWhiteSpace(objectType) || string.IsNullOrWhiteSpace(objectName))
            {
                error = "objectType and objectName are required.";
                return false;
            }

            string typeKey = objectType.StartsWith("Ax", StringComparison.OrdinalIgnoreCase)
                ? objectType.Substring(2)
                : objectType;

            var methodCandidates = new List<string>
            {
                "Get" + typeKey,
                "Get" + typeKey + "s"
            };

            if (string.Equals(objectType, "AxEdt", StringComparison.OrdinalIgnoreCase))
                methodCandidates.Insert(0, "GetExtendedDataType");
            if (string.Equals(objectType, "AxQuery", StringComparison.OrdinalIgnoreCase))
                methodCandidates.Insert(0, "GetQuery");
            if (string.Equals(objectType, "AxDataEntityView", StringComparison.OrdinalIgnoreCase))
                methodCandidates.Insert(0, "GetDataEntityView");

            foreach (string methodName in methodCandidates.Distinct(StringComparer.Ordinal))
            {
                try
                {
                    var method = MetaService.GetType().GetMethod(methodName, new[] { typeof(string) });
                    if (method == null) continue;
                    obj = method.Invoke(MetaService, new object[] { objectName });
                    if (obj != null) return true;
                }
                catch
                {
                    // Try next candidate.
                }
            }

            error = "Object '" + objectName + "' not found for type '" + objectType + "', or type is not readable via IMetaModelService.";
            return false;
        }

        private static void PopulateReadResultFromGenericObject(ReadObjectResult result, string objectType, object axObj)
        {
            if (result == null || axObj == null) return;

            result.Properties = ExtractProperties(axObj);
            result.TypedMetadataJson = BuildTypedMetadataJson(objectType, axObj);

            var declarationProp = axObj.GetType().GetProperty("Declaration");
            result.Declaration = declarationProp?.GetValue(axObj) as string;

            if (string.IsNullOrEmpty(result.Declaration))
            {
                var sourceCodeProp = axObj.GetType().GetProperty("SourceCode");
                var sourceCode = sourceCodeProp?.GetValue(axObj);
                if (sourceCode != null)
                {
                    var srcDeclProp = sourceCode.GetType().GetProperty("Declaration");
                    result.Declaration = srcDeclProp?.GetValue(sourceCode) as string;
                }
            }

            var methodsProp = axObj.GetType().GetProperty("Methods");
            var methods = methodsProp?.GetValue(axObj) as System.Collections.IEnumerable;
            if (methods != null)
            {
                foreach (var m in methods)
                {
                    if (m == null) continue;
                    string name = m.GetType().GetProperty("Name")?.GetValue(m) as string;
                    string source = m.GetType().GetProperty("Source")?.GetValue(m) as string;
                    if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(source))
                        result.Methods.Add(new MethodInfo { Name = name, Source = source });
                }
            }
        }

        private static string BuildTypedMetadataJson(string objectType, object axObj)
        {
            if (axObj == null) return null;
            try
            {
                var serializer = new JavaScriptSerializer
                {
                    MaxJsonLength = int.MaxValue,
                    RecursionLimit = 64
                };

                Type proxyType = ResolveProxyType(objectType);
                if (proxyType != null)
                {
                    var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
                    object proxy = MapSourceToTargetType(axObj, proxyType, 0, visited);
                    return serializer.Serialize(proxy);
                }

                var visitedFallback = new HashSet<object>(ReferenceEqualityComparer.Instance);
                object graph = ExtractMetadataGraph(axObj, 0, visitedFallback);
                return serializer.Serialize(graph);
            }
            catch
            {
                return null;
            }
        }

        private static object ExtractMetadataGraph(object value, int depth, HashSet<object> visited)
        {
            if (value == null) return null;
            if (depth > 12) return null;

            Type t = value.GetType();
            Type nt = Nullable.GetUnderlyingType(t) ?? t;

            if (nt.IsEnum) return value.ToString();
            if (nt.IsPrimitive || nt == typeof(string) || nt == typeof(decimal)
                || nt == typeof(DateTime) || nt == typeof(Guid))
                return value;

            if (!t.IsValueType)
            {
                if (!visited.Add(value)) return null;
            }

            if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                var list = new List<object>();
                foreach (object item in enumerable)
                {
                    object extracted = ExtractMetadataGraph(item, depth + 1, visited);
                    if (extracted != null) list.Add(extracted);
                }
                return list;
            }

            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                if (prop.Name == "Parent" || prop.Name == "Owner") continue;

                object propValue;
                try { propValue = prop.GetValue(value); }
                catch { continue; }

                object extracted = ExtractMetadataGraph(propValue, depth + 1, visited);
                if (extracted != null) dict[prop.Name] = extracted;
            }

            return dict;
        }

        private static Type ResolveProxyType(string objectType)
        {
            if (string.IsNullOrWhiteSpace(objectType)) return null;
            string fullName = "XppAiCopilotCompanion.MetaModel.Generated." + objectType + "Proxy";

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false, true);
                if (t != null) return t;
            }

            return null;
        }

        private static object MapSourceToTargetType(object source, Type targetType, int depth, HashSet<object> visited)
        {
            if (targetType == null || depth > 12) return null;

            Type targetUnderlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (source == null)
            {
                if (targetUnderlying.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                    return Activator.CreateInstance(targetUnderlying);
                return null;
            }

            if (IsSimpleType(targetUnderlying))
                return ConvertSimpleValue(source, targetUnderlying) ?? source?.ToString();

            if (!source.GetType().IsValueType)
            {
                if (!visited.Add(source)) return null;
            }

            if (targetUnderlying != typeof(string)
                && typeof(System.Collections.IEnumerable).IsAssignableFrom(targetUnderlying))
            {
                Type itemType = GetEnumerableItemType(targetUnderlying) ?? typeof(object);
                var list = new List<object>();
                if (source is System.Collections.IEnumerable srcEnumerable)
                {
                    foreach (object item in srcEnumerable)
                    {
                        object mapped = MapSourceToTargetType(item, itemType, depth + 1, visited);
                        if (mapped != null) list.Add(mapped);
                    }
                }

                object targetList = Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
                var add = targetList.GetType().GetMethod("Add");
                foreach (object item in list) add?.Invoke(targetList, new[] { item });
                return targetList;
            }

            object target = Activator.CreateInstance(targetUnderlying);
            var srcType = source.GetType();
            foreach (var tp in targetUnderlying.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!tp.CanRead || tp.GetIndexParameters().Length > 0) continue;

                var sp = srcType.GetProperty(tp.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (sp == null || !sp.CanRead || sp.GetIndexParameters().Length > 0) continue;

                object srcValue;
                try { srcValue = sp.GetValue(source); }
                catch { continue; }

                object mapped = MapSourceToTargetType(srcValue, tp.PropertyType, depth + 1, visited);
                SetPropertyValue(tp, target, mapped);
            }

            return target;
        }

        private static void SetPropertyValue(PropertyInfo property, object target, object value)
        {
            try
            {
                var setter = property.GetSetMethod(true);
                if (setter != null)
                {
                    setter.Invoke(target, new[] { value });
                    return;
                }
            }
            catch { }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
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

            if (!string.IsNullOrWhiteSpace(req.TypedMetadataJson))
            {
                if (string.IsNullOrWhiteSpace(readResult.TypedMetadataJson))
                {
                    mismatches.Add("typedMetadata: expected object graph but read result returned none");
                }
                else
                {
                    try
                    {
                        object expected = ParseTypedMetadataToNode(req.ObjectType, req.TypedMetadataJson);
                        object actual = ParseTypedMetadataToNode(req.ObjectType, readResult.TypedMetadataJson);
                        ValidateMetadataSubset(expected, actual, "typedMetadata", mismatches, 0);
                    }
                    catch (Exception ex)
                    {
                        mismatches.Add("typedMetadata: validation parse error: " + ex.Message);
                    }
                }
            }

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
                // Build HashSet for O(1) lookups instead of O(n) .Any() searches
                var fieldNameSet = readResult.Fields != null
                    ? new System.Collections.Generic.HashSet<string>(
                        readResult.Fields.Select(x => x.Name ?? ""),
                        StringComparer.OrdinalIgnoreCase)
                    : new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var f in req.Fields)
                {
                    if (!fieldNameSet.Contains(f.Name ?? ""))
                        mismatches.Add("Field '" + f.Name + "': expected to exist, not found");
                }
            }

            if (req.EnumValues != null)
            {
                // Build HashSet for O(1) lookups instead of O(n) .Any() searches
                var enumNameSet = readResult.EnumValues != null
                    ? new System.Collections.Generic.HashSet<string>(
                        readResult.EnumValues.Select(x => x.Name ?? ""),
                        StringComparer.OrdinalIgnoreCase)
                    : new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var ev in req.EnumValues)
                {
                    if (!enumNameSet.Contains(ev.Name ?? ""))
                        mismatches.Add("EnumValue '" + ev.Name + "': expected to exist, not found");
                }
            }

            if (req.Indexes != null)
            {
                // Build HashSet for O(1) lookups instead of O(n) .Any() searches
                var indexNameSet = readResult.Indexes != null
                    ? new System.Collections.Generic.HashSet<string>(
                        readResult.Indexes.Select(x => x.Name ?? ""),
                        StringComparer.OrdinalIgnoreCase)
                    : new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var idx in req.Indexes)
                {
                    if (!indexNameSet.Contains(idx.Name ?? ""))
                        mismatches.Add("Index '" + idx.Name + "': expected to exist, not found");
                }
            }

            if (req.Relations != null)
            {
                // Build HashSet for O(1) lookups instead of O(n) .Any() searches
                var relationNameSet = readResult.Relations != null
                    ? new System.Collections.Generic.HashSet<string>(
                        readResult.Relations.Select(x => x.Name ?? ""),
                        StringComparer.OrdinalIgnoreCase)
                    : new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var rel in req.Relations)
                {
                    if (!relationNameSet.Contains(rel.Name ?? ""))
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

        private static void ValidateMetadataSubset(object expected, object actual, string path, List<string> mismatches, int depth)
        {
            if (depth > 10) return;
            if (expected == null) return;
            if (actual == null)
            {
                mismatches.Add(path + ": expected value present, actual is null");
                return;
            }

            if (expected is Dictionary<string, object> expDict)
            {
                var actDict = actual as Dictionary<string, object>;
                if (actDict == null)
                {
                    mismatches.Add(path + ": expected object, actual is " + actual.GetType().Name);
                    return;
                }

                foreach (var kv in expDict)
                {
                    if (!actDict.TryGetValue(kv.Key, out object actVal))
                    {
                        mismatches.Add(path + "." + kv.Key + ": missing");
                        continue;
                    }
                    ValidateMetadataSubset(kv.Value, actVal, path + "." + kv.Key, mismatches, depth + 1);
                }
                return;
            }

            var expArray = ToObjectArray(expected);
            if (expArray != null)
            {
                var actArray = ToObjectArray(actual);
                if (actArray == null)
                {
                    mismatches.Add(path + ": expected array, actual is " + actual.GetType().Name);
                    return;
                }

                if (actArray.Length < expArray.Length)
                    mismatches.Add(path + ": expected at least " + expArray.Length + " items, actual " + actArray.Length);

                int compareCount = Math.Min(expArray.Length, actArray.Length);
                for (int i = 0; i < compareCount; i++)
                    ValidateMetadataSubset(expArray[i], actArray[i], path + "[" + i + "]", mismatches, depth + 1);

                return;
            }

            string expText = Convert.ToString(expected);
            string actText = Convert.ToString(actual);
            if (!string.Equals(expText, actText, StringComparison.OrdinalIgnoreCase))
                mismatches.Add(path + ": expected '" + expText + "', actual '" + actText + "'");
        }

        private static object[] ToObjectArray(object node)
        {
            if (node == null) return null;
            if (node is object[] arr) return arr;
            if (node is System.Collections.ArrayList al) return al.ToArray();
            if (node is System.Collections.IEnumerable en && !(node is string))
            {
                var list = new List<object>();
                foreach (object item in en) list.Add(item);
                return list.ToArray();
            }
            return null;
        }

        // ── Discovery ──

        public FindObjectResult FindObject(string objectName, string objectType, bool exactMatch, int maxResults)
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
                    try
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

                                if (result.Matches.Count >= maxResults) break;
                            }
                        }
                        if (result.Matches.Count >= maxResults) break;
                    }
                    catch (Exception typeEx)
                    {
                        // Log per-type error but continue searching other types
                        // This allows partial results even if one metadata extractor fails
                        System.Diagnostics.Debug.WriteLine("FindObject type search error for type '" + searcher.TypeName + "': " + typeEx.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                string svcContext = MetaService == null ? "[MetaService unavailable]" : "[MetaService active]";
                result.Message = "Search error in FindObject (searching for '" + objectName + "' in type '" + objectType + "'): " + svcContext + " - " + ex.Message;
            }

            if (result.Matches.Count == 0)
                result.Message = "No objects found matching '" + objectName + "'."
                    + (string.IsNullOrEmpty(objectType) ? "" : " (type: " + objectType + ")")
                    + " Found in " + result.Matches.Count + " type(s).";

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

        public ObjectTypeSchemaResult GetObjectTypeSchema(string objectType)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (string.IsNullOrWhiteSpace(objectType))
                return new ObjectTypeSchemaResult
                {
                    Success = false,
                    ObjectType = objectType,
                    Message = "objectType is required."
                };

            try
            {
                Type metadataType = ResolveMetadataType(objectType);
                if (metadataType == null)
                {
                    return new ObjectTypeSchemaResult
                    {
                        Success = false,
                        ObjectType = objectType,
                        Message = "Unsupported or unknown object type: " + objectType
                    };
                }

                var visited = new HashSet<Type>();
                object schema = BuildTypeSchema(metadataType, 0, visited);

                var serializer = new JavaScriptSerializer
                {
                    MaxJsonLength = int.MaxValue,
                    RecursionLimit = 64
                };

                return new ObjectTypeSchemaResult
                {
                    Success = true,
                    ObjectType = objectType,
                    SchemaJson = serializer.Serialize(schema),
                    Message = "Schema generated for " + objectType
                };
            }
            catch (Exception ex)
            {
                return new ObjectTypeSchemaResult
                {
                    Success = false,
                    ObjectType = objectType,
                    Message = "Schema generation failed: " + ex.Message
                };
            }
        }

        public ProxyGenerationResult GenerateProxies(ProxyGenerationRequest request)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var normalized = request ?? new ProxyGenerationRequest();
            var selectedTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            var requested = (normalized.ObjectTypes != null && normalized.ObjectTypes.Count > 0)
                ? normalized.ObjectTypes
                : GetDefaultProxyObjectTypes();

            foreach (var objectType in requested)
            {
                if (string.IsNullOrWhiteSpace(objectType))
                    continue;

                var metadataType = ResolveMetadataType(objectType.Trim());
                if (metadataType != null)
                    selectedTypes[objectType.Trim()] = metadataType;
            }

            if (selectedTypes.Count == 0)
            {
                return new ProxyGenerationResult
                {
                    Success = false,
                    Message = "No valid object types were provided for proxy generation."
                };
            }

            var result = MetaModelProxyGenerator.Generate(selectedTypes, normalized);
            if (!result.Success)
                return result;

            string outputPath = normalized.OutputFilePath;
            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = ResolveDefaultProxyOutputPath();

            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                try
                {
                    var directory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    File.WriteAllText(outputPath, result.GeneratedCode ?? string.Empty, Encoding.UTF8);
                    result.OutputFilePath = outputPath;
                    result.Message = result.Message + " Output written to '" + outputPath + "'.";
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = "Proxy code generated but failed to write output file: " + ex.Message;
                }
            }
            else
            {
                result.Message = result.Message + " Generated in memory only (no writable project path detected).";
            }

            return result;
        }

        private string ResolveDefaultProxyOutputPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var project = GetActiveProject();
                string projectFile = project?.FullName;
                if (string.IsNullOrWhiteSpace(projectFile))
                    return null;

                string projectDirectory = Path.GetDirectoryName(projectFile);
                if (string.IsNullOrWhiteSpace(projectDirectory))
                    return null;

                return Path.Combine(projectDirectory, "MetaModel", "Generated", "MetadataProxies.g.cs");
            }
            catch
            {
                return null;
            }
        }

        private static object BuildTypeSchema(Type type, int depth, HashSet<Type> visited)
        {
            if (type == null || depth > 8) return null;

            Type underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (IsSimpleType(underlying))
                return MapSimpleTypeToSchema(underlying);

            // Prevent cyclic loops in type graph
            if (!underlying.IsValueType && !visited.Add(underlying))
                return new Dictionary<string, object>
                {
                    { "kind", "object" },
                    { "type", underlying.Name },
                    { "recursive", true }
                };

            if (underlying != typeof(string)
                && typeof(System.Collections.IEnumerable).IsAssignableFrom(underlying))
            {
                Type itemType = GetEnumerableItemType(underlying) ?? typeof(object);
                return new Dictionary<string, object>
                {
                    { "kind", "array" },
                    { "item", BuildTypeSchema(itemType, depth + 1, visited) }
                };
            }

            var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in underlying.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                var propSchema = BuildTypeSchema(prop.PropertyType, depth + 1, new HashSet<Type>(visited));
                if (propSchema != null)
                    properties[prop.Name] = propSchema;
            }

            return new Dictionary<string, object>
            {
                { "kind", "object" },
                { "type", underlying.Name },
                { "properties", properties }
            };
        }

        private static Type GetEnumerableItemType(Type enumerableType)
        {
            if (enumerableType.IsArray)
                return enumerableType.GetElementType();

            if (enumerableType.IsGenericType)
            {
                var args = enumerableType.GetGenericArguments();
                if (args.Length == 1) return args[0];
            }

            Type ienum = enumerableType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType
                    && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return ienum?.GetGenericArguments().FirstOrDefault();
        }

        private static List<string> GetDefaultProxyObjectTypes()
        {
            return new List<string>
            {
                "AxClass",
                "AxTable",
                "AxForm",
                "AxEdt",
                "AxEnum",
                "AxView",
                "AxQuery",
                "AxMap",
                "AxMenu",
                "AxTile",
                "AxMenuItemDisplay",
                "AxMenuItemOutput",
                "AxMenuItemAction",
                "AxDataEntityView",
                "AxSecurityPrivilege",
                "AxSecurityDuty",
                "AxSecurityRole",
                "AxService",
                "AxServiceGroup",
                "AxConfigurationKey",
                "AxTableExtension",
                "AxFormExtension",
                "AxEnumExtension",
                "AxEdtExtension",
                "AxViewExtension",
                "AxMenuExtension",
                "AxMenuItemDisplayExtension",
                "AxMenuItemOutputExtension",
                "AxMenuItemActionExtension",
                "AxQuerySimpleExtension",
                "AxSecurityDutyExtension",
                "AxSecurityRoleExtension"
            };
        }

        private static Dictionary<string, object> MapSimpleTypeToSchema(Type type)
        {
            if (type == typeof(string))
                return new Dictionary<string, object> { { "kind", "scalar" }, { "type", "string" } };
            if (type == typeof(bool))
                return new Dictionary<string, object> { { "kind", "scalar" }, { "type", "boolean" } };
            if (type == typeof(int) || type == typeof(long) || type == typeof(short))
                return new Dictionary<string, object> { { "kind", "scalar" }, { "type", "integer" } };
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
                return new Dictionary<string, object> { { "kind", "scalar" }, { "type", "number" } };
            if (type == typeof(DateTime))
                return new Dictionary<string, object> { { "kind", "scalar" }, { "type", "datetime" } };
            if (type == typeof(Guid))
                return new Dictionary<string, object> { { "kind", "scalar" }, { "type", "guid" } };
            if (type.IsEnum)
                return new Dictionary<string, object>
                {
                    { "kind", "enum" },
                    { "type", type.Name },
                    { "values", Enum.GetNames(type) }
                };

            return new Dictionary<string, object> { { "kind", "scalar" }, { "type", type.Name } };
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
            
            // Check cache first
            if (_projectItemsCache != null && (DateTime.Now - _projectItemsCache.CachedAt).TotalSeconds < ProjectItemsCacheTtlSeconds)
            {
                return _projectItemsCache.Items;
            }
            
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

            // Cache the result
            _projectItemsCache = new CachedProjectItems
            {
                Items = items,
                CachedAt = DateTime.Now
            };

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
                axClass.Declaration = PrepareSource(req.Declaration, req.FormatCode);
            else
                axClass.Declaration = "class " + req.ObjectName + "\n{\n}";

            AddMethods(axClass.Methods, req.Methods, req.FormatCode);
            MetaService.CreateClass(axClass, saveInfo);
            AddToProjectIfActive(req.ObjectType ?? "AxClass", req.ObjectName);
            return Ok("Created AxClass '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateTable(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axTable = new AxTable { Name = req.ObjectName };
            if (!string.IsNullOrEmpty(req.Declaration))
                axTable.Declaration = PrepareSource(req.Declaration, req.FormatCode);

            AddMethods(axTable.Methods, req.Methods, req.FormatCode);
            try
            {
                ApplyTypedMetadata(axTable, req);
            }
            catch (Exception ex)
            {
                return Fail("ApplyTypedMetadata failed for AxTable '" + req.ObjectName + "': " + ex.Message);
            }
            MetaService.CreateTable(axTable, saveInfo);
            AddToProjectIfActive("AxTable", req.ObjectName);
            return Ok("Created AxTable '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateForm(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axForm = new AxForm { Name = req.ObjectName };
            if (!string.IsNullOrEmpty(req.Declaration))
                axForm.SourceCode.Declaration = PrepareSource(req.Declaration, req.FormatCode);

            AddMethods(axForm.Methods, req.Methods, req.FormatCode);
            try
            {
                ApplyTypedMetadata(axForm, req);
            }
            catch (Exception ex)
            {
                return Fail("ApplyTypedMetadata failed for AxForm '" + req.ObjectName + "': " + ex.Message);
            }
            MetaService.CreateForm(axForm, saveInfo);
            AddToProjectIfActive("AxForm", req.ObjectName);
            return Ok("Created AxForm '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateEdt(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axEdt = new AxEdtString { Name = req.ObjectName };
            try
            {
                ApplyTypedMetadata(axEdt, req);
            }
            catch (Exception ex)
            {
                return Fail("ApplyTypedMetadata failed for AxEdt '" + req.ObjectName + "': " + ex.Message);
            }
            MetaService.CreateExtendedDataType(axEdt, saveInfo);
            AddToProjectIfActive("AxEdt", req.ObjectName);
            return Ok("Created AxEdt '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateEnum(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var axEnum = new AxEnum { Name = req.ObjectName };
            try
            {
                ApplyTypedMetadata(axEnum, req);
            }
            catch (Exception ex)
            {
                return Fail("ApplyTypedMetadata failed for AxEnum '" + req.ObjectName + "': " + ex.Message);
            }
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
                axView.Declaration = PrepareSource(req.Declaration, req.FormatCode);
            AddMethods(axView.Methods, req.Methods, req.FormatCode);
            ApplyTypedMetadata(axView, req);
            MetaService.CreateView(axView, saveInfo);
            AddToProjectIfActive("AxView", req.ObjectName);
            return Ok("Created AxView '" + req.ObjectName + "'.");
        }

        private MetaModelResult CreateDataEntityView(CreateObjectRequest req, ModelSaveInfo saveInfo)
        {
            var entity = new AxDataEntityView { Name = req.ObjectName };
            if (!string.IsNullOrEmpty(req.Declaration))
                entity.Declaration = PrepareSource(req.Declaration, req.FormatCode);
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
                map.Declaration = PrepareSource(req.Declaration, req.FormatCode);
            AddMethods(map.Methods, req.Methods, req.FormatCode);
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
        /// Creates an extension object by serializing and writing to the
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
                cls.Declaration = PrepareSource(req.Declaration, req.FormatCode);

            UpdateMethods(cls.Methods, req.Methods, req.RemoveMethodNames, req.FormatCode);
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
                tbl.Declaration = PrepareSource(req.Declaration, req.FormatCode);

            UpdateMethods(tbl.Methods, req.Methods, req.RemoveMethodNames, req.FormatCode);
            try
            {
                ApplyTypedMetadata(tbl, req);
            }
            catch (Exception ex)
            {
                return Fail("ApplyTypedMetadata failed for AxTable '" + req.ObjectName + "': " + ex.Message);
            }
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
                frm.SourceCode.Declaration = PrepareSource(req.Declaration, req.FormatCode);

            UpdateMethods(frm.Methods, req.Methods, req.RemoveMethodNames, req.FormatCode);
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

        private MetaModelResult UpdateGenericByTypedApi(UpdateObjectRequest req)
        {
            return Fail("Update for type '" + req.ObjectType + "' is not yet supported via the typed API. "
                + "Supported types: AxClass, AxTable, AxForm, AxEdt, AxEnum.");
        }

        // ── Utility methods ──

        private void AddMethods(IList<AxMethod> target, string[] sources, bool formatCode)
        {
            if (sources == null) return;
            foreach (string src in sources)
            {
                if (string.IsNullOrWhiteSpace(src)) continue;
                string clean = PrepareSource(src, formatCode);
                string name = ExtractMethodName(clean);
                target.Add(new AxMethod { Name = name, Source = clean });
            }
        }

        private void UpdateMethods(IList<AxMethod> existing, string[] upserts, string[] removals, bool formatCode)
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
                    string clean = PrepareSource(src, formatCode);
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

        private static string PrepareSource(string source, bool formatCode)
        {
            string clean = StripCData(source);
            return formatCode ? NormalizeXppWhitespace(clean) : clean;
        }

        private static string NormalizeXppWhitespace(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return source;

            string normalized = source.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = normalized.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Replace("\t", "    ");
                lines[i] = line.TrimEnd();
            }
            return string.Join("\n", lines).TrimEnd();
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
            // Create cache key
            string cacheKey = objectType + ":" + objectName;
            
            // Check cache first
            if (_objectModelNameCache.TryGetValue(cacheKey, out CachedModelName cached))
            {
                // Check if cache is still fresh (within TTL)
                if ((DateTime.Now - cached.CachedAt).TotalSeconds < ModelNameCacheTtlSeconds)
                {
                    return cached.ModelName;
                }
                else
                {
                    // Expired cache entry - remove it
                    _objectModelNameCache.Remove(cacheKey);
                }
            }
            
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

                string modelName = modelInfo?.Name;
                
                // Cache the result
                _objectModelNameCache[cacheKey] = new CachedModelName
                {
                    ModelName = modelName,
                    CachedAt = DateTime.Now
                };
                
                return modelName;
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
            if (!string.IsNullOrWhiteSpace(req.TypedMetadataJson))
            {
                ApplyTypedMetadataGraph(target, req.ObjectType, req.TypedMetadataJson);
                return;
            }

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
            if (target is AxQuerySimple axQuery)
                ApplyQueryDataSources(axQuery, req.DataSources);
            ApplyEntryPoints(target, req.EntryPoints);
        }

        private void ApplyTypedMetadata(object target, UpdateObjectRequest req)
        {
            if (!string.IsNullOrWhiteSpace(req.TypedMetadataJson))
            {
                ApplyTypedMetadataGraph(target, req.ObjectType, req.TypedMetadataJson);
                return;
            }

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
            if (target is AxQuerySimple axQuery)
                ApplyQueryDataSources(axQuery, req.DataSources);
            ApplyEntryPoints(target, req.EntryPoints);
        }

        private static void ApplyTypedMetadataGraph(object target, string objectType, string typedMetadataJson)
        {
            if (target == null || string.IsNullOrWhiteSpace(typedMetadataJson)) return;

            try
            {
                object parsed = ParseTypedMetadataToNode(objectType, typedMetadataJson);
                var dict = parsed as Dictionary<string, object>;
                if (dict == null) return;

                // AxQuery needs concrete root/embedded datasource creation semantics.
                // These keys are handled by the specialized method and must be excluded
                // from the generic ApplyObjectNode pass to prevent double-clear wiping them.
                var handledKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (target is AxQuerySimple query)
                {
                    ApplyQueryDataSourcesFromTypedMetadata(query, dict);
                    handledKeys.Add("DataSources");
                    handledKeys.Add("dataSources");
                }

                ApplyObjectNode(target, dict, handledKeys);
            }
            catch
            {
                // Keep tool resilient; caller still gets base create/update result.
            }
        }

        private static object ParseTypedMetadataToNode(string objectType, string typedMetadataJson)
        {
            var serializer = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 64
            };

            Type proxyType = ResolveProxyType(objectType);
            if (proxyType != null)
            {
                object proxyObj = serializer.Deserialize(typedMetadataJson, proxyType);
                return ExtractMetadataGraph(proxyObj, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
            }

            return serializer.DeserializeObject(typedMetadataJson);
        }

        private static void ApplyObjectNode(object target, Dictionary<string, object> node, HashSet<string> skipKeys = null)
        {
            if (target == null || node == null) return;

            foreach (var kv in node)
            {
                if (skipKeys != null && skipKeys.Contains(kv.Key)) continue;

                var prop = FindProperty(target.GetType(), kv.Key);
                if (prop == null || !prop.CanRead) continue;

                Type propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                // Handle collections (including read-only collection properties).
                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(propType) && propType != typeof(string))
                {
                    object collection;
                    try { collection = prop.GetValue(target); }
                    catch { continue; }

                    if (collection == null)
                    {
                        if (!prop.CanWrite) continue;
                        object created = CreateDefaultInstance(propType);
                        if (created == null) continue;
                        prop.SetValue(target, created);
                        collection = created;
                    }

                    ClearCollectionIfPossible(collection);
                    PopulateCollectionFromNode(collection, kv.Value);
                    continue;
                }

                if (!prop.CanWrite) continue;

                if (kv.Value == null)
                {
                    if (Nullable.GetUnderlyingType(prop.PropertyType) != null || !prop.PropertyType.IsValueType)
                        prop.SetValue(target, null);
                    continue;
                }

                if (IsSimpleType(propType))
                {
                    object converted = ConvertSimpleValue(kv.Value, propType);
                    if (converted != null)
                        prop.SetValue(target, converted);
                    continue;
                }

                var childNode = kv.Value as Dictionary<string, object>;
                if (childNode == null) continue;

                object child = null;
                try { child = prop.GetValue(target); } catch { }

                // Support nested metadata on read-only properties when they expose an existing object.
                if (child == null)
                {
                    if (!prop.CanWrite) continue;
                    child = CreateDefaultInstance(propType);
                    if (child == null) continue;
                }

                ApplyObjectNode(child, childNode);
                if (prop.CanWrite)
                    prop.SetValue(target, child);
            }
        }

        private static void PopulateCollectionFromNode(object collection, object node)
        {
            if (collection == null || node == null) return;
            var values = ToObjectArray(node);
            if (values == null) return;

            // Use overload-safe Add resolution — KeyedObjectCollection and similar types
            // expose both typed and object-typed Add overloads; prefer the typed one.
            System.Reflection.MethodInfo addMethod = null;
            foreach (var m in collection.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "Add") continue;
                var ps = m.GetParameters();
                if (ps.Length != 1 || m.IsGenericMethod) continue;
                if (ps[0].ParameterType != typeof(object)) { addMethod = m; break; }
                if (addMethod == null) addMethod = m;
            }
            if (addMethod == null) return;
            Type itemType = addMethod.GetParameters()[0].ParameterType;

            foreach (object entry in values)
            {
                if (entry == null) continue;
                object addValue = null;

                if (IsSimpleType(itemType))
                {
                    addValue = ConvertSimpleValue(entry, itemType);
                }
                else
                {
                    var childDict = entry as Dictionary<string, object>;
                    if (childDict == null && entry is System.Collections.Hashtable ht)
                    {
                        childDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (System.Collections.DictionaryEntry de in ht)
                            childDict[Convert.ToString(de.Key)] = de.Value;
                    }
                    if (childDict != null)
                    {
                        object item = CreateDefaultInstance(itemType);
                        if (item != null)
                        {
                            ApplyObjectNode(item, childDict);
                            addValue = item;
                        }
                    }
                }

                if (addValue != null)
                    addMethod.Invoke(collection, new[] { addValue });
            }
        }

        private static void ApplyQueryDataSourcesFromTypedMetadata(AxQuerySimple query, Dictionary<string, object> node)
        {
            if (query == null || node == null) return;

            object dsNode = GetNodeValue(node, "DataSources", "dataSources");
            var dsValues = ToObjectArray(dsNode);
            if (dsValues == null) return;

            var dsProp = query.GetType().GetProperty("DataSources", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var rootCollection = dsProp?.GetValue(query);
            if (rootCollection == null) return;

            ClearCollectionIfPossible(rootCollection);

            foreach (var entry in dsValues)
            {
                var dsDict = ToDictionary(entry);
                if (dsDict == null) continue;
                AddQueryDataSourceNode(rootCollection, dsDict);
            }
        }

        private static void AddQueryDataSourceNode(object collection, Dictionary<string, object> node)
        {
            if (collection == null || node == null) return;

            // KeyedObjectCollection has multiple Add overloads (typed + object).
            // Pick the most-specific single-param Add whose parameter is not System.Object.
            System.Reflection.MethodInfo addMethod = null;
            foreach (var m in collection.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "Add") continue;
                var ps = m.GetParameters();
                if (ps.Length != 1 || m.IsGenericMethod) continue;
                if (ps[0].ParameterType == typeof(object))
                {
                    // Keep as fallback only if nothing better found yet.
                    if (addMethod == null) addMethod = m;
                }
                else
                {
                    addMethod = m;
                    break;
                }
            }

            if (addMethod == null) return;

            Type itemType = addMethod.GetParameters()[0].ParameterType;
            object dataSource = CreateDefaultInstance(itemType);
            if (dataSource == null) return;

            ApplyDictionaryToObject(dataSource, node,
                "DataSources", "dataSources", "ChildDataSources", "childDataSources", "Ranges", "ranges");
            ApplyQueryRangesFromNode(dataSource, node);
            ApplyChildDataSourcesFromNode(dataSource, node);

            addMethod.Invoke(collection, new[] { dataSource });
        }

        private static void ApplyChildDataSourcesFromNode(object dataSource, Dictionary<string, object> node)
        {
            object childrenNode = GetNodeValue(node,
                "DataSources", "dataSources", "ChildDataSources", "childDataSources");
            var children = ToObjectArray(childrenNode);
            if (children == null || children.Length == 0) return;

            var childrenProp = FindProperty(dataSource.GetType(), "DataSources")
                ?? FindProperty(dataSource.GetType(), "ChildDataSources");
            var childCollection = childrenProp?.GetValue(dataSource);
            if (childCollection == null) return;

            ClearCollectionIfPossible(childCollection);
            foreach (var child in children)
            {
                var childDict = ToDictionary(child);
                if (childDict == null) continue;
                AddQueryDataSourceNode(childCollection, childDict);
            }
        }

        private static void ApplyQueryRangesFromNode(object dataSource, Dictionary<string, object> node)
        {
            object rangesNode = GetNodeValue(node, "Ranges", "ranges");
            var ranges = ToObjectArray(rangesNode);
            if (ranges == null || ranges.Length == 0) return;

            var rangesProp = FindProperty(dataSource.GetType(), "Ranges");
            var rangeCollection = rangesProp?.GetValue(dataSource);
            if (rangeCollection == null) return;

            System.Reflection.MethodInfo addMethod = null;
            foreach (var m in rangeCollection.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "Add") continue;
                var ps = m.GetParameters();
                if (ps.Length != 1 || m.IsGenericMethod) continue;
                if (ps[0].ParameterType != typeof(object)) { addMethod = m; break; }
                if (addMethod == null) addMethod = m;
            }
            if (addMethod == null) return;
            Type rangeType = addMethod.GetParameters()[0].ParameterType;

            ClearCollectionIfPossible(rangeCollection);
            foreach (var rangeNode in ranges)
            {
                var rangeDict = ToDictionary(rangeNode);
                if (rangeDict == null) continue;

                object range = CreateDefaultInstance(rangeType);
                if (range == null) continue;

                ApplyDictionaryToObject(range, rangeDict);
                addMethod.Invoke(rangeCollection, new[] { range });
            }
        }

        private static void ApplyDictionaryToObject(object target, Dictionary<string, object> source, params string[] excludedKeys)
        {
            if (target == null || source == null) return;

            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (excludedKeys != null)
            {
                foreach (var k in excludedKeys)
                    if (!string.IsNullOrWhiteSpace(k)) excluded.Add(k);
            }

            foreach (var kv in source)
            {
                if (excluded.Contains(kv.Key)) continue;

                var prop = FindProperty(target.GetType(), kv.Key);
                if (prop == null || !prop.CanWrite) continue;

                object converted = ConvertValueForProperty(kv.Value, prop.PropertyType);
                if (converted != null)
                    prop.SetValue(target, converted);
            }
        }

        private static object ConvertValueForProperty(object input, Type propertyType)
        {
            if (input == null) return null;

            Type targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            object simple = ConvertSimpleValue(input, targetType);
            if (simple != null) return simple;

            if (targetType == typeof(string))
                return Convert.ToString(input);

            if (input is string text)
            {
                try
                {
                    var parse = targetType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static,
                        null, new[] { typeof(string) }, null);
                    if (parse != null)
                        return parse.Invoke(null, new object[] { text });
                }
                catch { }

                try
                {
                    var ctor = targetType.GetConstructor(new[] { typeof(string) });
                    if (ctor != null)
                        return ctor.Invoke(new object[] { text });
                }
                catch { }

                try
                {
                    object obj = CreateDefaultInstance(targetType);
                    var nameProp = FindProperty(targetType, "Name");
                    if (obj != null && nameProp != null && nameProp.CanWrite)
                    {
                        nameProp.SetValue(obj, text);
                        return obj;
                    }
                }
                catch { }
            }

            return null;
        }

        private static object GetNodeValue(Dictionary<string, object> dict, params string[] keys)
        {
            if (dict == null || keys == null) return null;

            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (dict.TryGetValue(key, out object value)) return value;
            }

            return null;
        }

        private static Dictionary<string, object> ToDictionary(object value)
        {
            if (value is Dictionary<string, object> d) return d;
            if (value is System.Collections.Hashtable ht)
            {
                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (System.Collections.DictionaryEntry de in ht)
                    dict[Convert.ToString(de.Key)] = de.Value;
                return dict;
            }
            return null;
        }

        private static void ClearCollectionIfPossible(object collection)
        {
            var clearMethod = collection?.GetType().GetMethod("Clear");
            if (clearMethod == null) return;
            try { clearMethod.Invoke(collection, null); } catch { }
        }

        private static object CreateDefaultInstance(Type type)
        {
            try
            {
                if (type.IsInterface || type.IsAbstract) return null;
                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }

        private static PropertyInfo FindProperty(Type type, string name)
        {
            return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        }

        private static bool IsSimpleType(Type t)
        {
            Type nt = Nullable.GetUnderlyingType(t) ?? t;
            return nt.IsPrimitive || nt.IsEnum || nt == typeof(string) || nt == typeof(decimal)
                || nt == typeof(DateTime) || nt == typeof(Guid);
        }

        private static object ConvertSimpleValue(object input, Type targetType)
        {
            try
            {
                if (input == null) return null;
                Type nt = Nullable.GetUnderlyingType(targetType) ?? targetType;

                if (nt == typeof(string)) return Convert.ToString(input);
                if (nt.IsEnum)
                {
                    if (input is string es)
                        return Enum.Parse(nt, es, true);
                    return Enum.ToObject(nt, Convert.ToInt32(input));
                }
                if (nt == typeof(bool))
                {
                    if (input is string bs)
                    {
                        if ("1".Equals(bs) || "yes".Equals(bs, StringComparison.OrdinalIgnoreCase)) return true;
                        if ("0".Equals(bs) || "no".Equals(bs, StringComparison.OrdinalIgnoreCase)) return false;
                    }
                    return Convert.ToBoolean(input);
                }
                if (nt == typeof(Guid))
                    return Guid.Parse(Convert.ToString(input));
                if (nt == typeof(DateTime))
                    return DateTime.Parse(Convert.ToString(input));

                return Convert.ChangeType(input, nt, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyQueryDataSources(AxQuerySimple query, List<QueryDataSourceDto> dataSources)
        {
            if (query == null || dataSources == null || dataSources.Count == 0)
                return;

            var dsProp = query.GetType().GetProperty("DataSources");
            var dsCollection = dsProp?.GetValue(query);
            if (dsCollection == null) return;

            ClearCollectionIfPossible(dsCollection);

            // Preferred shape: nested DTO tree using ChildDataSources.
            bool hasNested = dataSources.Any(ds => ds?.ChildDataSources != null && ds.ChildDataSources.Count > 0);
            if (hasNested)
            {
                foreach (var dto in dataSources)
                    AddQueryDataSourceFromDto(dsCollection, dto);
                return;
            }

            // Backward-compat shape: flattened list using ParentDataSource references.
            var byParent = new Dictionary<string, List<QueryDataSourceDto>>(StringComparer.OrdinalIgnoreCase);
            foreach (var dto in dataSources.Where(d => d != null))
            {
                string parent = string.IsNullOrWhiteSpace(dto.ParentDataSource) ? string.Empty : dto.ParentDataSource;
                if (!byParent.TryGetValue(parent, out var list))
                {
                    list = new List<QueryDataSourceDto>();
                    byParent[parent] = list;
                }
                list.Add(dto);
            }

            if (byParent.TryGetValue(string.Empty, out var roots))
            {
                foreach (var root in roots)
                    AddQueryDataSourceFromFlat(dsCollection, root, byParent);
            }
            else
            {
                // If parent links are absent, treat all as roots.
                foreach (var dto in dataSources)
                    AddQueryDataSourceFromDto(dsCollection, dto);
            }
        }

        private static void AddQueryDataSourceFromFlat(object collection, QueryDataSourceDto dto,
            Dictionary<string, List<QueryDataSourceDto>> byParent)
        {
            object created = AddQueryDataSourceFromDto(collection, dto);
            if (created == null || string.IsNullOrWhiteSpace(dto?.Name)) return;

            if (!byParent.TryGetValue(dto.Name, out var children) || children == null || children.Count == 0)
                return;

            var childCollection = FindProperty(created.GetType(), "DataSources")?.GetValue(created)
                ?? FindProperty(created.GetType(), "ChildDataSources")?.GetValue(created);
            if (childCollection == null) return;

            foreach (var child in children)
                AddQueryDataSourceFromFlat(childCollection, child, byParent);
        }

        private static object AddQueryDataSourceFromDto(object collection, QueryDataSourceDto dto)
        {
            if (collection == null || dto == null) return null;
            if (string.IsNullOrWhiteSpace(dto.Name) && string.IsNullOrWhiteSpace(dto.Table)) return null;

            var addMethod = collection.GetType().GetMethod("Add");
            if (addMethod == null || addMethod.GetParameters().Length != 1) return null;
            Type dsType = addMethod.GetParameters()[0].ParameterType;

            object ds = CreateDefaultInstance(dsType);
            if (ds == null) return null;

            TrySetPropertyValue(ds, dto.Name, "Name", "DataSourceName");
            TrySetPropertyValue(ds, dto.Table, "Table", "TableName");
            TrySetPropertyValue(ds, dto.ParentDataSource, "ParentDataSource", "JoinDataSource", "Parent");
            TrySetPropertyValue(ds, dto.JoinMode, "JoinMode");
            TrySetPropertyValue(ds, dto.LinkType, "LinkType");
            if (dto.DynamicFields.HasValue)
                TrySetPropertyValue(ds, dto.DynamicFields.Value ? "true" : "false", "DynamicFields");
            if (dto.Relations.HasValue)
                TrySetPropertyValue(ds, dto.Relations.Value ? "true" : "false", "Relations");
            if (dto.FirstOnly.HasValue)
                TrySetPropertyValue(ds, dto.FirstOnly.Value ? "true" : "false", "FirstOnly");

            ApplyQueryRanges(ds, dto.Ranges);

            addMethod.Invoke(collection, new object[] { ds });

            if (dto.ChildDataSources != null && dto.ChildDataSources.Count > 0)
            {
                var childCollection = FindProperty(ds.GetType(), "DataSources")?.GetValue(ds)
                    ?? FindProperty(ds.GetType(), "ChildDataSources")?.GetValue(ds);
                if (childCollection != null)
                {
                    ClearCollectionIfPossible(childCollection);
                    foreach (var child in dto.ChildDataSources)
                        AddQueryDataSourceFromDto(childCollection, child);
                }
            }

            return ds;
        }

        private static object FindQueryDataSourceByName(object dsCollection, string name)
        {
            if (dsCollection == null || string.IsNullOrWhiteSpace(name)) return null;
            var enumerable = dsCollection as System.Collections.IEnumerable;
            if (enumerable == null) return null;

            foreach (object item in enumerable)
            {
                var nameProp = item?.GetType().GetProperty("Name")
                    ?? item?.GetType().GetProperty("DataSourceName");
                string existingName = nameProp?.GetValue(item) as string;
                if (!string.IsNullOrWhiteSpace(existingName)
                    && existingName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return item;
            }

            return null;
        }

        private static void ApplyQueryRanges(object dataSource, List<QueryRangeDto> ranges)
        {
            if (dataSource == null || ranges == null || ranges.Count == 0) return;

            var rangesProp = dataSource.GetType().GetProperty("Ranges");
            var rangesCollection = rangesProp?.GetValue(dataSource);
            if (rangesCollection == null) return;

            var addMethod = rangesCollection.GetType().GetMethod("Add");
            if (addMethod == null || addMethod.GetParameters().Length != 1) return;
            Type rangeType = addMethod.GetParameters()[0].ParameterType;

            foreach (var dto in ranges)
            {
                if (string.IsNullOrWhiteSpace(dto?.Field) && string.IsNullOrWhiteSpace(dto?.Name))
                    continue;

                object range = Activator.CreateInstance(rangeType);
                TrySetPropertyValue(range, dto.Name, "Name");
                TrySetPropertyValue(range, dto.Field, "Field", "DataField", "Column");
                TrySetPropertyValue(range, dto.Value, "Value", "Range", "Expression");
                addMethod.Invoke(rangesCollection, new object[] { range });
            }
        }

        private static void TrySetPropertyValue(object target, string value, params string[] propertyNames)
        {
            if (target == null || string.IsNullOrWhiteSpace(value) || propertyNames == null) return;

            foreach (string propertyName in propertyNames)
            {
                if (string.IsNullOrWhiteSpace(propertyName)) continue;
                var prop = target.GetType().GetProperty(propertyName);
                if (prop == null || !prop.CanWrite) continue;

                try
                {
                    Type propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    object converted;

                    if (propType == typeof(string))
                        converted = value;
                    else if (propType == typeof(bool))
                        converted = value.Equals("true", StringComparison.OrdinalIgnoreCase)
                            || value.Equals("1", StringComparison.OrdinalIgnoreCase)
                            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
                    else if (propType == typeof(int))
                        converted = int.Parse(value);
                    else if (propType == typeof(long))
                        converted = long.Parse(value);
                    else if (propType.IsEnum)
                        converted = Enum.Parse(propType, value, true);
                    else
                        continue;

                    prop.SetValue(target, converted);
                    return;
                }
                catch
                {
                    // Try next matching property name.
                }
            }
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

        private static List<QueryDataSourceDto> ExtractQueryDataSources(object query)
        {
            var result = new List<QueryDataSourceDto>();
            if (query == null) return result;

            var dsProp = query.GetType().GetProperty("DataSources");
            var dsCollection = dsProp?.GetValue(query) as System.Collections.IEnumerable;
            if (dsCollection == null) return result;

            foreach (object ds in dsCollection)
            {
                if (ds == null) continue;

                var dto = new QueryDataSourceDto
                {
                    Name = ReadStringProperty(ds, "Name", "DataSourceName"),
                    Table = ReadStringProperty(ds, "Table", "TableName"),
                    ParentDataSource = ReadStringProperty(ds, "ParentDataSource", "JoinDataSource", "Parent"),
                    JoinMode = ReadStringProperty(ds, "JoinMode"),
                    LinkType = ReadStringProperty(ds, "LinkType")
                };

                dto.DynamicFields = ReadBoolProperty(ds, "DynamicFields");
                dto.Relations = ReadBoolProperty(ds, "Relations");
                dto.FirstOnly = ReadBoolProperty(ds, "FirstOnly");

                var rangesProp = ds.GetType().GetProperty("Ranges");
                var ranges = rangesProp?.GetValue(ds) as System.Collections.IEnumerable;
                if (ranges != null)
                {
                    foreach (object r in ranges)
                    {
                        if (r == null) continue;
                        dto.Ranges.Add(new QueryRangeDto
                        {
                            Name = ReadStringProperty(r, "Name"),
                            Field = ReadStringProperty(r, "Field", "DataField", "Column"),
                            Value = ReadStringProperty(r, "Value", "Range", "Expression")
                        });
                    }
                }

                result.Add(dto);
            }

            return result;
        }

        private static string ReadStringProperty(object target, params string[] names)
        {
            if (target == null || names == null) return null;
            foreach (string name in names)
            {
                var prop = target.GetType().GetProperty(name);
                if (prop == null || !prop.CanRead) continue;
                try
                {
                    var val = prop.GetValue(target);
                    if (val != null) return val.ToString();
                }
                catch { }
            }
            return null;
        }

        private static bool? ReadBoolProperty(object target, params string[] names)
        {
            if (target == null || names == null) return null;
            foreach (string name in names)
            {
                var prop = target.GetType().GetProperty(name);
                if (prop == null || !prop.CanRead) continue;
                try
                {
                    object val = prop.GetValue(target);
                    if (val == null) continue;

                    Type valType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    if (valType == typeof(bool))
                        return (bool)val;

                    string s = val.ToString();
                    if ("true".Equals(s, StringComparison.OrdinalIgnoreCase)
                        || "yes".Equals(s, StringComparison.OrdinalIgnoreCase)
                        || "1".Equals(s, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if ("false".Equals(s, StringComparison.OrdinalIgnoreCase)
                        || "no".Equals(s, StringComparison.OrdinalIgnoreCase)
                        || "0".Equals(s, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                catch { }
            }
            return null;
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

        // ── Cross-Reference search ──

        public FindReferencesResult FindReferences(string objectType, string objectName, string referenceKind, int maxResults)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new FindReferencesResult { Success = true };

            try
            {
                var xrefProvider = CoreUtility.ServiceProvider
                    .GetService(typeof(ICrossReferenceProvider)) as ICrossReferenceProvider;

                if (xrefProvider == null)
                {
                    result.Message = "Cross-reference service not available. " +
                        "The XRef database may not have been built yet — run a full build in VS to populate it.";
                    return result;
                }

                string targetPath = BuildXRefPath(objectType, objectName);
                if (targetPath == null)
                {
                    result.Message = "Unsupported object type for cross-reference search: '" + objectType + "'. " +
                        "Supported: AxClass, AxTable, AxForm, AxEnum, AxView, AxQuery, AxEdt, AxDataEntityView, AxMap.";
                    return result;
                }

                result.TargetPath = targetPath;

                CrossReferenceKind kind = ParseCrossReferenceKind(referenceKind);
                var refs = xrefProvider.FindReferences(null, targetPath, kind);

                foreach (var r in refs)
                {
                    if (result.References.Count >= maxResults) break;
                    result.References.Add(CrossReferenceToItem(r));
                }

                if (result.References.Count == 0)
                    result.Message = "No references found to '" + objectName + "' (" + objectType + ").";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "Cross-reference search error: " + ex.Message;
            }

            return result;
        }

        private static string BuildXRefPath(string objectType, string objectName)
        {
            string folder = ObjectTypeToXRefFolder(objectType);
            return folder == null ? null : "/" + folder + "/" + objectName;
        }

        private static string ObjectTypeToXRefFolder(string objectType)
        {
            switch (objectType)
            {
                case "AxClass": return "Classes";
                case "AxTable": return "Tables";
                case "AxForm": return "Forms";
                case "AxEnum": return "BaseEnums";
                case "AxView": return "Views";
                case "AxQuery": return "Queries";
                case "AxEdt": return "ExtendedDataTypes";
                case "AxMenuItemDisplay": return "MenuItemDisplays";
                case "AxMenuItemOutput": return "MenuItemOutputs";
                case "AxMenuItemAction": return "MenuItemActions";
                case "AxDataEntityView": return "DataEntityViews";
                case "AxMap": return "Maps";
                case "AxService": return "Services";
                case "AxServiceGroup": return "ServiceGroups";
                case "AxSecurityRole": return "SecurityRoles";
                case "AxSecurityPrivilege": return "SecurityPrivileges";
                case "AxSecurityDuty": return "SecurityDuties";
                default: return null;
            }
        }

        private static string XRefFolderToObjectType(string folder)
        {
            switch (folder)
            {
                case "Classes": return "AxClass";
                case "Tables": return "Tables";
                case "Forms": return "AxForm";
                case "BaseEnums": return "AxEnum";
                case "Views": return "AxView";
                case "Queries": return "AxQuery";
                case "ExtendedDataTypes": return "AxEdt";
                case "MenuItemDisplays": return "AxMenuItemDisplay";
                case "MenuItemOutputs": return "AxMenuItemOutput";
                case "MenuItemActions": return "AxMenuItemAction";
                case "DataEntityViews": return "AxDataEntityView";
                case "Maps": return "AxMap";
                default: return folder;
            }
        }

        private static CrossReferenceKind ParseCrossReferenceKind(string kind)
        {
            if (string.IsNullOrEmpty(kind) || kind.Equals("Any", StringComparison.OrdinalIgnoreCase))
                return CrossReferenceKind.Any;
            CrossReferenceKind parsed;
            if (Enum.TryParse(kind, ignoreCase: true, result: out parsed))
                return parsed;
            return CrossReferenceKind.Any;
        }

        private static CrossReferenceItem CrossReferenceToItem(CrossReference r)
        {
            // Parse path like "/Classes/HcmWorker/Methods/someMethod" → [Classes, HcmWorker, Methods, someMethod]
            string sourcePath = r.SourcePath ?? "";
            var parts = sourcePath.TrimStart('/').Split('/');
            string objType = parts.Length > 0 ? XRefFolderToObjectType(parts[0]) : null;
            string objName = parts.Length > 1 ? parts[1] : null;
            // parts[2] is usually "Methods" (member kind), parts[3] is member name
            string member = parts.Length >= 4 ? parts[3] : null;

            return new CrossReferenceItem
            {
                SourcePath = r.SourcePath,
                SourceObjectType = objType,
                SourceObjectName = objName,
                SourceMember = member,
                Kind = r.Kind.ToString(),
                LineNumber = r.LineNumber,
                SourceModule = r.SourceModule
            };
        }
    }
}
