# X++ AI Copilot Companion

A Visual Studio 2022 extension (VSIX) + MCP server that gives GitHub Copilot full D365FO X++ awareness — language knowledge, project context, and **16 metadata tools** that create, read, update, validate, search, and manage X++ objects through the official MetaModel API.

---

## Problem Statement

GitHub Copilot has no built-in knowledge of X++. When a D365FO developer uses Copilot in Visual Studio, it:

- Does not understand X++ syntax, data types, or keywords (`ttsbegin`, `next`, `select forupdate`, etc.)
- Has no concept of the five D365FO extensibility mechanisms (metadata extensions, CoC, events, extension methods, plug-in/factory)
- Does not know that Microsoft standard objects are read-only and must be extended, never modified directly
- Has no awareness of model descriptors, package dependencies, or ownership rules that determine whether to extend or modify directly
- Cannot reason about where to place new objects or which model/package they belong in
- Has no awareness of the active project's model, metadata structure, or naming conventions
- Cannot create, modify, or inspect D365FO metadata objects programmatically

The result is that Copilot generates invalid or dangerous code — modifying base objects, missing `next` calls in CoC, using C# syntax instead of X++, or failing to check model dependencies.

## Solution

This extension solves the problem at three levels:

### 1. MCP Server with 22 Metadata Tools

A standalone MCP (Model Context Protocol) server (`XppMcpServer.exe`) exposes 22 tools that Copilot can call directly. These tools use the D365FO MetaModel API — the **same API that Visual Studio uses internally** — to manipulate metadata with full type safety.

| Tool | Description |
|---|---|
| `xpp_create_object` | Create any of 19 base object types, extensions (table, enum, EDT, form, menu, data entity, security), composite data entities, label files and menu extensions, with strongly-typed JSON metadata. All-or-nothing: relations are checked before saving, a failed metadata apply rolls the object back, an existing name is never overwritten, the object is added to the active project and the response ends with a `Validation:` line; a failed validation returns an error |
| `xpp_read_object` | Read object declaration, methods, and typed metadata (properties, fields, indexes, etc.). Objects over 25,000 characters return a summary first; `sections` returns only the parts you need |
| `xpp_update_object` | Update declaration, methods, or metadata on existing custom objects |
| `xpp_validate_object` | Validate that an object exists and has the expected metadata; project membership is required only for objects of the project's model |
| `xpp_find_object` | Search for objects by name pattern and type; security roles, duties and privileges also by their label text (closest labels when no exact wording matches) |
| `xpp_list_objects` | Look up a handful of objects by a specific name substring |
| `xpp_list_model_objects` | List what one model contains (counts per type, then paged names) — reads the model index on disk, outside VS |
| `xpp_find_references` | Query the cross-reference database for all objects that use/reference a given object; `includeCode` also shows the calling code lines |
| `xpp_find_examples` | Standard implementations to reuse or copy: for a task ("batch job with a contract", "number sequence", forms with a pattern) or objects similar to a given one |
| `xpp_search_code` | Text or regex search over the X++ code of every package (classes, tables, forms, views, entities, queries, maps) |
| `xpp_get_object_type_schema` | Return the generated JSON schema for a metadata type via reflective type inspection |
| `xpp_get_form_layout` | Show a form's data sources and control tree with all extensions merged and attributed — the survey step before placing form-extension controls |
| `xpp_get_model_info` | Get model descriptor (layer, references, dependencies) |
| `xpp_list_models` | List all installed models with editability status |
| `xpp_read_label` | Read a label by ID and language |
| `xpp_create_label` | Create labels: one, or many in one call (`labels` array), each with translations; each label file is written once; per-label result (created / unchanged / failed) |
| `xpp_add_to_project` | Add an existing object to the active VS project |
| `xpp_list_project_items` | List items in the active VS project |
| `xpp_get_environment` | Get D365FO environment info (metadata folders, active model) |
| `xpp_search_docs` | Search Microsoft D365FO documentation (Microsoft Learn semantic search; pages returned as markdown) |
| `xpp_delete_object` | Delete an object of your own model completely (metadata file and project entry) and remove its own cross-reference entries. Refused for standard/ISV objects, and while other objects still reference it unless `force=true` |
| `xpp_remove_from_project` | Exclude an object from the active project; it stays in the model |
| `xpp_select_project` | List the D365FO projects of the open solution, or make one active (added to the solution if needed, selected and set as startup project) |
| `xpp_build` | Build a custom model (LabelC + xppc with cross-references) like Dynamics 365 > Build, and return BUILD SUCCEEDED/FAILED with every error, fatal error and warning; `scope=project` reports only the active project's objects. Runs off the VS UI thread |

#### Supported Object Types (19)

All 19 base types are created via the typed MetaModel API:

| Object Type | Read | Create | Update | Validate | Status |
|---|:---:|:---:|:---:|:---:|---|
| AxClass | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxTable | ✅ | ✅ | ✅ | ✅ | Fully tested. Change own fields with updateFields (EDT, label, help text, properties; the base type never changes), remove fields/indexes/field groups/relations; best-practice warnings after every save |
| AxView | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxDataEntityView | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxEdt | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxEnum | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxForm | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxMenu | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxMenuItemAction | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxMenuItemDisplay | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxMenuItemOutput | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxQuery | ✅ | ✅ | ✅ | ✅ | Data sources and ranges (class declaration added automatically); same code as the VS Code extension, where it is regression-tested |
| AxSecurityPrivilege | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxSecurityDuty | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxSecurityRole | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxService | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxServiceGroup | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxConfigurationKey | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxTile | ✅ | ✅ | ✅ | ✅ | Fully tested |

**All 19 types support read → create → validate → compare.**

#### Object Extensions

`IMetaModelService` can only read extensions, so extension writes go through the D365FO metadata provider (`IMetaModelProviders.CurrentMetadataProvider`). Extensions are named `<BaseObject>.<Suffix>` and can only be created in custom models.

| Extension Type | Read | Create | Update | Validate | What can be changed |
|---|:---:|:---:|:---:|:---:|---|
| AxTableExtension | ✅ | ✅ | ✅ | ✅ | Add fields, indexes, field groups, relations; property modifications; change or remove its own fields, indexes, field groups and relations (updateFields, remove*) |
| AxEnumExtension | ✅ | ✅ | ✅ | ✅ | Add enum values; property modifications |
| AxEdtExtension | ✅ | ✅ | ✅ | ✅ | Property modifications |
| AxFormExtension | ✅ | ✅ | ✅ | ✅ | Add controls with explicit placement (parent + begin/end/after), data sources; control property modifications (base/other-extension controls, D365FO-modifiable properties only); update, move and remove the extension's own controls (`updateControls`, `removeControls`, `removeControlModifications`) |
| AxDataEntityViewExtension | ✅ | ✅ | ✅ | ✅ | Add mapped fields (`entityFields` or `typedMetadata.Fields` with `$type`), also when the data source is another entity or a view; the staging table is extended with the underlying table field |
| AxCompositeDataEntityView | ✅ | ✅ | ✅ | ✅ | `rootDataEntities`: root + embedded entity references with their relation; staging tables checked (RowId/ParentRowId, parent relation) |

Form extensions are survey-first: `xpp_get_form_layout` shows the base form with **all** existing extensions merged (each extension control and data source is attributed) and the child structure each container's pattern expects. New controls must name a parent and a position; placement is validated against that merged form before anything is saved, and re-verified after saving. New controls get the property values their form pattern requires, and the compiler's own form-extension validators run after saving: any error the change would add to the build restores the previous version and is returned with the build's text.

#### Strongly-Typed JSON Metadata

All create/update/read operations use **strongly-typed JSON parameters** instead of raw XML. This ensures reliable round-trip metadata fidelity:

- **`properties`** — Key-value pairs for scalar metadata (Label, IsExtensible, TableGroup, etc.) with automatic type coercion (string → bool/int/long/enum)
- **`enumValues`** — `[{ "name": "None", "value": 0, "label": "None" }, ...]`
- **`fields`** — `[{ "name": "CustId", "fieldType": "String", "extendedDataType": "CustAccount", "label": "Customer" }, ...]`
- **`indexes`** — `[{ "name": "CustIdx", "allowDuplicates": false, "fields": ["CustId"] }, ...]`
- **`fieldGroups`** — `[{ "name": "AutoReport", "label": "Report", "fields": ["CustId", "Name"] }, ...]`
- **`relations`** — `[{ "name": "CustTableRel", "relatedTable": "CustTable", "constraints": [{ "field": "CustId", "relatedField": "AccountNum" }] }, ...]`
- **`entryPoints`** — `[{ "name": "Maintain", "objectType": "MenuItemDisplay", "objectName": "CustTable" }, ...]`

Read results return the **same JSON format**, so Copilot can use a read result directly as a template for creating or modifying objects.

Other behaviour worth knowing:

- **EDTs**: set `properties.EdtType` (String, Int, Int64, Real, Enum, Date, UtcDateTime, Time, Guid, Container) or `properties.Extends`; the read shows the kind.
- **Label files**: `xpp_create_object` with `AxLabelFile` and `properties.Languages` (e.g. `en-US,fr-CA`) creates one label file per language; `xpp_create_label` accepts `translations` and a `labels` array.
- **`typedMetadata`**: items of polymorphic collections take `"$type"` (as `xpp_read_object` writes them); unknown keys are refused, so nothing is dropped silently. Query data sources default to `dynamicFields` true and accept `fields`.
- **Menu extensions**: `AxMenuExtension` with `menuElements` (menu item, type, parent submenu, position).
- **`methods`**: plain X++ source strings, or objects with `source`.
- **Reads**: data entities include data sources, entity fields and the staging table; `methodNames` returns only the requested methods; only non-default properties are shown.
- **Validation** compares only the attributes you passed, treats Yes/True and No/False alike, and checks project membership for objects of the project's model.
- **Find/list** reject unknown types, find data entities and security roles, and say `0 matches` when nothing matches; `xpp_list_model_objects` explains package names that contain several models.
- **Where-used** marks objects from packages that are not installed locally (typically test packages).

### 2. Embedded X++ Knowledge (System Prompt Injection)

A comprehensive X++ language reference and decision-making framework is compiled into the VSIX assembly as an embedded resource. It covers:

- **Ownership-first decision logic**: Determines whether to modify directly or extend, based on model descriptors and package ownership
- **Model & package awareness**: Model descriptor format, `ModuleReferences`, `ModelModule`, dependency rules
- **Five extensibility mechanisms** with decision trees:
  1. Metadata extensions (table/form/enum/EDT extensions — add fields, controls, relations)
  2. Chain of Command (CoC) — wrapping existing methods (class, table, form, data source, data field, control, data entity)
  3. Events & event handlers — table data events, form control/data source/data field events, delegate subscriptions, EventHandlerResult patterns
  4. Extension methods — adding new methods to existing types via `_Extension` suffix classes
  5. Plug-in / factory — SysExtension and SysPlugin frameworks for strategy pattern variants
- **X++ syntax**: Data types, classes, method modifiers, data access (`select`, `while select`, set-based operations, Query framework)
- **Display & edit methods**: On tables, as extension methods on standard tables, form-level
- **Object type properties**: Semantic descriptions of all object types with their valid property values
- **Transaction control, exception handling, config key guards**
- **Naming conventions and best practices**

This prompt is injected into **every** Copilot interaction — both in the context payload and the AI service system message. It travels with the VSIX binary, so it works in any D365FO project regardless of the repository.

### 3. Dynamic Project Context Pipeline

The extension crawls the open solution's metadata files in real time, scores them by relevance to the developer's current task, and feeds a token-budgeted selection into Copilot's context. This gives Copilot awareness of:

- What objects exist in the current project
- The active document's content
- Related classes, tables, EDTs, and enums
- Whether objects are custom (editable) or Microsoft reference (extend only)

---

## Architecture

For detailed architecture diagrams, design decisions, data flow, reliability mechanisms, and file structure see [ARCHITECTURE.md](ARCHITECTURE.md).

---

## Installation

### Prerequisites

- Visual Studio 2022 (17.14+) — Community, Professional, or Enterprise
- .NET Framework 4.8
- Visual Studio SDK workload (for building from source)
- D365FO development tools extension (Microsoft.Dynamics.FinOps.ToolsVS2022)

### Build from Source

1. Clone this repository
2. Open `DynDevTools.sln` in Visual Studio 2022 or later (or run `vsix-companion\build.cmd Release`; it finds MSBuild with vswhere)
3. Ensure the **Visual Studio extension development** workload is installed
4. Build the solution (`Ctrl+Shift+B`)
5. The VSIX will be in `vsix-companion/bin/Release/XppAiCopilotCompanion.vsix`
6. The MCP server will be in `vsix-companion/mcp-server/bin/Release/XppMcpServer.exe`

### Install the VSIX

1. Close all Visual Studio instances
2. Double-click `XppAiCopilotCompanion.vsix`
3. Follow the installer prompts
4. Restart Visual Studio

> **Upgrading:** On the first VS startup after installing a new version, the extension automatically detects the version mismatch, kills the old MCP server, cleans the companion folder, and starts fresh. No manual cleanup needed.

### Deploy to a D365FO Dev VM

Copy the `.vsix` file to the dev VM and double-click to install. The extension activates automatically when a solution is opened. The MCP server starts and stops automatically.

---

## Usage

### Configure

Go to **Tools > Options > X++ AI Copilot > General** and set the **Environment Type** to match your D365FO environment:

| Environment Type | When to use | PackagesLocalDirectory |
|---|---|---|
| **UDE** *(default)* | Unified Development Environment / remote dev box | Auto-detected from registry |
| **Onebox** | Local all-in-one install (e.g. demo VM) | `C:\AOSService\PackagesLocalDirectory` *(default)* |
| **CHE** | Cloud-Hosted Environment (local install) | `K:\AOSService\PackagesLocalDirectory` *(default)* |

For **UDE**, metadata paths are auto-detected from the active D365FO VS configuration:
1. **Registry** — `HKCU\Software\Microsoft\Dynamics\AX7\Development\Configurations` → `CurrentMetadataConfig`
2. **JSON config file** — `%LOCALAPPDATA%\Microsoft\Dynamics365\XPPConfig` → `ModelStoreFolder` / `ReferencePackagesPaths`

For **Onebox / CHE**, the default `PackagesLocalDirectory` paths are used automatically. Override them in the **Packages Local Directory** field if your installation uses a non-standard path.

The **Metadata Roots** fields (Custom and Reference) can be used for fully custom path overrides on any environment type. Leave them empty to use the environment-driven auto-detection.

### MCP Server Registration

The MCP server is registered automatically when the VSIX loads. You can also use:

- **Tools > X++ AI: Register MCP Server** — Manually register/restart the MCP server
- **Tools > X++ AI: MCP Diagnostics** — Check MCP server health and tool availability

### Using with Copilot Chat

Once the MCP server is registered, Copilot Chat in Visual Studio can call the 16 `xpp_*` tools directly. Examples:

- *"Create an AxEnum called MyStatus with values None, Active, and Closed"*
- *"Read the CustTable and show me its fields and indexes"*
- *"Add a String field called MyCustomField to my table"*
- *"Find all classes that start with 'Cust'"*
- *"What models are installed?"*

Copilot uses the **same MetaModel API that Visual Studio uses**, so objects are properly registered, cross-referenced, and added to the active project.

### Menu Commands

All commands are under **Tools** in the main menu:

| Command | Description |
|---|---|
| **X++ AI: Refresh Context** | Builds the context payload and displays it in the Output window |
| **X++ AI: Generate Code** | Prompts for a description → AI generates X++ → applies to active document |
| **X++ AI: Create Object** | Prompts for type/name/description → creates via MetaModel API → adds to project |
| **X++ AI: Register MCP Server** | Registers or restarts the MCP server for Copilot |
| **X++ AI: MCP Diagnostics** | Shows MCP server health, port status, and tool list |

---

## Configuration Reference

### Environment

| Setting | Default | Description |
|---|---|---|
| **Environment Type** | UDE | `UDE` = remote dev box (registry auto-detects paths). `Onebox` = local all-in-one. `CHE` = Cloud-Hosted Environment. |
| **Packages Local Directory** | *(env default)* | Override path to `PackagesLocalDirectory`. Leave blank to use the default for the selected environment type. Ignored when Environment Type is UDE. |

### Metadata Roots

| Setting | Default | Description |
|---|---|---|
| Custom Metadata Roots | *(auto-detect)* | Semicolon-separated paths to your model's metadata directories. Leave empty to use environment-driven auto-detection. |
| Reference (MS) Metadata Roots | *(auto-detect)* | Semicolon-separated paths to Microsoft reference metadata. Leave empty to use environment-driven auto-detection. |

### Context Budget

| Setting | Default | Description |
|---|---|---|
| Max Context Tokens | 6000 | Total token budget for the AI context payload |
| Max Snippet Tokens Per Object | 350 | Token limit per individual object in context |
| Max Active Document Tokens | 1000 | Token budget for the currently open file |

### Selection

| Setting | Default | Description |
|---|---|---|
| Top N Objects | 20 | Maximum relevant objects to include |
| Min Custom Objects | 3 | Minimum custom objects guaranteed in context |
| Min Reference Objects | 3 | Minimum MS reference objects guaranteed in context |
| Include Samples | false | Include files from `\samples\` directories |

---

## Limitations

- **VS Copilot MCP discovery**: Visual Studio's Copilot Chat does not always discover or use the registered MCP endpoint automatically. When this happens, you can guide Copilot by explicitly referencing the tools in your prompt (e.g. *"use xpp_read_object to read CustTable"*) or by invoking tools via a PowerShell HTTP call to `http://127.0.0.1:21329/` to verify the server is responsive and then retrying in Copilot Chat. This is a known VS Copilot MCP integration limitation.
- **VS Copilot language model API**: Currently preview/internal in VS 2022. The extension falls back to prompt composition until the API stabilizes.
- **No label support for extension types**: Label files can be read/created for base types, but not yet for extensions.
- **Single model scope**: Context pipeline doesn't yet distinguish between multiple models in a multi-model solution.
- **AccessGrant on security entry points**: The `grant` field on `entryPoints` is not yet fully supported — `EntryPointType` and `ObjectName` are set, but granular access grants (Read, Update, Delete, Invoke) require a future enhancement.
- **UDE config auto-detection depends on D365FO extension**: The registry key and JSON config are written by Microsoft's D365FO VS extension. If that extension is not installed or not yet configured, set Environment Type to Onebox or CHE (or set Metadata Roots manually).
