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
      ""description"": ""Creates a new D365FO X++ metadata object using the MetaModel API. CRITICAL: This is the ONLY correct way to create X++ objects. NEVER create metadata XML files directly via terminal, file writes, or Set-Content — that bypasses the MetaModel API and produces corrupted/unregistered objects. Supports: AxClass, AxTable, AxForm, AxEdt, AxEnum, AxMenuItemDisplay/Output/Action, AxQuery, AxView, AxDataEntityView, AxSecurityPrivilege/Duty/Role, AxService, AxServiceGroup, AxMap, AxMenu, AxTile, AxConfigurationKey. The object is automatically added to the active VS project."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""objectType"": {
            ""type"": ""string"",
            ""description"": ""The D365FO object type to create."",
            ""enum"": [""AxClass"", ""AxTable"", ""AxView"", ""AxDataEntityView"", ""AxMap"", ""AxEdt"", ""AxEnum"", ""AxForm"", ""AxTile"", ""AxMenu"", ""AxMenuItemDisplay"", ""AxMenuItemOutput"", ""AxMenuItemAction"", ""AxQuery"", ""AxSecurityPrivilege"", ""AxSecurityDuty"", ""AxSecurityRole"", ""AxService"", ""AxServiceGroup"", ""AxConfigurationKey""]
          },
          ""objectName"": { ""type"": ""string"", ""description"": ""Name of the object. Must follow the model's naming prefix convention."" },
          ""declaration"": { ""type"": ""string"", ""description"": ""Raw X++ class/table declaration code block. Do NOT wrap in CDATA — the API handles that automatically."" },
          ""methods"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Array of complete X++ method source strings. Raw code only, no CDATA wrappers."" },
          ""metadataXml"": { ""type"": ""string"", ""description"": ""XML fragment for structural metadata. REQUIRED for AxEnum (enum values), AxTable (fields, field groups, indexes, relations), AxQuery (data sources), AxMenuItemDisplay/Output/Action (properties), AxEdt (properties), AxSecurityPrivilege/Duty/Role (entry points). For AxEnum, use: <EnumValues><AxEnumValue><Name>ValueName</Name><Value>0</Value><Label>@LabelId</Label></AxEnumValue></EnumValues>. For AxTable fields, use: <Fields><AxTableFieldString><Name>FieldName</Name><ExtendedDataType>EdtName</ExtendedDataType></AxTableFieldString></Fields>. NEVER omit this for enums or tables — objects without metadata are empty shells."" },
          ""modelName"": { ""type"": ""string"", ""description"": ""Target model name. If omitted, uses the active project's model."" }
        },
        ""required"": [""objectType"", ""objectName""]
      }
    },
    {
      ""name"": ""xpp_read_object"",
      ""description"": ""Reads a D365FO X++ object by type and name using the MetaModel API. CRITICAL: This is the ONLY correct way to read X++ objects. NEVER read metadata XML files directly via Get-Content or terminal commands. Returns declaration, methods, metadata, model name, and whether the object is in a custom (editable) or standard (read-only) model. For objects not supported by the typed API, pass filePath instead."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""objectType"": { ""type"": ""string"", ""description"": ""The object type (AxClass, AxTable, AxForm, AxEnum, AxEdt, AxView, AxQuery)."" },
          ""objectName"": { ""type"": ""string"", ""description"": ""The object name."" },
          ""filePath"": { ""type"": ""string"", ""description"": ""Absolute path to the metadata XML file. Use this for types not supported by the typed API, or when you have the path."" }
        },
        ""required"": []
      }
    },
    {
      ""name"": ""xpp_update_object"",
      ""description"": ""Updates an existing D365FO X++ object using the MetaModel API. CRITICAL: This is the ONLY correct way to modify X++ objects. NEVER edit metadata XML files directly. Can update declaration, add/replace/remove methods. ONLY works on custom model objects."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""objectType"": { ""type"": ""string"", ""description"": ""The object type (AxClass, AxTable, AxForm, AxEdt, AxEnum)."" },
          ""objectName"": { ""type"": ""string"", ""description"": ""The object name to update."" },
          ""declaration"": { ""type"": ""string"", ""description"": ""New raw X++ declaration code. Replaces existing. No CDATA wrappers."" },
          ""methods"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Methods to add or replace (matched by name). Raw X++ code only, no CDATA."" },
          ""removeMethodNames"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Method names to remove."" },
          ""metadataXml"": { ""type"": ""string"", ""description"": ""New metadata XML fragment."" }
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
      ""description"": ""Returns the D365FO development environment info: metadata folders, active project, active model name/ID/layer. Use this to understand the project context. CRITICAL RULE: In D365FO/X++ projects, NEVER use terminal commands to create, read, update, search, or list metadata XML files. Always use the xpp_* tools — they use the MetaModel API which correctly registers objects, maintains cross-references, and respects model boundaries."",
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
    }
}
