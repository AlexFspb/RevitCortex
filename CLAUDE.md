# RevitCortex 2026 — Claude / MCP Guide

`AGENTS.md` is the canonical fork-specific development guide. This file summarizes the rules most relevant to Claude-style MCP operation and code work.

## Target

This fork supports **Autodesk Revit 2026 only** and targets **.NET 8**.

Do not use or recommend legacy R23/R24/R25/R27 build matrices for this fork.

The upstream experimental **Premium / License & Account** entitlement subsystem is not part of this fork. The upstream MIT software license remains unchanged in `LICENSE`.

## Runtime flow

```text
Claude / MCP client
  -> RevitCortex.Server
  -> local TCP / JSON-RPC bridge
  -> RevitCortex.Plugin in Revit 2026
  -> CortexRouter
  -> ICortexTool
  -> Revit API
```

The Revit plugin server is off by default; the user starts it with **Cortex Switch**.

## Session behavior

- Prefer dedicated tools.
- Reuse information already present in the conversation instead of re-querying Revit.
- Keep large model-data responses scoped with filters/limits/compact modes.
- For custom parameters, inspect a sample element first; never guess the exact parameter name.
- Use a new conversation/session when old large tool outputs make the context noisy.

## Tool selection

Use the narrowest dedicated tool available.

Examples:

- simple element search → `export_elements_data`
- complex logical filtering → `ai_element_filter`
- current view → `get_current_view_elements`
- spatial query → `get_elements_in_spatial_volume`
- single/few parameter writes → `set_element_parameters`
- same parameter/value on many elements → `bulk_modify_parameter_values`
- per-element differing values → `sync_csv_parameters`
- copy properties → `match_element_properties`
- quick clashes → `clash_detection`
- visual clash review → `workflow_clash_review`

## Destructive writes

Use preview-first behavior when supported:

1. call with `dryRun: true`;
2. inspect the proposed impact;
3. execute with `dryRun: false`;
4. respect Revit confirmation and transaction results.

Do not describe a rolled-back Revit transaction as successful.

## `send_code_to_revit`

Treat this tool as **last resort**. Use it when a dedicated tool does not cover the requested operation adequately.

For this fork:

- Revit 2026 / .NET 8 only;
- Roslyn execution path only;
- `EnableCodeExecution` must be enabled;
- sandbox validation remains mandatory;
- user-selected read-only mode and disabled-tool restrictions remain active;
- there is no Premium activation/license gate;
- each execution is audited;
- critical confirmation happens in Revit before execution.

Globals available to the script:

- `document`
- `uiDocument`
- `app`

For Revit 2026, use `ElementId.Value`.

Never use modal `Document.EditFamily` flows from the MCP external-event context.

## Critical confirmation and `Allow auto-run`

The Revit 2026 fork uses a dedicated critical confirmation window for custom C# execution.

- **Yes** approves immediately.
- **No** cancels.
- **Allow auto-run** enables a visible **3-second countdown**.
- At zero, the current script is approved automatically.
- The user can still click Yes or No while the countdown runs.
- Auto-run is stored only in process memory and resets when Revit closes.

Auto-run does not disable sandboxing, auditing, read-only mode, disabled-tool restrictions or code-execution permissions.

## Diagnostic reports

The ribbon **Diagnostic Report** action creates a local ZIP and opens it in Explorer. It does not automatically email the upstream author or upload the report.

## Build commands

```powershell
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Debug R26"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R26"
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj -c Release
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"
```

Before release, also validate Plugin and Tools with `Release R26`.

## Deploy

```powershell
.\deploy.ps1
```

Side-by-side development install:

```powershell
.\deploy-dev.ps1
```

User-scope install:

```powershell
.\deploy-userscope.ps1
```

All deploy paths in this fork are for **Revit 2026**.

## Release policy

`build-release.ps1` creates an R26 package.

`release.ps1` must remain fork-safe and must not publish into `LuDattilo/revitcortex-releases`.

Automatic upstream update checks are disabled until this fork has its own release channel. Do not point the customized fork back at the upstream manifest.

## AI skill references

Use `ai-skills/revitcortex/SKILL.md` as the router for focused references. Fork-specific references for build targeting and script escalation have been updated for Revit 2026.

## Source precedence

Some large documents under `docs/` and `WORKFLOWS.md` originated upstream and may contain historical multi-version examples. If a legacy version/build/access statement conflicts with the current fork behavior, use this precedence:

1. current source code / project files;
2. `AGENTS.md`;
3. this file;
4. `README.md` and current fork-specific AI references;
5. upstream historical documentation.
