# Copilot Instructions for D365FO X++ Repositories (X++ AI Copilot Companion)

## Purpose

Use this file in repositories that work with Microsoft Dynamics 365 Finance and Operations (D365FO) and X++.
It is designed to work alongside the X++ AI Copilot Companion VSIX and its MCP tools.

This guidance is optimized for:
- Correct D365FO metadata operations
- Fast and reliable object discovery
- Safe extensibility patterns
- Accurate data model handling in a highly normalized ERP schema

## Operating Mode

For D365FO and X++ tasks, Copilot must run in MCP tool-first mode.

Primary rule:
- If an X++ MCP tool can perform the task, use that tool first.

## Tool-First Rules (Non-Negotiable)

Required:
- Read object metadata with xpp_read_object.
- Create objects with xpp_create_object.
- Modify objects with xpp_update_object.
- Search objects with xpp_find_object and xpp_list_objects.
- Analyze usage impact with xpp_find_references.
- Validate modifications with xpp_validate_object after create and update operations.

Forbidden:
- Do not read raw metadata XML from disk for understanding objects.
- Do not write or edit metadata XML directly.
- Do not ask users to manually create D365FO metadata files when tools can do it.
- Do not output fake tool invocation markup (for example, invoke tags or pseudo XML wrappers).

## Supported MCP Tools

Discovery and context:
- xpp_get_environment
- xpp_list_models
- xpp_get_model_info
- xpp_find_object
- xpp_list_objects
- xpp_find_references

Metadata CRUD:
- xpp_read_object
- xpp_create_object
- xpp_update_object
- xpp_delete_object
- xpp_validate_object

Project and labels:
- xpp_add_to_project
- xpp_list_project_items
- xpp_read_label
- xpp_create_label

Documentation:
- xpp_search_docs

## JSON-Only Payload Contract

All MCP tool payloads must be JSON.

Allowed formats:
- declaration and methods: plain X++ source strings
- properties, fields, indexes, fieldGroups, relations, enumValues, entryPoints: JSON objects or arrays

Disallowed formats:
- XML fragments
- CDATA wrappers
- metadataXml payloads

Recovery rule:
- If a tool rejects payload format, convert to plain JSON plus plain X++ strings and retry.

## Invocation Correctness

Copilot must:
- Use native MCP tool calls, not textual fake calls.
- Match parameter names exactly to schema.
- Supply required parameters before calling.
- Return actionable errors when required parameters are missing.

Copilot must not:
- Claim a tool was called if it was not called.
- Return pseudo invocations as output.

## Fast Search and Impact Pattern

Always prefer this two-step pattern:
1. Locate target object with xpp_find_object or xpp_list_objects.
2. Expand context with:
   - xpp_find_references for usage graph
   - xpp_list_objects filtered by modelName for local related objects

Performance guidance:
- Provide objectType whenever known.
- Use bounded maxResults and increase only when needed.
- Avoid global broad scans once model context is known.

## Required Execution Workflow

### Phase 1: Gather

- Call xpp_get_environment when context is unclear.
- Read all relevant objects with xpp_read_object.
- For data logic, read every table in the join chain and inspect relations.
- Use xpp_search_docs when framework behavior or syntax is uncertain.

### Phase 2: Plan

Before substantial code generation, provide a numbered plan listing:
- Object type
- Object name
- Purpose
- Target model

### Phase 3: Placement

Confirm before implementation:
- Ownership (standard Microsoft, third-party, or custom)
- Correct model and package
- Required dependencies
- Extension vs direct update strategy

### Phase 4: Implement and Validate

Implement in dependency order:
- Shared types
- Data objects
- Logic objects
- UI and security artifacts

After every create or update:
- Run xpp_validate_object
- Fix mismatches before reporting completion

## Ownership and Extensibility Rules

Ownership is the first decision.

- Standard Microsoft objects: do not modify directly.
- Third-party or foreign ISV objects: do not modify directly.
- Custom objects in your model: direct updates are allowed.

When changing behavior of non-owned objects, use extensibility mechanisms:
- Metadata extensions
- Chain of Command (CoC)
- Event handlers
- Extension methods
- Plug-in or factory patterns where applicable

## D365FO Data Modeling Rules

D365FO data is deeply normalized. Never infer join paths from names alone.

For multi-table logic:
- Read each table object involved.
- Confirm relation owner and constraint direction.
- Build joins from discovered FK to PK mappings only.

Date-effective tables:
- Detect ValidTimeState behavior.
- Use validtimestate when needed to avoid silent now filtering errors.

## Code Generation Quality Rules

- Provide complete implementations (no TODO placeholders as final code).
- Preserve existing behavior unless change is explicitly requested.
- Avoid speculative API usage; verify uncertain APIs with xpp_search_docs.
- Do not report success unless tool responses and validation indicate success.

## X++ Formatting and Indentation Rules

All generated X++ must follow consistent, production-safe formatting.

Indentation:
- Use 4 spaces per indentation level.
- Do not use tabs.
- Keep indentation level consistent inside class, method, if/else, while, switch, and try/catch blocks.

Braces and layout:
- Put opening braces on a new line for classes, methods, conditionals, loops, and try/catch/finally.
- Align closing braces with the matching block declaration.
- Keep one blank line between methods.
- Keep no trailing whitespace on lines.

Method formatting:
- Ensure method signatures and bodies are fully indented.
- For multiline parameter lists, align continuation lines with one extra indentation level.
- Keep variable declarations and statements aligned with their block indentation.

Control flow formatting:
- Keep if/else chains vertically aligned.
- Keep switch/case indentation consistent:
   - case labels at one indentation level inside switch
   - statements inside each case one level deeper

Output quality gate:
- Before returning generated code, perform a formatting self-check.
- If indentation is inconsistent, reformat before returning.
- Never return mixed tab/space indentation.

## Recommended Task Playbooks

Read an existing object:
1. xpp_find_object or xpp_list_objects
2. xpp_read_object

Impact analysis before refactor:
1. xpp_find_object
2. xpp_find_references
3. Read top referencing objects

Create a new object:
1. Read a similar object
2. Create via xpp_create_object
3. Validate via xpp_validate_object
4. Add to project if needed

Update an object:
1. Read current object
2. Update with deltas via xpp_update_object
3. Validate and iterate until clean

## Error Handling Expectations

When blocked, Copilot should:
- Surface the exact failing tool and parameter context
- Explain why it failed in D365FO terms
- Propose the next corrected tool call
- Retry with corrected payloads when appropriate

When tools are unavailable, Copilot should:
- State that tool access is required for safe D365FO metadata operations
- Avoid falling back to direct metadata XML manipulation

## Security and Safety

- Treat delete operations as destructive and require explicit user intent.
- Avoid exposing secrets or credentials in outputs.
- Keep changes scoped to the requested task.

## Response Style Expectations

Copilot responses should be:
- Concise but technically precise
- Explicit about which tools were used and outcomes
- Clear about assumptions, blockers, and validation status

Copilot must never output:
- Fake MCP invocation syntax
- XML wrappers for MCP payloads
- Instructions to manually edit D365FO metadata XML

## Compatibility Note

This file is intended for teams using the X++ AI Copilot Companion VSIX.
If tool names evolve in future releases, update this file to match shipped MCP schemas.
