# Architecture

> Detailed architecture documentation for the X++ AI Copilot Companion.
> For a quick overview, see [README.md](README.md).

---

## System Architecture

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
│  │  16 tools exposed via Model Context Protocol (SSE/HTTP)        │ │
│  │  ┌──────────────────┬──────────────────┬────────────────────┐ │ │
│  │  │ xpp_create_object│ xpp_read_object  │ xpp_update_object  │ │ │
│  │  │ xpp_find_object  │ xpp_list_objects │ xpp_find_references│ │ │
│  │  │ xpp_get_model_info│ xpp_list_models │ xpp_read_label     │ │ │
│  │  │ xpp_create_label │ xpp_add_to_proj  │ xpp_list_proj_items│ │ │
│  │  │ xpp_get_environment│ xpp_search_docs│ xpp_validate_object│ │ │
│  │  │ xpp_get_object_  │                  │                    │ │ │
│  │  │   type_schema    │                  │                    │ │ │
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

---

## Key Design Decisions

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
| **Cascade timeout prevention (Bridge)** | When a `DispatchAction` call times out, the dispatch gate (`SemaphoreSlim`) is **held** until the hung task eventually completes. Subsequent requests fail fast with "bridge busy" (~3 s) instead of each one blocking for the full dispatch timeout and cascading failures across all remaining types. |
| **Cancelled-item skip (MCP queue)** | When a caller in the MCP server times out waiting for the serial queue, it marks the work item as cancelled. The queue worker skips cancelled items instead of sending a wasted HTTP call to the bridge. |

---

## MCP Server Data Flow

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

---

## MCP ↔ Bridge Reliability

The MCP server and MetaModel Bridge use several interlocking mechanisms to stay responsive even when individual MetaModel API calls hang or time out:

1. **Serial request queue** — `BridgeClient` feeds all outbound HTTP requests through a `BlockingCollection` with a single worker thread. This guarantees one-at-a-time execution and prevents concurrent VS main-thread marshalling from deadlocking.
2. **Dispatch gate with hold-on-timeout** — `MetaModelBridgeServer.DispatchAction` acquires a `SemaphoreSlim(1,1)` before marshalling to the main thread. If the call times out, the gate is **not released** until the hung task completes (via a `ContinueWith` callback). This prevents new requests from piling up behind a blocked main thread.
3. **Cancelled-item skip** — When the `BridgeClient.Call()` caller times out waiting for the queue, it sets a `Cancelled` flag on the work item. The queue worker checks this flag before making the HTTP call and skips cancelled items, avoiding wasted round-trips.
4. **Post-timeout cooldown** — After a timeout, `BridgeClient` sleeps for a configurable cooldown period to give the bridge time to recover before the next request.

---

## System Prompt Injection

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

## File Structure

```
vsix-companion/
├── Resources/
│   └── XppCopilotSystemPrompt.txt       # Embedded X++ language reference + tool instructions
│
├── MetaModel/                            # ── MetaModel Bridge (strongly-typed API layer) ──
│   ├── IMetaModelBridge.cs              # Bridge interface
│   ├── MetaModelBridge.cs               # IMetaModelService wrapper — Create, Read, Update
│   │                                    #   methods + ApplyProperties, ApplyEnumValues,
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
│   ├── ToolRouter.cs                    # 16 tool definitions with JSON Schema, routing logic
│   ├── BridgeClient.cs                  # HTTP client → MetaModel Bridge (port 21330),
│   │                                    #   serial queue (BlockingCollection), cancelled-item skip
│   ├── DocSearchHandler.cs              # xpp_search_docs handler (Microsoft Learn scraping)
│   ├── HtmlExtractor.cs                 # HTML → text extraction for doc search
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
