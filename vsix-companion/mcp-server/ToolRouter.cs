using System;
using System.Text;

namespace XppAiCopilotCompanion.McpServer
{
    /// <summary>
    /// Routes MCP tool calls to the MetaModel bridge running inside the VSIX.
    /// All metadata operations go through the strongly-typed MetaModel API.
    /// </summary>
    internal sealed class ToolRouter
    {
        private readonly BridgeClient _bridge;
        private readonly DocSearchHandler _docSearch;

        public ToolRouter(BridgeClient bridge)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _docSearch = new DocSearchHandler();
        }

        public string BuildToolsListResponse(string idToken)
        {
            string result = @"{
  ""tools"": [
    {
      ""name"": ""xpp_create_object"",
      ""description"": ""Creates a new D365FO X++ metadata object using the MetaModel API. CRITICAL: This is the ONLY correct way to create X++ objects. NEVER create metadata files directly via terminal, file writes, or Set-Content — that bypasses the MetaModel API and produces corrupted/unregistered objects. ALL parameters MUST be JSON — any XML or CDATA content will be REJECTED with an error. Use 'properties' for scalar metadata (Label, IsExtensible, TableGroup, etc.), 'enumValues' for AxEnum values, 'fields'/'indexes'/'fieldGroups'/'relations' for AxTable structure, 'entryPoints' for security types. Use xpp_read_object on existing objects to see the exact JSON format. The object is automatically added to the active VS project."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""objectType"": {
            ""type"": ""string"",
            ""description"": ""The D365FO object type to create."",
            ""enum"": [""AxClass"", ""AxTable"", ""AxView"", ""AxDataEntityView"", ""AxMap"", ""AxEdt"", ""AxEnum"", ""AxForm"", ""AxTile"", ""AxMenu"", ""AxMenuItemDisplay"", ""AxMenuItemOutput"", ""AxMenuItemAction"", ""AxQuery"", ""AxSecurityPrivilege"", ""AxSecurityDuty"", ""AxSecurityRole"", ""AxService"", ""AxServiceGroup"", ""AxConfigurationKey"", ""AxTableExtension"", ""AxFormExtension"", ""AxEnumExtension"", ""AxEdtExtension"", ""AxViewExtension"", ""AxMenuExtension"", ""AxMenuItemDisplayExtension"", ""AxMenuItemOutputExtension"", ""AxMenuItemActionExtension"", ""AxQuerySimpleExtension"", ""AxSecurityDutyExtension"", ""AxSecurityRoleExtension""]
          },
          ""objectName"": { ""type"": ""string"", ""description"": ""Name of the object. Must follow the model's naming prefix convention."" },
          ""declaration"": { ""type"": ""string"", ""description"": ""Raw X++ class/table declaration code block. Plain code only — NO XML, NO CDATA wrappers."" },
          ""methods"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Array of complete X++ method source strings. Plain code only — NO XML, NO CDATA wrappers. Any XML content will be rejected with an error."" },
          ""properties"": { ""type"": ""object"", ""description"": ""Key-value pairs for scalar metadata properties set via reflection. Keys are property names (e.g. Label, IsExtensible, TableGroup, ObjectType, Object, FormRef). Values are strings — type coercion is automatic (bool, int, enum). Use xpp_read_object on an existing object to discover available property names."" },
          ""enumValues"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""value"": { ""type"": ""integer"" }, ""label"": { ""type"": ""string"" } }, ""required"": [""name"", ""value""] }, ""description"": ""Enum values for AxEnum or AxEnumExtension. Each entry creates an AxEnumValue with Name, Value (integer), and optional Label."" },
          ""fields"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""fieldType"": { ""type"": ""string"", ""enum"": [""String"", ""Int"", ""Real"", ""Date"", ""DateTime"", ""Enum"", ""Int64"", ""Container"", ""Guid"", ""Time""] }, ""extendedDataType"": { ""type"": ""string"" }, ""enumType"": { ""type"": ""string"" }, ""label"": { ""type"": ""string"" } }, ""required"": [""name"", ""fieldType""] }, ""description"": ""Table fields for AxTable or AxTableExtension. fieldType determines the field class (AxTableFieldString, AxTableFieldInt, etc.)."" },
          ""indexes"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""allowDuplicates"": { ""type"": ""boolean"" }, ""fields"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } } }, ""required"": [""name"", ""fields""] }, ""description"": ""Table indexes for AxTable. Each index has a name and array of field names."" },
          ""fieldGroups"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""label"": { ""type"": ""string"" }, ""fields"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } } }, ""required"": [""name"", ""fields""] }, ""description"": ""Table field groups for AxTable. Each group has a name and array of field names."" },
          ""relations"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""relatedTable"": { ""type"": ""string"" }, ""constraints"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""field"": { ""type"": ""string"" }, ""relatedField"": { ""type"": ""string"" } } } } }, ""required"": [""name"", ""relatedTable""] }, ""description"": ""Table relations for AxTable. Each relation maps fields to related table fields."" },
          ""entryPoints"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""objectType"": { ""type"": ""string"" }, ""objectName"": { ""type"": ""string"" }, ""grant"": { ""type"": ""string"" } }, ""required"": [""name""] }, ""description"": ""Security entry points for AxSecurityPrivilege/Duty/Role."" },
          ""modelName"": { ""type"": ""string"", ""description"": ""Target model name. If omitted, uses the active project's model."" }
        },
        ""required"": [""objectType"", ""objectName""]
      }
    },
    {
      ""name"": ""xpp_read_object"",
      ""description"": ""Reads a D365FO X++ object by type and name using the MetaModel API. CRITICAL: This is the ONLY correct way to read X++ objects. NEVER read metadata files directly via Get-Content or terminal commands. Returns declaration, methods, and strongly-typed metadata (properties, enumValues, fields, indexes, fieldGroups, relations) in JSON format — use this output directly as a template for creating or modifying objects. Also returns model name and editability. For objects not supported by the typed API, pass filePath instead."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""objectType"": { ""type"": ""string"", ""description"": ""The object type (AxClass, AxTable, AxForm, AxEnum, AxEdt, AxView, AxQuery)."" },
          ""objectName"": { ""type"": ""string"", ""description"": ""The object name."" },
          ""filePath"": { ""type"": ""string"", ""description"": ""Absolute path to the metadata file. Use this for types not supported by the typed API, or when you have the path."" }
        },
        ""required"": []
      }
    },
    {
      ""name"": ""xpp_update_object"",
      ""description"": ""Updates an existing D365FO X++ object using the MetaModel API. CRITICAL: This is the ONLY correct way to modify X++ objects. NEVER edit metadata files directly. ALL parameters MUST be JSON — any XML or CDATA content will be REJECTED with an error. Can update declaration, add/replace/remove methods, set properties, and add enum values/fields/indexes/relations. Use xpp_read_object first to see current state. ONLY works on custom model objects."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""objectType"": { ""type"": ""string"", ""description"": ""The object type (AxClass, AxTable, AxForm, AxEdt, AxEnum)."" },
          ""objectName"": { ""type"": ""string"", ""description"": ""The object name to update."" },
          ""declaration"": { ""type"": ""string"", ""description"": ""New raw X++ declaration code. Replaces existing. Plain code only — NO XML, NO CDATA."" },
          ""methods"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Methods to add or replace (matched by name). Plain X++ code only — NO XML, NO CDATA. Any XML content will be rejected."" },
          ""removeMethodNames"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Method names to remove."" },
          ""properties"": { ""type"": ""object"", ""description"": ""Key-value pairs for scalar metadata properties to set or update. Same format as xpp_create_object."" },
          ""enumValues"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""value"": { ""type"": ""integer"" }, ""label"": { ""type"": ""string"" } }, ""required"": [""name"", ""value""] }, ""description"": ""Enum values to add (for AxEnum). Values with matching names are skipped."" },
          ""fields"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""fieldType"": { ""type"": ""string"" }, ""extendedDataType"": { ""type"": ""string"" }, ""enumType"": { ""type"": ""string"" }, ""label"": { ""type"": ""string"" } }, ""required"": [""name"", ""fieldType""] }, ""description"": ""Table fields to add (for AxTable)."" },
          ""indexes"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""allowDuplicates"": { ""type"": ""boolean"" }, ""fields"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } } }, ""required"": [""name"", ""fields""] }, ""description"": ""Indexes to add (for AxTable)."" },
          ""fieldGroups"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""label"": { ""type"": ""string"" }, ""fields"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } } }, ""required"": [""name"", ""fields""] }, ""description"": ""Field groups to add (for AxTable)."" },
          ""relations"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""relatedTable"": { ""type"": ""string"" }, ""constraints"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""field"": { ""type"": ""string"" }, ""relatedField"": { ""type"": ""string"" } } } } }, ""required"": [""name"", ""relatedTable""] }, ""description"": ""Relations to add (for AxTable)."" },
          ""entryPoints"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""objectType"": { ""type"": ""string"" }, ""objectName"": { ""type"": ""string"" }, ""grant"": { ""type"": ""string"" } }, ""required"": [""name""] }, ""description"": ""Entry points to add (for security types)."" }
        },
        ""required"": [""objectType"", ""objectName""]
      }
    },
    {
      ""name"": ""xpp_delete_object"",
      ""description"": ""Deletes a D365FO X++ object using the MetaModel API. ONLY works on custom model objects."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""objectType"": { ""type"": ""string"", ""description"": ""The object type."" },
          ""objectName"": { ""type"": ""string"", ""description"": ""The object name."" },
          ""modelName"": { ""type"": ""string"", ""description"": ""The model containing the object. If omitted, uses active project's model."" }
        },
        ""required"": [""objectType"", ""objectName""]
      }
    },
    {
      ""name"": ""xpp_find_object"",
      ""description"": ""Searches for D365FO metadata objects by name across ALL loaded models and packages using the MetaModel API. CRITICAL: Always use this instead of Get-ChildItem/dir/file searches on metadata folders. NEVER search the file system for X++ objects. Returns matching objects with type, model, and editability."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""objectName"": { ""type"": ""string"", ""description"": ""Full or partial name to search for (case-insensitive)."" },
          ""objectType"": { ""type"": ""string"", ""description"": ""Limit search to a specific type (AxClass, AxTable, etc.)."" },
          ""exactMatch"": { ""type"": ""boolean"", ""description"": ""If true, only exact name matches. Default false."" }
        },
        ""required"": [""objectName""]
      }
    },
    {
      ""name"": ""xpp_list_objects"",
      ""description"": ""Lists D365FO metadata objects filtered by name pattern. CRITICAL: Always use this instead of Get-ChildItem/dir/file searches on metadata folders. NEVER search the file system for X++ objects. Always provide nameFilter (substring match) and objectType to avoid slow full scans. Returns matching object names, types, and models."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""nameFilter"": { ""type"": ""string"", ""description"": ""Required. Substring to match object names (case-insensitive). Example: 'CustTable', 'SalesOrder'."" },
          ""objectType"": { ""type"": ""string"", ""description"": ""Strongly recommended. Filter by type: AxClass, AxTable, AxForm, AxEnum, AxView, AxQuery, AxEdt, AxMenuItemDisplay, AxMenuItemOutput, AxMenuItemAction."" },
          ""modelName"": { ""type"": ""string"", ""description"": ""Filter by model name."" },
          ""maxResults"": { ""type"": ""integer"", ""description"": ""Max results. Default 100."" }
        },
        ""required"": [""nameFilter""]
      }
    },
    {
      ""name"": ""xpp_get_model_info"",
      ""description"": ""Returns information about a D365FO model: name, publisher, version, layer, dependencies."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""modelName"": { ""type"": ""string"", ""description"": ""The model name to inspect."" }
        },
        ""required"": [""modelName""]
      }
    },
    {
      ""name"": ""xpp_list_models"",
      ""description"": ""Lists all D365FO models in the current environment with their editability status."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {},
        ""required"": []
      }
    },
    {
      ""name"": ""xpp_read_label"",
      ""description"": ""Reads labels from a D365FO label file using the Label API. Look up specific label IDs or search by text."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""labelFileId"": { ""type"": ""string"", ""description"": ""The label file ID (e.g. 'MyLabels')."" },
          ""language"": { ""type"": ""string"", ""description"": ""Language code (e.g. 'en-US'). Default en-US."" },
          ""labelId"": { ""type"": ""string"", ""description"": ""Specific label ID to look up."" },
          ""searchText"": { ""type"": ""string"", ""description"": ""Search labels containing this text."" },
          ""maxResults"": { ""type"": ""integer"", ""description"": ""Max results. Default 50."" }
        },
        ""required"": [""labelFileId""]
      }
    },
    {
      ""name"": ""xpp_create_label"",
      ""description"": ""Creates a new label in a D365FO label file using the Label API."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""labelFileId"": { ""type"": ""string"", ""description"": ""The label file ID."" },
          ""language"": { ""type"": ""string"", ""description"": ""Language code. Default en-US."" },
          ""labelId"": { ""type"": ""string"", ""description"": ""The label ID to create."" },
          ""text"": { ""type"": ""string"", ""description"": ""The label text."" },
          ""comment"": { ""type"": ""string"", ""description"": ""Optional comment."" }
        },
        ""required"": [""labelFileId"", ""labelId"", ""text""]
      }
    },
    {
      ""name"": ""xpp_add_to_project"",
      ""description"": ""Adds a metadata object to the active Visual Studio D365FO project."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""objectType"": { ""type"": ""string"", ""description"": ""The object type (AxClass, AxTable, etc.)."" },
          ""objectName"": { ""type"": ""string"", ""description"": ""The object name."" }
        },
        ""required"": [""objectType"", ""objectName""]
      }
    },
    {
      ""name"": ""xpp_list_project_items"",
      ""description"": ""Lists all items in the active Visual Studio D365FO project."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {},
        ""required"": []
      }
    },
    {
      ""name"": ""xpp_get_environment"",
      ""description"": ""Returns the D365FO development environment info: metadata folders, active project, active model name/ID/layer. Use this to understand the project context. CRITICAL RULE: In D365FO/X++ projects, NEVER use terminal commands to create, read, update, search, or list metadata files. Always use the xpp_* tools — they use the MetaModel API which correctly registers objects, maintains cross-references, and respects model boundaries."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {},
        ""required"": []
      }
    },
    {
      ""name"": ""xpp_search_docs"",
      ""description"": ""Searches Microsoft Learn documentation for D365FO, X++ language, extensibility, and technical references. ALWAYS use this when unsure about X++ syntax, table structures, API signatures, or extensibility rules."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""url"": { ""type"": ""string"", ""description"": ""A specific Microsoft Learn URL to fetch."" },
          ""query"": { ""type"": ""string"", ""description"": ""Search query for Microsoft Learn D365FO docs."" },
          ""maxLength"": { ""type"": ""integer"", ""description"": ""Max character length of returned content. Default 12000."" }
        },
        ""required"": [""query""]
      }
    }
  ]
}";
            return JsonHelpers.BuildResult(idToken, result);
        }

        public string HandleToolCall(string idToken, string json)
        {
            string toolName = JsonHelpers.ExtractNestedString(json, "params", "name");
            McpLogger.Log("tools/call name=" + (toolName ?? "<null>"));

            switch (toolName)
            {
                // ── Bridge-delegated tools ──
                case "xpp_create_object": return DelegateToBridge(idToken, json, "create_object");
                case "xpp_read_object": return HandleReadObject(idToken, json);
                case "xpp_update_object":
                case "xpp_update_current_object": return DelegateToBridge(idToken, json, "update_object");
                case "xpp_delete_object": return DelegateToBridge(idToken, json, "delete_object");
                case "xpp_find_object": return DelegateToBridge(idToken, json, "find_object");
                case "xpp_list_objects": return DelegateToBridge(idToken, json, "list_objects");
                case "xpp_get_model_info": return DelegateToBridge(idToken, json, "get_model_info");
                case "xpp_list_models": return DelegateToBridge(idToken, json, "list_models");
                case "xpp_read_label": return DelegateToBridge(idToken, json, "read_label");
                case "xpp_create_label": return DelegateToBridge(idToken, json, "create_label");
                case "xpp_add_to_project": return DelegateToBridge(idToken, json, "add_to_project");
                case "xpp_list_project_items": return DelegateToBridge(idToken, json, "list_project_items");
                case "xpp_get_environment": return DelegateToBridge(idToken, json, "get_environment");

                // ── Local-only tools ──
                case "xpp_search_docs": return _docSearch.Handle(idToken, json);

                default:
                    return JsonHelpers.BuildError(idToken, -32602, "Unknown tool: " + toolName);
            }
        }

        private string DelegateToBridge(string idToken, string json, string action)
        {
            string argsJson = JsonHelpers.ExtractToolArgumentsObject(json);

            // Log full arguments for create/update to aid debugging
            if (action == "create_object" || action == "update_object")
                McpLogger.Log("bridge args (" + action + "): " + argsJson);

            if (!_bridge.IsAvailable())
            {
                return JsonHelpers.BuildToolResult(idToken,
                    "MetaModel bridge is not available. The D365FO tools extension may not be loaded, "
                    + "or the bridge server hasn't started yet. "
                    + "Ensure Visual Studio has the Microsoft Dynamics 365 Finance and Operations tools installed.",
                    isError: true);
            }

            string bridgeResponse = _bridge.Call(action, argsJson);

            // Parse bridge response and format as MCP tool result
            string success = JsonHelpers.ExtractJsonString(bridgeResponse, "success");
            string successToken = JsonHelpers.ExtractJsonValueToken(bridgeResponse, "success");
            string message = JsonHelpers.ExtractJsonString(bridgeResponse, "message");

            // Bridge returns success as JSON boolean (true/false), while older
            // paths may return string values. Support both to avoid marking
            // successful bridge calls as MCP errors.
            bool isSuccess = false;
            if (!string.IsNullOrWhiteSpace(success))
            {
              isSuccess = "true".Equals(success, StringComparison.OrdinalIgnoreCase);
            }
            else if (!string.IsNullOrWhiteSpace(successToken))
            {
              string token = successToken.Trim();
              if (token.StartsWith("\"") && token.EndsWith("\"") && token.Length >= 2)
                token = token.Substring(1, token.Length - 2);
              isSuccess = "true".Equals(token, StringComparison.OrdinalIgnoreCase);
            }
            bool isError = !isSuccess;

            // For rich results, pass through relevant data
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(message))
                sb.AppendLine(message);

            // Append matches/objects/items/labels if present
            AppendArrayField(sb, bridgeResponse, "matches", "objectType", "objectName", "modelName", "isCustom");
            AppendArrayField(sb, bridgeResponse, "objects", "objectType", "objectName", "modelName", "isCustom");
            AppendArrayField(sb, bridgeResponse, "models", "name", "displayName", "isCustom");
            AppendArrayField(sb, bridgeResponse, "labels", "id", "text");
            AppendArrayField(sb, bridgeResponse, "items", "name", "filePath");

            // Append read result fields
            string declaration = JsonHelpers.ExtractJsonString(bridgeResponse, "declaration");
            if (!string.IsNullOrEmpty(declaration))
            {
                sb.AppendLine("\n=== Declaration ===");
                sb.AppendLine(declaration);
            }

            // Append typed metadata (properties, enumValues, fields, indexes, etc.)
            AppendTypedMetadata(sb, bridgeResponse);

            string objectType = JsonHelpers.ExtractJsonString(bridgeResponse, "objectType");
            string objectName = JsonHelpers.ExtractJsonString(bridgeResponse, "objectName");
            string modelName = JsonHelpers.ExtractJsonString(bridgeResponse, "modelName");
            string isCustom = JsonHelpers.ExtractJsonString(bridgeResponse, "isCustom");

            if (!string.IsNullOrEmpty(objectType))
                sb.Insert(0, "ObjectType: " + objectType + "\nName: " + objectName
                    + "\nModel: " + modelName + "\nEditable: " + (isCustom == "true" ? "YES" : "NO") + "\n\n");

            // Environment info
            string customFolder = JsonHelpers.ExtractJsonString(bridgeResponse, "customMetadataFolder");
            if (!string.IsNullOrEmpty(customFolder))
            {
                sb.AppendLine("CustomMetadataFolder: " + customFolder);
                string projectName = JsonHelpers.ExtractJsonString(bridgeResponse, "activeProjectName");
                string activeModel = JsonHelpers.ExtractJsonString(bridgeResponse, "activeModelName");
                string layer = JsonHelpers.ExtractJsonString(bridgeResponse, "activeModelLayer");
                if (!string.IsNullOrEmpty(projectName)) sb.AppendLine("ActiveProject: " + projectName);
                if (!string.IsNullOrEmpty(activeModel)) sb.AppendLine("ActiveModel: " + activeModel);
                if (!string.IsNullOrEmpty(layer)) sb.AppendLine("Layer: " + layer);
            }

            string text = sb.ToString().Trim();
            if (string.IsNullOrEmpty(text)) text = isError ? "Operation failed." : "Operation completed.";

            return JsonHelpers.BuildToolResult(idToken, text, isError);
        }

        private string HandleReadObject(string idToken, string json)
        {
            string argsJson = JsonHelpers.ExtractToolArgumentsObject(json);
            string filePath = JsonHelpers.ExtractArgString(argsJson, "filePath", "path");
            string objectType = JsonHelpers.ExtractArgString(argsJson, "objectType", "type");
            string objectName = JsonHelpers.ExtractArgString(argsJson, "objectName", "name");

            // If filePath provided, use read_object_by_path
            if (!string.IsNullOrEmpty(filePath))
                return DelegateToBridge(idToken, json, "read_object_by_path");

            // Otherwise use typed API
            if (!string.IsNullOrEmpty(objectType) && !string.IsNullOrEmpty(objectName))
                return DelegateToBridge(idToken, json, "read_object");

            return JsonHelpers.BuildToolResult(idToken,
                "Either (objectType + objectName) or filePath is required.", isError: true);
        }

        private static void AppendArrayField(StringBuilder sb, string json, string arrayKey,
            params string[] fields)
        {
            // Simple heuristic: check if the array key exists and has content
            string marker = "\"" + arrayKey + "\":[";
            int idx = json.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return;

            // Find closing bracket
            int start = idx + marker.Length;
            if (start >= json.Length || json[start] == ']') return;

            // Extract items by parsing simple JSON objects
            int depth = 1;
            int pos = start;
            while (pos < json.Length && depth > 0)
            {
                if (json[pos] == '[') depth++;
                if (json[pos] == ']') depth--;
                pos++;
            }
            if (pos <= start) return;

            string arrayContent = json.Substring(start, pos - start - 1);

            // Parse each object in the array
            int objStart = arrayContent.IndexOf('{');
            int count = 0;
            while (objStart >= 0 && count < 200)
            {
                int objEnd = arrayContent.IndexOf('}', objStart);
                if (objEnd < 0) break;
                string obj = arrayContent.Substring(objStart, objEnd - objStart + 1);

                var line = new StringBuilder();
                foreach (string field in fields)
                {
                    string val = JsonHelpers.ExtractJsonString(obj, field);
                    if (val != null)
                    {
                        if (line.Length > 0) line.Append(" | ");
                        line.Append(field + "=" + val);
                    }
                }
                if (line.Length > 0)
                    sb.AppendLine("  " + line);

                objStart = arrayContent.IndexOf('{', objEnd);
                count++;
            }
        }

        /// <summary>
        /// Extracts typed metadata (properties, enumValues, fields, indexes, etc.)
        /// from the bridge JSON response and formats them for display.
        /// The output uses the same JSON parameter names that xpp_create_object and
        /// xpp_update_object accept, enabling round-trip usage.
        /// </summary>
        private static void AppendTypedMetadata(StringBuilder sb, string json)
        {
            // Properties (JSON object)
            string propsObj = JsonHelpers.ExtractNestedObject(json, "properties");
            if (!string.IsNullOrEmpty(propsObj) && propsObj != "{}")
            {
                sb.AppendLine("\n=== Properties (use in \"properties\" parameter) ===");
                sb.AppendLine(propsObj);
            }

            // Enum Values
            AppendJsonArray(sb, json, "enumValues", "Enum Values");
            // Fields
            AppendJsonArray(sb, json, "fields", "Fields");
            // Indexes
            AppendJsonArray(sb, json, "indexes", "Indexes");
            // Field Groups
            AppendJsonArray(sb, json, "fieldGroups", "Field Groups");
            // Relations
            AppendJsonArray(sb, json, "relations", "Relations");
            // Entry Points
            AppendJsonArray(sb, json, "entryPoints", "Entry Points");
        }

        private static void AppendJsonArray(StringBuilder sb, string json, string key, string label)
        {
            string marker = "\"" + key + "\":[";
            int idx = json.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return;

            // Find the full array including brackets
            int arrStart = idx + marker.Length - 1; // position of [
            int depth = 0; bool inStr = false; bool esc = false; int arrEnd = -1;
            for (int i = arrStart; i < json.Length; i++)
            {
                char c = json[i];
                if (esc) { esc = false; continue; }
                if (c == '\\') { esc = true; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '[') depth++;
                if (c == ']') { depth--; if (depth == 0) { arrEnd = i; break; } }
            }
            if (arrEnd < 0 || arrEnd == arrStart + 1) return; // empty array

            string arrayJson = json.Substring(arrStart, arrEnd - arrStart + 1);
            sb.AppendLine("\n=== " + label + " (use in \"" + key + "\" parameter) ===");
            sb.AppendLine(arrayJson);
        }
    }
}
