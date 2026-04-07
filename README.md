# X++ AI Copilot Companion

A Visual Studio 2022 extension (VSIX) that teaches GitHub Copilot how to write X++ for Dynamics 365 Finance & Operations.

---

## Problem Statement

GitHub Copilot has no built-in knowledge of X++. When a D365FO developer uses Copilot in Visual Studio, it:

- Does not understand X++ syntax, data types, or keywords (`ttsbegin`, `next`, `select forupdate`, etc.)
- Has no concept of the five D365FO extensibility mechanisms (metadata extensions, CoC, events, extension methods, plug-in/factory)
- Does not know that Microsoft standard objects are read-only and must be extended, never modified directly
- Has no awareness of model descriptors, package dependencies, or ownership rules that determine whether to extend or modify directly
- Cannot reason about where to place new objects or which model/package they belong in
- Has no awareness of the active project's model, metadata structure, or naming conventions

The result is that Copilot generates invalid or dangerous code — modifying base objects, missing `next` calls in CoC, using C# syntax instead of X++, creating extensions when direct modification is appropriate, or failing to check model dependencies.

## Solution

This VSIX extension solves the problem at two levels:

### 1. Embedded X++ Knowledge (System Prompt Injection)

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
- **Object type properties**: Semantic descriptions of table, field, index, relation, EDT, enum, form extension properties (no XML templates — the VSIX tooling handles metadata generation)
- **Transaction control, exception handling, config key guards**
- **Naming conventions and best practices**

This prompt is injected into **every** Copilot interaction — both in the context payload and the AI service system message. It travels with the VSIX binary, so it works in any D365FO project regardless of the repository.

### 2. Dynamic Project Context Pipeline

The extension crawls the open solution's metadata XML files in real time, scores them by relevance to the developer's current task, and feeds a token-budgeted selection into Copilot's context. This gives Copilot awareness of:

- What objects exist in the current project
- The active document's content
- Related classes, tables, EDTs, and enums
- Whether objects are custom (editable) or Microsoft reference (extend only)

---

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│  Visual Studio 2022 + D365FO Extension                          │
│                                                                  │
│  ┌─────────────────────────┐   ┌──────────────────────────────┐ │
│  │  XppCopilotPackage      │   │  Tools > Options             │ │
│  │  (AsyncPackage)         │   │  > X++ AI Copilot            │ │
│  │                         │   │  ┌────────────────────────┐  │ │
│  │  Registers:             │   │  │ XppCopilotOptionsPage  │  │ │
│  │  - 3 menu commands      │   │  │ - Metadata roots       │  │ │
│  │  - Options page         │   │  │ - Token budgets        │  │ │
│  │  - Auto-load on sln     │   │  │ - Selection params     │  │ │
│  └─────────┬───────────────┘   │  └────────────────────────┘  │ │
│             │                   └──────────────────────────────┘ │
│             ▼                                                    │
│  ┌──────────────────────────────────────────────────┐           │
│  │  Tools Menu Commands                              │           │
│  │                                                   │           │
│  │  X++ AI: Refresh Context                          │           │
│  │  → Builds context, shows in Output window         │           │
│  │                                                   │           │
│  │  X++ AI: Generate Code                            │           │
│  │  → Prompts for request → AI generates X++         │           │
│  │  → Applies to active document                     │           │
│  │                                                   │           │
│  │  X++ AI: Create Object                            │           │
│  │  → Prompts for type/name/description              │           │
│  │  → Creates metadata XML → Adds to project         │           │
│  └──────────────────┬───────────────────────────────┘           │
│                      │                                           │
│                      ▼                                           │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Core Pipeline                                             │  │
│  │                                                            │  │
│  │  XppInstructionsProvider ──► Embedded X++ system prompt    │  │
│  │         │                                                  │  │
│  │         ▼                                                  │  │
│  │  XppContextPipelineService ──► Crawls metadata XMLs       │  │
│  │  │  - Parses AxClass/AxTable/AxEdt/AxEnum                 │  │
│  │  │  - Token-based scoring (query overlap, active file)    │  │
│  │  │  - Min quota per source kind (custom vs. MS reference) │  │
│  │  │  - Returns XppContextBundle                            │  │
│  │  │                                                        │  │
│  │  ▼                                                        │  │
│  │  CopilotContextBridge                                     │  │
│  │  │  - Prepends X++ instructions to context payload        │  │
│  │  │  - Truncates to 24K char hard limit                    │  │
│  │  │                                                        │  │
│  │  ▼                                                        │  │
│  │  CopilotLanguageModelService (IAiCodeGenerationService)   │  │
│  │  │  - Composes system prompt + context + user request     │  │
│  │  │  - Calls VS Copilot language model API (when stable)   │  │
│  │  │  - Falls back to prompt for manual Copilot Chat use    │  │
│  │  │                                                        │  │
│  │  ▼                                                        │  │
│  │  XppObjectCreationService                                 │  │
│  │     - Generates D365FO metadata XML                       │  │
│  │     - Writes to disk under correct Ax* subfolder          │  │
│  │     - Adds to active VS project via DTE                   │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  VS Integration Layer                                      │  │
│  │                                                            │  │
│  │  VisualStudioSessionService (IVisualStudioSessionService)  │  │
│  │  - Get/replace active document text (DTE + TextDocument)   │  │
│  │  - Get solution directory                                  │  │
│  │  - Add files to active project                             │  │
│  │                                                            │  │
│  │  ServiceLocator         → MEF service resolution           │  │
│  │  OutputPane             → "X++ AI Copilot" output window   │  │
│  │  PromptDialog           → VS-themed input dialog           │  │
│  └───────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

| Decision | Rationale |
|---|---|
| **Embedded resource for X++ instructions** | Instructions travel with the VSIX binary. No dependency on the developer's repo containing a `.github/copilot-instructions.md` file. |
| **Old-style csproj (not SDK-style)** | Required for VSIX projects with VSCT command tables and `Microsoft.VsSDK.targets`. |
| **Token budget system** | Prevents context from consuming the entire AI token window. Configurable via Options page. |
| **Fallback prompt composition** | The VS Copilot language model API (`ILanguageModelBroker`) is preview/internal. Until it stabilizes, the extension composes a full prompt that can be pasted into Copilot Chat. |
| **MEF exports for services** | `VisualStudioSessionService`, `XppUiSettingsService`, and `CopilotLanguageModelService` are resolved via VS's MEF composition container. Commands wire up the pipeline manually since it takes constructor parameters. |
| **D365FO config auto-detection** | Reads the active configuration from the registry (`CurrentMetadataConfig`) and the corresponding JSON file under `%LOCALAPPDATA%\Microsoft\Dynamics365\XPPConfig`. Zero-config for developers who already have D365FO set up. |

---

## File Structure

```
vsix-companion/
├── Resources/
│   └── XppCopilotSystemPrompt.txt    # Embedded X++ language reference
│
├── Core Pipeline
│   ├── XppInstructionsProvider.cs     # Loads embedded resource, caches
│   ├── XppContextPipelineService.cs   # Crawls + scores metadata XMLs
│   ├── XppContextBundle.cs            # Context data model + rendering
│   ├── CopilotContextBridge.cs        # Merges instructions + context
│   ├── CopilotContextProviderAdapter.cs # Copilot provider wiring
│   ├── CopilotProviderContract.cs     # ICopilotContextBridge interface
│   └── TokenEstimator.cs             # ~4 chars/token estimation
│
├── AI Service
│   ├── IAiCodeGenerationService.cs    # Request/response models + interface
│   └── CopilotLanguageModelService.cs # VS Copilot API + fallback
│
├── Object Creation
│   ├── IXppObjectCreationService.cs   # Request model + interface
│   └── XppObjectCreationService.cs    # Generates metadata XML, adds to project
│
├── VS Integration
│   ├── IVisualStudioSessionService.cs # DTE abstraction interface
│   ├── VisualStudioSessionService.cs  # DTE implementation
│   ├── ServiceLocator.cs             # MEF service resolution
│   ├── OutputPane.cs                 # Output window pane
│   └── PromptDialog.cs              # VS-themed input dialog
│
├── UI / Commands
│   ├── XppCopilotPackage.cs          # AsyncPackage (entry point)
│   ├── XppCopilotCommands.vsct       # Menu command table
│   ├── PackageGuids.cs               # GUIDs + command IDs
│   ├── RefreshContextCommand.cs      # Tools > X++ AI: Refresh Context
│   ├── GenerateCodeCommand.cs        # Tools > X++ AI: Generate Code
│   └── CreateObjectCommand.cs        # Tools > X++ AI: Create Object
│
├── Settings
│   ├── XppCopilotOptionsPage.cs      # Tools > Options page
│   ├── XppUiSettings.cs              # Settings data model
│   ├── XppUiSettingsService.cs       # Reads from options page + D365 auto-detect
│   ├── IXppUiSettingsService.cs      # Settings interface
│   └── DynamicsConfigurationReader.cs # Reads active D365FO config (registry + JSON)
│
├── Workflow
│   ├── XppCopilotWorkflowService.cs  # End-to-end orchestration
│   ├── IXppCopilotUiController.cs    # UI controller interface
│   └── XppCopilotUiController.cs     # UI controller implementation
│
├── XppAiCopilotCompanion.csproj      # VSIX project file
└── source.extension.vsixmanifest     # VSIX manifest
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
2. Open `vsix-companion/XppAiCopilotCompanion.csproj` in Visual Studio 2022
3. Ensure the **Visual Studio extension development** workload is installed
4. Build the solution (`Ctrl+Shift+B`)
5. The VSIX will be in `vsix-companion/bin/Debug/XppAiCopilotCompanion.vsix`

### Install the VSIX

1. Close all Visual Studio instances
2. Double-click `XppAiCopilotCompanion.vsix`
3. Follow the installer prompts
4. Restart Visual Studio

### Deploy to a D365FO Dev VM

Copy the `.vsix` file to the dev VM and double-click to install. The extension will activate automatically when a solution is opened.

---

## Usage

### Configure

Metadata paths are **auto-detected** from the active D365FO configuration. The extension reads:

1. **Registry** — `HKCU\Software\Microsoft\Dynamics\AX7\Development\Configurations` → `CurrentMetadataConfig` (path to the active JSON config file) and `FrameworkDirectory` (fallback)
2. **JSON config file** — under `%LOCALAPPDATA%\Microsoft\Dynamics365\XPPConfig` → `ModelStoreFolder` (custom metadata) and `ReferencePackagesPaths` (reference metadata)

This means **zero configuration is needed** when the D365FO extension is properly set up (Extensions > Dynamics 365 > Configure Metadata).

To override, go to **Tools > Options > X++ AI Copilot > General** and set explicit paths. When the options page fields are empty, the auto-detected paths are used.

Adjust token budget settings if needed (defaults work well for most scenarios).

### Menu Commands

All commands are under **Tools** in the main menu:

#### X++ AI: Refresh Context

Builds the context payload from the current solution and displays it in the **Output** window ("X++ AI Copilot" pane). Use this to verify what Copilot will see — the X++ instructions, active document, and relevant objects with their scores.

#### X++ AI: Generate Code

1. Prompts for a natural language description (e.g., "Create a CoC extension for CustTable.insert that validates the Name field")
2. Builds context from the open solution
3. Sends the X++ system prompt + context + your request to the AI service
4. Applies the generated code to the currently active document

#### X++ AI: Create Object

1. Prompts for object type (`AxClass`, `AxTable`, `AxEdt`, `AxEnum`, `AxForm`)
2. Prompts for object name
3. Prompts for a description of what the object should do
4. Generates a properly structured D365FO metadata XML file
5. Writes it to the correct `Ax*` subfolder
6. Adds it to the active VS project

### With Copilot Chat (Manual Fallback)

If the VS Copilot language model API is not available, the extension composes a full prompt and displays it in the Output window. Copy and paste it into Copilot Chat for the same result.

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

## How the X++ Instructions Are Injected

The file `Resources/XppCopilotSystemPrompt.txt` is compiled as an **embedded resource** in the assembly. At runtime:

1. `XppInstructionsProvider` loads it via `Assembly.GetManifestResourceStream()` and caches it
2. `CopilotContextBridge` prepends it to every context payload (so Copilot always sees it in the context window)
3. `XppCopilotWorkflowService` sets it as `AiCodeRequest.SystemPrompt` on every AI request (so direct API calls use it as the system message)

This ensures X++ knowledge is present in every interaction path — whether through the context provider, the direct AI service, or the fallback prompt.

### What the System Prompt Covers

The prompt is structured around an **ownership-first decision framework**:

1. **Critical Rules** — Never modify standard objects; understand model ownership
2. **Models & Packages** — Model descriptor format (`ModuleReferences`, `ModelModule`, `Layer`), dependency rules, package relationships
3. **Ownership Decision** — Step 0: who owns the target object? Step 1: where to place new objects. Step 2: pick the right extensibility mechanism
4. **Five Extensibility Mechanisms** — Each with code examples and when-to-use guidance:
   - Metadata extensions (table/form/enum/EDT)
   - Chain of Command (class, table, form, data source, data field, control, data entity, static methods)
   - Events & event handlers (pre/post handlers, table data events, form events, delegate subscriptions, EventHandlerResult)
   - Extension methods (`_Extension` suffix classes)
   - Plug-in/factory (SysExtension, SysPlugin, SysOperation)
5. **X++ Language Reference** — Data types, classes, data access, set-based operations, Query framework, transactions
6. **Object Types & Properties** — Semantic descriptions of all object types (table, field, index, relation, EDT, enum, form extension) with their valid property values — no XML templates since the tooling generates metadata
7. **Display & Edit Methods** — On tables, as extension methods, form-level
8. **Naming Conventions & Best Practices**

---

## Limitations

- **VS Copilot language model API**: Currently preview/internal in VS 2022. The extension falls back to prompt composition until the API stabilizes.
- **No label support**: Does not generate or manage D365FO label files.
- **Single model scope**: Context pipeline doesn't yet distinguish between multiple models in a multi-model solution.
- **Model descriptor not auto-parsed**: The system prompt teaches the AI about model descriptors, but the VSIX does not yet automatically read the active project's descriptor to provide concrete model context.
- **Config auto-detection depends on D365FO extension**: The registry key and JSON config are written by Microsoft's D365FO VS extension. If that extension is not installed or configured, paths must be set manually.
