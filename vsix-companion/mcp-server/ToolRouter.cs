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
          ""typedMetadata"": { ""type"": ""object"", ""description"": ""Full strongly-typed metadata graph for the target object type. Preferred for complete metadata writes across all object types."" },
          ""declaration"": { ""type"": ""string"", ""description"": ""Raw X++ class/table declaration code block. Plain code only — NO XML, NO CDATA wrappers."" },
          ""methods"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Array of complete X++ method source strings. Plain code only — NO XML, NO CDATA wrappers. Any XML content will be rejected with an error."" },
          ""properties"": { ""type"": ""object"", ""description"": ""Key-value pairs for scalar metadata properties set via reflection. Keys are property names (e.g. Label, IsExtensible, TableGroup, ObjectType, Object, FormRef). Values are strings — type coercion is automatic (bool, int, enum). Use xpp_read_object on an existing object to discover available property names."" },
          ""enumValues"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""value"": { ""type"": ""integer"" }, ""label"": { ""type"": ""string"" } }, ""required"": [""name"", ""value""] }, ""description"": ""Enum values for AxEnum or AxEnumExtension. Each entry creates an AxEnumValue with Name, Value (integer), and optional Label."" },
          ""fields"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""fieldType"": { ""type"": ""string"", ""enum"": [""String"", ""Int"", ""Real"", ""Date"", ""DateTime"", ""Enum"", ""Int64"", ""Container"", ""Guid"", ""Time""] }, ""extendedDataType"": { ""type"": ""string"" }, ""enumType"": { ""type"": ""string"" }, ""label"": { ""type"": ""string"" } }, ""required"": [""name"", ""fieldType""] }, ""description"": ""Table fields for AxTable or AxTableExtension. fieldType determines the field class (AxTableFieldString, AxTableFieldInt, etc.)."" },
          ""indexes"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""allowDuplicates"": { ""type"": ""boolean"" }, ""fields"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } } }, ""required"": [""name"", ""fields""] }, ""description"": ""Table indexes for AxTable. Each index has a name and array of field names."" },
          ""fieldGroups"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""label"": { ""type"": ""string"" }, ""fields"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } } }, ""required"": [""name"", ""fields""] }, ""description"": ""Table field groups for AxTable. Each group has a name and array of field names."" },
          ""relations"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""relatedTable"": { ""type"": ""string"" }, ""constraints"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""field"": { ""type"": ""string"" }, ""relatedField"": { ""type"": ""string"" } } } } }, ""required"": [""name"", ""relatedTable""] }, ""description"": ""Table relations for AxTable. Each relation maps fields to related table fields."" },
          ""dataSources"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""table"": { ""type"": ""string"" }, ""parentDataSource"": { ""type"": ""string"" }, ""joinMode"": { ""type"": ""string"" }, ""linkType"": { ""type"": ""string"" }, ""dynamicFields"": { ""type"": ""boolean"" }, ""relations"": { ""type"": ""boolean"" }, ""firstOnly"": { ""type"": ""boolean"" }, ""ranges"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""field"": { ""type"": ""string"" }, ""value"": { ""type"": ""string"" } } } } }, ""required"": [""name""] }, ""description"": ""Query data sources for AxQuery. Use this to define table sources and optional ranges."" },
          ""entryPoints"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""objectType"": { ""type"": ""string"" }, ""objectName"": { ""type"": ""string"" }, ""grant"": { ""type"": ""string"" } }, ""required"": [""name""] }, ""description"": ""Security entry points for AxSecurityPrivilege/Duty/Role."" },
          ""modelName"": { ""type"": ""string"", ""description"": ""Target model name. If omitted, uses the active project's model."" }
        },
        ""required"": [""objectType"", ""objectName""]
      }
    },
    {
      ""name"": ""xpp_read_object"",
      ""description"": ""Reads a D365FO X++ object by type and name using the MetaModel API. CRITICAL: This is the ONLY correct way to read X++ objects. NEVER read metadata files directly via Get-Content or terminal commands. Returns declaration, methods, and strongly-typed metadata, including a full typedMetadata object graph for round-trip safe writes across all types (plus compatibility fields like properties, enumValues, fields, indexes, fieldGroups, relations, dataSources). Also returns model name and editability. For objects not supported by the typed API, pass filePath instead."",
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
          ""typedMetadata"": { ""type"": ""object"", ""description"": ""Full strongly-typed metadata graph for complete metadata updates across all object types."" },
          ""declaration"": { ""type"": ""string"", ""description"": ""New raw X++ declaration code. Replaces existing. Plain code only — NO XML, NO CDATA."" },
          ""methods"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Methods to add or replace (matched by name). Plain X++ code only — NO XML, NO CDATA. Any XML content will be rejected."" },
          ""removeMethodNames"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Method names to remove."" },
          ""properties"": { ""type"": ""object"", ""description"": ""Key-value pairs for scalar metadata properties to set or update. Same format as xpp_create_object."" },
          ""enumValues"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""value"": { ""type"": ""integer"" }, ""label"": { ""type"": ""string"" } }, ""required"": [""name"", ""value""] }, ""description"": ""Enum values to add (for AxEnum). Values with matching names are skipped."" },
          ""fields"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""fieldType"": { ""type"": ""string"" }, ""extendedDataType"": { ""type"": ""string"" }, ""enumType"": { ""type"": ""string"" }, ""label"": { ""type"": ""string"" } }, ""required"": [""name"", ""fieldType""] }, ""description"": ""Table fields to add (for AxTable)."" },
          ""indexes"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""allowDuplicates"": { ""type"": ""boolean"" }, ""fields"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } } }, ""required"": [""name"", ""fields""] }, ""description"": ""Indexes to add (for AxTable)."" },
          ""fieldGroups"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""label"": { ""type"": ""string"" }, ""fields"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } } }, ""required"": [""name"", ""fields""] }, ""description"": ""Field groups to add (for AxTable)."" },
          ""relations"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""relatedTable"": { ""type"": ""string"" }, ""constraints"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""field"": { ""type"": ""string"" }, ""relatedField"": { ""type"": ""string"" } } } } }, ""required"": [""name"", ""relatedTable""] }, ""description"": ""Relations to add (for AxTable)."" },
          ""dataSources"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""table"": { ""type"": ""string"" }, ""parentDataSource"": { ""type"": ""string"" }, ""joinMode"": { ""type"": ""string"" }, ""linkType"": { ""type"": ""string"" }, ""dynamicFields"": { ""type"": ""boolean"" }, ""relations"": { ""type"": ""boolean"" }, ""firstOnly"": { ""type"": ""boolean"" }, ""ranges"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" }, ""field"": { ""type"": ""string"" }, ""value"": { ""type"": ""string"" } } } } }, ""required"": [""name""] }, ""description"": ""Query data sources for AxQuery updates."" },
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
      ""description"": ""Searches for D365FO metadata objects by name across ALL loaded models and packages using the MetaModel API. CRITICAL: Always use this instead of Get-ChildItem/dir/file searches on metadata folders. NEVER search the file system for X++ objects. Returns matching objects with type, model, and editability. PERFORMANCE TIP: Always provide objectType when known -- searching 1 type is 5-10x faster than searching all 10 types."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""objectName"": { ""type"": ""string"", ""description"": ""Full or partial name to search for (case-insensitive)."" },
          ""objectType"": { ""type"": ""string"", ""description"": ""Strongly recommended: limits search to 1 type (5-10x faster). Values: AxClass, AxTable, AxForm, AxEnum, AxView, AxQuery, AxEdt, AxMenuItemDisplay, AxMenuItemOutput, AxMenuItemAction."" },
          ""exactMatch"": { ""type"": ""boolean"", ""description"": ""If true, only exact name matches. Default false."" },
          ""maxResults"": { ""type"": ""integer"", ""description"": ""Max results to return. Default 25. Max 100. Lower values return faster."" }
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
          ""modelName"": { ""type"": ""string"", ""description"": ""Filter by model name. EFFICIENCY TIP: after finding an object use its modelName here to find related objects in the same model — much faster than searching all models."" },
          ""maxResults"": { ""type"": ""integer"", ""description"": ""Max results. Default 25. Max 100. Lower values return faster."" }
        },
        ""required"": [""nameFilter""]
      }
    },
    {
      ""name"": ""xpp_find_references"",
      ""description"": ""Queries the local D365FO cross-reference database to find all objects that USE or REFERENCE a given object (incoming references). Uses an indexed SQL query — returns results in milliseconds regardless of codebase size. Ideal for: finding all classes/forms that use a table, finding who extends a class, finding all method call sites. After xpp_find_object returns a result, immediately call this to discover the usage graph without expensive full scans."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""objectType"": { ""type"": ""string"", ""description"": ""Required. Type of the TARGET object (the one being searched for). AxClass, AxTable, AxForm, AxEnum, AxView, AxQuery, AxEdt, AxDataEntityView, AxMap, AxService."" },
          ""objectName"": { ""type"": ""string"", ""description"": ""Required. Exact name of the target object to find references to."" },
          ""referenceKind"": { ""type"": ""string"", ""description"": ""Optional. Filter by reference kind. Any (default), TypeReference (code that uses the type), MethodCall (calls a method on this object), ClassExtended (extends this class), InterfaceImplementation (implements this interface), Attribute, MethodOverride."" },
          ""maxResults"": { ""type"": ""integer"", ""description"": ""Max results. Default 50. Max 200."" }
        },
        ""required"": [""objectType"", ""objectName""]
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
    },
    {
      ""name"": ""xpp_validate_object"",
      ""description"": ""Validates that a D365FO object exists, is added to the active VS project, and has the expected metadata properties/fields/relations. Use after xpp_create_object or xpp_update_object to confirm all metadata was applied correctly. Returns valid=true only when ALL checks pass: object exists, in project, and all specified properties/fields match. Returns a mismatches list describing any discrepancies."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""objectType"": { ""type"": ""string"", ""description"": ""The object type (AxClass, AxTable, AxEnum, AxEdt, AxView, AxQuery)."" },
          ""objectName"": { ""type"": ""string"", ""description"": ""The object name to validate."" },
          ""typedMetadata"": { ""type"": ""object"", ""description"": ""Optional full typed metadata graph to validate as a subset against the current object."" },
          ""properties"": { ""type"": ""object"", ""description"": ""Expected key-value property pairs to verify (e.g. {Label: '@MyLabel', IsExtensible: 'true'}). Each entry is checked against the actual object."" },
          ""fields"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" } }, ""required"": [""name""] }, ""description"": ""Expected table fields — only names are checked for existence."" },
          ""enumValues"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" } }, ""required"": [""name""] }, ""description"": ""Expected enum value names to verify exist on the enum."" },
          ""indexes"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" } }, ""required"": [""name""] }, ""description"": ""Expected index names to verify exist on the table."" },
          ""relations"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""name"": { ""type"": ""string"" } }, ""required"": [""name""] }, ""description"": ""Expected relation names to verify exist on the table."" }
        },
        ""required"": [""objectType"", ""objectName""]
      }
    },
    {
      ""name"": ""xpp_get_object_type_schema"",
      ""description"": ""Returns a generated JSON schema for a D365FO metadata object type based on reflective type inspection. Use this to discover full strongly-typed typedMetadata shape for create/update/validate payloads."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""objectType"": { ""type"": ""string"", ""description"": ""The object type to inspect (for example AxClass, AxTable, AxForm, AxEnum, AxQuery, AxView)."" }
        },
        ""required"": [""objectType""]
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
                case "xpp_find_object": return HandleFindObject(idToken, json);
                case "xpp_list_objects": return HandleListObjects(idToken, json);
                case "xpp_find_references": return HandleFindReferences(idToken, json);
                case "xpp_get_model_info": return HandleGetModelInfo(idToken, json);
                case "xpp_list_models": return DelegateToBridge(idToken, json, "list_models");
                case "xpp_read_label": return HandleReadLabel(idToken, json);
                case "xpp_create_label": return HandleCreateLabel(idToken, json);
                case "xpp_add_to_project": return HandleAddToProject(idToken, json);
                case "xpp_list_project_items": return DelegateToBridge(idToken, json, "list_project_items");
                case "xpp_get_environment": return DelegateToBridge(idToken, json, "get_environment");
                case "xpp_validate_object": return HandleValidateObject(idToken, json);
                case "xpp_get_object_type_schema": return HandleGetObjectTypeSchema(idToken, json);

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

        private string HandleFindObject(string idToken, string json)
        {
            string argsJson = JsonHelpers.ExtractToolArgumentsObject(json);
            string objectName = JsonHelpers.ExtractArgString(argsJson, "objectName", "name");

            if (string.IsNullOrEmpty(objectName))
                return JsonHelpers.BuildToolResult(idToken,
                    "Parameter 'objectName' is required.", isError: true);

            return DelegateToBridge(idToken, json, "find_object");
        }

        private string HandleListObjects(string idToken, string json)
        {
            string argsJson = JsonHelpers.ExtractToolArgumentsObject(json);
            string nameFilter = JsonHelpers.ExtractArgString(argsJson, "nameFilter");

            if (string.IsNullOrEmpty(nameFilter))
                return JsonHelpers.BuildToolResult(idToken,
                    "Parameter 'nameFilter' is required. Provide a substring to match object names (e.g., 'CustTable', 'SalesOrder').", isError: true);

            return DelegateToBridge(idToken, json, "list_objects");
        }

        private string HandleFindReferences(string idToken, string json)
        {
            string argsJson = JsonHelpers.ExtractToolArgumentsObject(json);
            string objectType = JsonHelpers.ExtractArgString(argsJson, "objectType");
            string objectName = JsonHelpers.ExtractArgString(argsJson, "objectName");

            if (string.IsNullOrEmpty(objectType))
                return JsonHelpers.BuildToolResult(idToken,
                    "Parameter 'objectType' is required (e.g. AxTable, AxClass).", isError: true);
            if (string.IsNullOrEmpty(objectName))
                return JsonHelpers.BuildToolResult(idToken,
                    "Parameter 'objectName' is required.", isError: true);

            return DelegateToBridge(idToken, json, "find_references");
        }

        private string HandleGetModelInfo(string idToken, string json)
        {
            string argsJson = JsonHelpers.ExtractToolArgumentsObject(json);
            string modelName = JsonHelpers.ExtractArgString(argsJson, "modelName");

            if (string.IsNullOrEmpty(modelName))
                return JsonHelpers.BuildToolResult(idToken,
                    "Parameter 'modelName' is required.", isError: true);

            return DelegateToBridge(idToken, json, "get_model_info");
        }

        private string HandleReadLabel(string idToken, string json)
        {
            string argsJson = JsonHelpers.ExtractToolArgumentsObject(json);
            string labelFileId = JsonHelpers.ExtractArgString(argsJson, "labelFileId");

            if (string.IsNullOrEmpty(labelFileId))
                return JsonHelpers.BuildToolResult(idToken,
                    "Parameter 'labelFileId' is required.", isError: true);

            return DelegateToBridge(idToken, json, "read_label");
        }

        private string HandleCreateLabel(string idToken, string json)
        {
            string argsJson = JsonHelpers.ExtractToolArgumentsObject(json);
            string labelFileId = JsonHelpers.ExtractArgString(argsJson, "labelFileId");
            string labelId = JsonHelpers.ExtractArgString(argsJson, "labelId");
            string text = JsonHelpers.ExtractArgString(argsJson, "text");

            if (string.IsNullOrEmpty(labelFileId) || string.IsNullOrEmpty(labelId) || string.IsNullOrEmpty(text))
                return JsonHelpers.BuildToolResult(idToken,
                    "Parameters 'labelFileId', 'labelId', and 'text' are required.", isError: true);

            return DelegateToBridge(idToken, json, "create_label");
        }

        private string HandleAddToProject(string idToken, string json)
        {
            string argsJson = JsonHelpers.ExtractToolArgumentsObject(json);
            string objectType = JsonHelpers.ExtractArgString(argsJson, "objectType", "type");
            string objectName = JsonHelpers.ExtractArgString(argsJson, "objectName", "name");

            if (string.IsNullOrEmpty(objectType) || string.IsNullOrEmpty(objectName))
                return JsonHelpers.BuildToolResult(idToken,
                    "Parameters 'objectType' and 'objectName' are required.", isError: true);

            return DelegateToBridge(idToken, json, "add_to_project");
        }

        private string HandleValidateObject(string idToken, string json)
        {
            string argsJson = JsonHelpers.ExtractToolArgumentsObject(json);
            string objectType = JsonHelpers.ExtractArgString(argsJson, "objectType", "type");
            string objectName = JsonHelpers.ExtractArgString(argsJson, "objectName", "name");

            if (string.IsNullOrEmpty(objectType) || string.IsNullOrEmpty(objectName))
                return JsonHelpers.BuildToolResult(idToken,
                    "Parameters 'objectType' and 'objectName' are required.", isError: true);

            return DelegateToBridge(idToken, json, "validate_object");
        }

        private string HandleGetObjectTypeSchema(string idToken, string json)
        {
            string argsJson = JsonHelpers.ExtractToolArgumentsObject(json);
            string objectType = JsonHelpers.ExtractArgString(argsJson, "objectType", "type");

            if (string.IsNullOrEmpty(objectType))
                return JsonHelpers.BuildToolResult(idToken,
                    "Parameter 'objectType' is required.", isError: true);

            return DelegateToBridge(idToken, json, "get_object_type_schema");
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
