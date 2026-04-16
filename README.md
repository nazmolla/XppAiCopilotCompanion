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

### 1. MCP Server with 16 Metadata Tools

A standalone MCP (Model Context Protocol) server (`XppMcpServer.exe`) exposes 16 tools that Copilot can call directly. These tools use the D365FO MetaModel API — the **same API that Visual Studio uses internally** — to manipulate metadata with full type safety.

| Tool | Description |
|---|---|
| `xpp_create_object` | Create any of 19 base object types with strongly-typed JSON metadata |
| `xpp_read_object` | Read object declaration, methods, and typed metadata (properties, fields, indexes, etc.) |
| `xpp_update_object` | Update declaration, methods, or metadata on existing custom objects |
| `xpp_validate_object` | Validate that an object exists, is in the project, and has the expected metadata |
| `xpp_find_object` | Search for objects by name pattern and type |
| `xpp_list_objects` | List all objects of a given type in a model |
| `xpp_find_references` | Query the cross-reference database for all objects that use/reference a given object |
| `xpp_get_object_type_schema` | Return the generated JSON schema for a metadata type via reflective type inspection |
| `xpp_get_model_info` | Get model descriptor (layer, references, dependencies) |
| `xpp_list_models` | List all installed models with editability status |
| `xpp_read_label` | Read a label by ID and language |
| `xpp_create_label` | Create or update a label |
| `xpp_add_to_project` | Add an existing object to the active VS project |
| `xpp_list_project_items` | List items in the active VS project |
| `xpp_get_environment` | Get D365FO environment info (metadata folders, active model) |
| `xpp_search_docs` | Search Microsoft D365FO documentation |

#### Supported Object Types (19)

All 19 base types are created via the typed MetaModel API:

| Object Type | Read | Create | Update | Validate | Status |
|---|:---:|:---:|:---:|:---:|---|
| AxClass | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxTable | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxView | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxDataEntityView | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxEdt | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxEnum | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxForm | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxMenu | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxMenuItemAction | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxMenuItemDisplay | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxMenuItemOutput | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxQuery | ✅ | ⚠️ | ⚠️ | — | Create succeeds but metadata apply may fail (MetaModel API ambiguity) |
| AxSecurityPrivilege | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxSecurityDuty | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxSecurityRole | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxService | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxServiceGroup | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxConfigurationKey | ✅ | ✅ | ✅ | ✅ | Fully tested |
| AxTile | ✅ | ✅ | ✅ | ✅ | Fully tested |

**18 of 19 types pass full round-trip testing** (read → create → validate → compare). AxQuery read works; create+update has a known MetaModel API reflection ambiguity with complex query metadata.

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

- Visual Studio 2022 (17.0+) — Community, Professional, or Enterprise
- .NET Framework 4.8
- Visual Studio SDK workload (for building from source)
- D365FO development tools extension (for use on a dev VM)

### Build from Source

1. Clone this repository
2. Open `DynDevTools.sln` in Visual Studio 2022
3. Ensure the **Visual Studio extension development** workload is installed
4. Build the solution (`Ctrl+Shift+B`)
5. The VSIX will be in `vsix-companion/bin/Release/XppAiCopilotCompanion.vsix`
6. The MCP server will be in `vsix-companion/mcp-server/bin/Release/XppMcpServer.exe`

### Install the VSIX

1. Close all Visual Studio instances
2. Double-click `XppAiCopilotCompanion.vsix`
3. Follow the installer prompts
4. Restart Visual Studio

### Deploy to a D365FO Dev VM

Copy the `.vsix` file to the dev VM and double-click to install. The extension will activate automatically when a solution is opened. The MCP server is started and stopped automatically by the VSIX.

---

## Usage

### Configure

Metadata paths are **auto-detected** from the active D365FO configuration. The extension reads:

1. **Registry** — `HKCU\Software\Microsoft\Dynamics\AX7\Development\Configurations` → `CurrentMetadataConfig` (path to the active JSON config file) and `FrameworkDirectory` (fallback)
2. **JSON config file** — under `%LOCALAPPDATA%\Microsoft\Dynamics365\XPPConfig` → `ModelStoreFolder` (custom metadata) and `ReferencePackagesPaths` (reference metadata)

This means **zero configuration is needed** when the D365FO extension is properly set up (Extensions > Dynamics 365 > Configure Metadata).

To override, go to **Tools > Options > X++ AI Copilot > General** and set explicit paths. When the options page fields are empty, the auto-detected paths are used.

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

| Setting | Default | Description |
|---|---|---|
| Custom Metadata Roots | *(auto-detect)* | Semicolon-separated paths to your model's metadata directories. Leave empty to auto-detect from the active D365FO configuration. |
| Reference (MS) Metadata Roots | *(auto-detect)* | Semicolon-separated paths to Microsoft reference metadata. Leave empty to auto-detect from the active D365FO configuration. |
| Max Context Tokens | 6000 | Total token budget for the AI context payload |
| Max Snippet Tokens Per Object | 350 | Token limit per individual object in context |
| Max Active Document Tokens | 1000 | Token budget for the currently open file |
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
- **Config auto-detection depends on D365FO extension**: The registry key and JSON config are written by Microsoft's D365FO VS extension. If that extension is not installed or configured, paths must be set manually.
