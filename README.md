# X++ AI Copilot Companion

A Visual Studio 2022 extension (VSIX) + MCP server that gives GitHub Copilot full D365FO X++ awareness — language knowledge, project context, and **14 metadata tools** that create, read, update, delete, search, and manage X++ objects through the official MetaModel API.

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

### 1. MCP Server with 14 Metadata Tools

A standalone MCP (Model Context Protocol) server (`XppMcpServer.exe`) exposes 14 tools that Copilot can call directly. These tools use the D365FO MetaModel API — the **same API that Visual Studio uses internally** — to manipulate metadata with full type safety.

| Tool | Description |
|---|---|
| `xpp_create_object` | Create any of 32 object types with strongly-typed JSON metadata |
| `xpp_read_object` | Read object declaration, methods, and typed metadata (properties, fields, indexes, etc.) |
| `xpp_update_object` | Update declaration, methods, or metadata on existing custom objects |
| `xpp_delete_object` | Delete a custom-model object |
| `xpp_find_object` | Search for objects by name pattern and type |
| `xpp_list_objects` | List all objects of a given type in a model |
| `xpp_get_model_info` | Get model descriptor (layer, references, dependencies) |
| `xpp_list_models` | List all installed models |
| `xpp_read_label` | Read a label by ID and language |
| `xpp_create_label` | Create or update a label |
| `xpp_add_to_project` | Add an existing object to the active VS project |
| `xpp_list_project_items` | List items in the active VS project |
| `xpp_get_environment` | Get D365FO environment info (metadata folders, active model) |
| `xpp_search_docs` | Search Microsoft D365FO documentation |

#### Supported Object Types (32)

**20 base types** created via the typed MetaModel API:
AxClass, AxTable, AxForm, AxEdt, AxEnum, AxMenuItemDisplay, AxMenuItemOutput, AxMenuItemAction, AxQuery, AxView, AxDataEntityView, AxSecurityPrivilege, AxSecurityDuty, AxSecurityRole, AxService, AxServiceGroup, AxMap, AxMenu, AxTile, AxConfigurationKey

**12 extension types** created via XML serialization (no typed Create API exists):
AxTableExtension, AxFormExtension, AxEnumExtension, AxEdtExtension, AxViewExtension, AxMenuExtension, AxMenuItemDisplayExtension, AxMenuItemOutputExtension, AxMenuItemActionExtension, AxQuerySimpleExtension, AxSecurityDutyExtension, AxSecurityRoleExtension

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

```
┌──────────────────────────────────────────────────────────────────────┐
│  Visual Studio 2022 + D365FO Extension                               │
│                                                                      │
│  ┌──────────────────────────┐   ┌─────────────────────────────────┐ │
│  │  XppCopilotPackage       │   │  Tools > Options                │ │
│  │  (AsyncPackage)          │   │  > X++ AI Copilot               │ │
│  │                          │   │  ┌───────────────────────────┐  │ │
│  │  Registers:              │   │  │ XppCopilotOptionsPage     │  │ │
│  │  - 5 menu commands       │   │  │ - Metadata roots          │  │ │
│  │  - Options page          │   │  │ - Token budgets           │  │ │
│  │  - Auto-load on sln      │   │  │ - Selection params        │  │ │
│  │  - MCP registration      │   │  └───────────────────────────┘  │ │
│  └─────────┬────────────────┘   └─────────────────────────────────┘ │
│             │                                                        │
│             ▼                                                        │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │  MCP Server (XppMcpServer.exe — port 21329)                    │ │
│  │                                                                │ │
│  │  14 tools exposed via Model Context Protocol (SSE/HTTP)        │ │
│  │  ┌──────────────────┬──────────────────┬────────────────────┐ │ │
│  │  │ xpp_create_object│ xpp_read_object  │ xpp_update_object  │ │ │
│  │  │ xpp_delete_object│ xpp_find_object  │ xpp_list_objects   │ │ │
│  │  │ xpp_get_model_info│ xpp_list_models │ xpp_read_label     │ │ │
│  │  │ xpp_create_label │ xpp_add_to_proj  │ xpp_list_proj_items│ │ │
│  │  │ xpp_get_environment│ xpp_search_docs│                    │ │ │
│  │  └──────────────────┴──────────────────┴────────────────────┘ │ │
│  │                                                                │ │
│  │  ToolRouter.cs  → Tool definitions, JSON schema, routing      │ │
│  │  BridgeClient.cs → HTTP calls to MetaModel Bridge (port 21330)│ │
│  └────────────────────────────────┬───────────────────────────────┘ │
│                                    │ HTTP (localhost:21330)           │
│                                    ▼                                 │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │  MetaModel Bridge (in-process HTTP server — port 21330)        │ │
│  │                                                                │ │
│  │  MetaModelBridgeServer.cs → JSON request/response handling     │ │
│  │  MetaModelBridge.cs       → IMetaModelService wrapper          │ │
│  │  MetaModelContracts.cs    → Strongly-typed DTOs                │ │
│  │                                                                │ │
│  │  ┌─────────────────────────────────────────────────────────┐  │ │
│  │  │  Strongly-Typed Flow (no XML in the data path):         │  │ │
│  │  │                                                         │  │ │
│  │  │  JSON params → Parse → Typed DTOs → ApplyProperties()   │  │ │
│  │  │  → ApplyEnumValues/Fields/Indexes/Relations/...         │  │ │
│  │  │  → IMetaModelService.Create*/Update*/Save*              │  │ │
│  │  │                                                         │  │ │
│  │  │  IMetaModelService.Get* → Extract* methods              │  │ │
│  │  │  → Typed DTOs → JSON response                           │  │ │
│  │  └─────────────────────────────────────────────────────────┘  │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                                                                      │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │  Core Pipeline                                                 │ │
│  │                                                                │ │
│  │  XppInstructionsProvider ──► Embedded X++ system prompt        │ │
│  │         │                                                      │ │
│  │         ▼                                                      │ │
│  │  XppContextPipelineService                                     │ │
│  │  │  - Crawls metadata files for context scoring                │ │
│  │  │  - Token-based scoring (query overlap, active file)         │ │
│  │  │  - Min quota per source kind (custom vs. MS reference)      │ │
│  │  │  - Returns XppContextBundle                                 │ │
│  │  │                                                             │ │
│  │  ▼                                                             │ │
│  │  CopilotContextBridge                                          │ │
│  │  │  - Prepends X++ instructions to context payload             │ │
│  │  │  - Truncates to 24K char hard limit                         │ │
│  │  │                                                             │ │
│  │  ▼                                                             │ │
│  │  CopilotLanguageModelService (IAiCodeGenerationService)        │ │
│  │     - Composes system prompt + context + user request          │ │
│  │     - Calls VS Copilot language model API (when stable)        │ │
│  │     - Falls back to prompt for manual Copilot Chat use         │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                                                                      │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │  VS Integration Layer                                          │ │
│  │                                                                │ │
│  │  VisualStudioSessionService (IVisualStudioSessionService)      │ │
│  │  - Get/replace active document text (DTE + TextDocument)       │ │
│  │  - Get solution directory                                      │ │
│  │  - Add files to active project                                 │ │
│  │                                                                │ │
│  │  ServiceLocator → MEF service resolution                       │ │
│  │  OutputPane     → "X++ AI Copilot" output window               │ │
│  │  PromptDialog   → VS-themed input dialog                       │ │
│  └────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

| Decision | Rationale |
|---|---|
| **MCP server + MetaModel Bridge** | Copilot calls tools via MCP (port 21329), which forwards to the MetaModel Bridge (port 21330) running in the VSIX process. This gives Copilot direct access to the same `IMetaModelService` API that Visual Studio uses. |
| **Strongly-typed JSON (not XML)** | All metadata flows use typed DTOs serialized as JSON. The original XML-based approach (`XmlSerializer.Deserialize` on LLM-generated XML fragments) silently failed for all types. Typed JSON with automatic type coercion (string → bool/int/enum) is reliable and round-trippable. |
| **Round-trip format consistency** | Read results return the same JSON structure that create/update accept. Copilot can read an object and use its metadata directly as a template. |
| **Embedded resource for X++ instructions** | Instructions travel with the VSIX binary. No dependency on the developer's repo containing a `.github/copilot-instructions.md` file. |
| **Old-style csproj (not SDK-style)** | Required for VSIX projects with VSCT command tables and `Microsoft.VsSDK.targets`. |
| **Token budget system** | Prevents context from consuming the entire AI token window. Configurable via Options page. |
| **D365FO config auto-detection** | Reads the active configuration from the registry (`CurrentMetadataConfig`) and the corresponding JSON file under `%LOCALAPPDATA%\Microsoft\Dynamics365\XPPConfig`. Zero-config for developers who already have D365FO set up. |
| **Serial request queue (BridgeClient)** | All MCP→Bridge HTTP calls are funnelled through a `BlockingCollection` with a single worker thread. This prevents concurrent requests from racing on the VS main thread and causing deadlocks. |
| **Cascade timeout prevention (Bridge)** | When a `DispatchAction` call times out (e.g. a hung `DeleteTile`), the dispatch gate (`SemaphoreSlim`) is **held** until the hung task eventually completes. Subsequent requests fail fast with "bridge busy" (~3 s) instead of each one blocking for the full dispatch timeout and cascading failures across all remaining types. |
| **Cancelled-item skip (MCP queue)** | When a caller in the MCP server times out waiting for the serial queue, it marks the work item as cancelled. The queue worker skips cancelled items instead of sending a wasted HTTP call to the bridge. |

---

## File Structure

```
vsix-companion/
├── Resources/
│   └── XppCopilotSystemPrompt.txt       # Embedded X++ language reference + tool instructions
│
├── MetaModel/                            # ── MetaModel Bridge (strongly-typed API layer) ──
│   ├── IMetaModelBridge.cs              # Bridge interface
│   ├── MetaModelBridge.cs               # IMetaModelService wrapper — 20 Create, Update, Read,
│   │                                    #   Delete methods + ApplyProperties, ApplyEnumValues,
│   │                                    #   ApplyFields, ApplyIndexes, ApplyRelations,
│   │                                    #   ExtractProperties, ExtractFields, etc.
│   ├── MetaModelBridgeServer.cs         # HTTP server (port 21330) — JSON parsing/serialization,
│   │                                    #   request routing to MetaModelBridge, dispatch gate
│   │                                    #   with hold-on-timeout cascade prevention
│   └── MetaModelContracts.cs            # Strongly-typed DTOs: CreateObjectRequest,
│                                        #   UpdateObjectRequest, ReadObjectResult, EnumValueDto,
│                                        #   FieldDto, IndexDto, FieldGroupDto, RelationDto, etc.
│
├── mcp-server/                           # ── MCP Server (standalone exe) ──
│   ├── Program.cs                       # Entry point — SSE/HTTP MCP server on port 21329
│   ├── ToolRouter.cs                    # 14 tool definitions with JSON Schema, routing logic
│   ├── BridgeClient.cs                  # HTTP client → MetaModel Bridge (port 21330),
│   │                                    #   serial queue (BlockingCollection), cancelled-item skip
│   ├── DocSearchHandler.cs              # xpp_search_docs handler (Microsoft Learn scraping)
│   ├── HtmlExtractor.cs                # HTML → text extraction for doc search
│   ├── JsonHelpers.cs                   # JSON utilities
│   ├── McpLogger.cs                     # MCP protocol logging
│   └── XppMcpServer.csproj             # .NET Framework 4.8 console app project
│
├── Core Pipeline
│   ├── XppInstructionsProvider.cs       # Loads embedded resource, caches
│   ├── XppContextPipelineService.cs     # Crawls + scores metadata for context
│   ├── IXppContextPipelineService.cs    # Pipeline interface
│   ├── XppContextBundle.cs              # Context data model + rendering
│   ├── CopilotContextBridge.cs          # Merges instructions + context
│   ├── CopilotContextProviderAdapter.cs # Copilot provider wiring
│   ├── CopilotProviderContract.cs       # ICopilotContextBridge interface
│   └── TokenEstimator.cs               # ~4 chars/token estimation
│
├── AI Service
│   ├── IAiCodeGenerationService.cs      # Request/response models + interface
│   └── CopilotLanguageModelService.cs   # VS Copilot API + fallback
│
├── Object Creation
│   ├── IXppObjectCreationService.cs     # Request model + interface
│   └── MetaModelObjectCreationService.cs # Routes creates through MetaModel Bridge
│
├── VS Integration
│   ├── IVisualStudioSessionService.cs   # DTE abstraction interface
│   ├── VisualStudioSessionService.cs    # DTE implementation
│   ├── ServiceLocator.cs               # MEF service resolution
│   ├── OutputPane.cs                   # Output window pane
│   └── PromptDialog.cs                # VS-themed input dialog
│
├── UI / Commands
│   ├── XppCopilotPackage.cs            # AsyncPackage (entry point)
│   ├── XppCopilotCommands.vsct         # Menu command table
│   ├── PackageGuids.cs                 # GUIDs + command IDs
│   ├── RefreshContextCommand.cs        # Tools > X++ AI: Refresh Context
│   ├── GenerateCodeCommand.cs          # Tools > X++ AI: Generate Code
│   ├── CreateObjectCommand.cs          # Tools > X++ AI: Create Object
│   ├── RegisterMcpCommand.cs           # Tools > X++ AI: Register MCP Server
│   └── McpDiagnosticsCommand.cs        # Tools > X++ AI: MCP Diagnostics
│
├── Settings
│   ├── XppCopilotOptionsPage.cs        # Tools > Options page
│   ├── XppUiSettings.cs                # Settings data model
│   ├── XppUiSettingsService.cs         # Reads from options page + D365 auto-detect
│   ├── IXppUiSettingsService.cs        # Settings interface
│   └── DynamicsConfigurationReader.cs  # Reads active D365FO config (registry + JSON)
│
├── Workflow
│   ├── XppCopilotWorkflowService.cs    # End-to-end orchestration
│   ├── IXppCopilotUiController.cs      # UI controller interface
│   └── XppCopilotUiController.cs       # UI controller implementation
│
├── Version.props                        # Auto-generated version from git commits
├── auto-version.ps1                     # Conventional Commits → version bumper
├── XppAiCopilotCompanion.csproj        # VSIX project file
└── source.extension.vsixmanifest       # VSIX manifest
```

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

Once the MCP server is registered, Copilot Chat in Visual Studio can call the 14 `xpp_*` tools directly. Examples:

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

## How It Works

### MCP Server Data Flow

```
Copilot Chat                    MCP Server (21329)           MetaModel Bridge (21330)
    │                               │                               │
    │  "create AxEnum MyStatus"     │                               │
    │ ─────────────────────────────>│                               │
    │                               │  POST /create                 │
    │                               │  { objectType: "AxEnum",      │
    │                               │    objectName: "MyStatus",    │
    │                               │    properties: {Label:"..."},  │
    │                               │    enumValues: [{name:"None", │
    │                               │      value:0, label:"None"}]  │
    │                               │  }                            │
    │                               │ ─────────────────────────────>│
    │                               │                               │  MetaModelBridge.CreateEnum()
    │                               │                               │  → ApplyProperties()
    │                               │                               │  → ApplyEnumValues()
    │                               │                               │  → IMetaModelService.CreateEnum()
    │                               │            200 OK             │
    │                               │ <─────────────────────────────│
    │       tool result (JSON)      │                               │
    │ <─────────────────────────────│                               │
```

### MCP ↔ Bridge Reliability

The MCP server and MetaModel Bridge use several interlocking mechanisms to stay responsive even when individual MetaModel API calls hang or time out:

1. **Serial request queue** — `BridgeClient` feeds all outbound HTTP requests through a `BlockingCollection` with a single worker thread. This guarantees one-at-a-time execution and prevents concurrent VS main-thread marshalling from deadlocking.
2. **Dispatch gate with hold-on-timeout** — `MetaModelBridgeServer.DispatchAction` acquires a `SemaphoreSlim(1,1)` before marshalling to the main thread. If the call times out, the gate is **not released** until the hung task completes (via a `ContinueWith` callback). This prevents new requests from piling up behind a blocked main thread.
3. **Cancelled-item skip** — When the `BridgeClient.Call()` caller times out waiting for the queue, it sets a `Cancelled` flag on the work item. The queue worker checks this flag before making the HTTP call and skips cancelled items, avoiding wasted round-trips.
4. **Post-timeout cooldown** — After a timeout, `BridgeClient` sleeps for a configurable cooldown period to give the bridge time to recover before the next request.

These layers mean that a single hung API call (e.g. `DeleteTile`) no longer cascades into timeouts for every subsequent request.

### System Prompt Injection

The file `Resources/XppCopilotSystemPrompt.txt` is compiled as an **embedded resource** in the assembly. At runtime:

1. `XppInstructionsProvider` loads it via `Assembly.GetManifestResourceStream()` and caches it
2. `CopilotContextBridge` prepends it to every context payload
3. `XppCopilotWorkflowService` sets it as `AiCodeRequest.SystemPrompt` on every AI request

The prompt is structured around an **ownership-first decision framework**:

1. **Critical Rules** — Never modify standard objects; understand model ownership
2. **Tool-First Mandate** — Always use `xpp_*` tools; never read/write metadata files directly
3. **Models & Packages** — Model descriptor format, dependency rules, package relationships
4. **Five Extensibility Mechanisms** — Each with code examples and when-to-use guidance
5. **X++ Language Reference** — Data types, classes, data access, transactions
6. **Object Types & Properties** — Valid property values and JSON parameter formats
7. **Naming Conventions & Best Practices**

---

## Limitations

- **VS Copilot language model API**: Currently preview/internal in VS 2022. The extension falls back to prompt composition until the API stabilizes.
- **No label support for extension types**: Label files can be read/created for base types, but not yet for extensions.
- **Single model scope**: Context pipeline doesn't yet distinguish between multiple models in a multi-model solution.
- **AccessGrant on security entry points**: The `grant` field on `entryPoints` is not yet fully supported — `EntryPointType` and `ObjectName` are set, but granular access grants (Read, Update, Delete, Invoke) require a future enhancement.
- **Config auto-detection depends on D365FO extension**: The registry key and JSON config are written by Microsoft's D365FO VS extension. If that extension is not installed or configured, paths must be set manually.
