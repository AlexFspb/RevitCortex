# RevitCortex 2026 — AI Assistant / Developer Guide

This file is the fork-specific source of truth for AI-assisted development.

## Fork scope

This repository targets **Autodesk Revit 2026 only**.

- Revit: 2026
- Plugin/Tools runtime: .NET 8
- Build configurations: `Debug R26`, `Release R26`
- Older plugin targets R23/R24/R25 and R27 are intentionally not maintained in this fork.
- The upstream experimental Premium/License & Account entitlement subsystem is intentionally removed.
- The upstream MIT software license remains unchanged in `LICENSE`.

Do not restore multi-version compatibility or Premium entitlement gating unless the user explicitly decides to do so.

## Architecture

```text
MCP client
  -> RevitCortex.Server
  -> TCP / JSON-RPC
  -> RevitCortex.Plugin
  -> CortexRouter
  -> ICortexTool
  -> Autodesk Revit API
```

`CortexSession` provides shared session state, document capabilities, locale, confirmation callbacks and result cache. Revit API work that arrives from the socket background thread is dispatched through Revit `ExternalEvent`.

## Build and test

After Plugin/Tools changes:

```powershell
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Debug R26"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R26"
```

Before release:

```powershell
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Release R26"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Release R26"
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj -c Release
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"
```

Never claim a build/test passed unless it was actually executed or verified by CI.

## Core implementation rules

### Prefer dedicated tools

Use the most targeted `ICortexTool` for normal Revit work. `send_code_to_revit` is the last resort, not the default shortcut.

Typical choices:

- exact/simple element search → `export_elements_data`
- complex element filter → `ai_element_filter`
- current view → `get_current_view_elements`
- spatial query → `get_elements_in_spatial_volume`
- one/few parameters → `set_element_parameters`
- same value on many elements → `bulk_modify_parameter_values`
- different values per element → `sync_csv_parameters`
- copy selected properties → `match_element_properties`
- quick clash check → `clash_detection`
- visual clash workflow → `workflow_clash_review`

Never guess custom parameter names. Inspect a representative element first.

### Destructive operations

Use preview-first behavior where available:

1. `dryRun: true`
2. inspect counts/results
3. `dryRun: false`
4. Revit confirmation before the transaction when the tool requires it

New destructive tools must use `CortexSession.RequestConfirmation(...)` and the standard transaction failure handling conventions.

### Result contract

Return `CortexResult<object>` / `CortexResult<T>` with structured errors. Do not allow raw exceptions to escape normal tool execution.

Common error codes:

- `ElementNotFound`
- `PermissionDenied`
- `TransactionFailed`
- `InvalidInput`
- `Timeout`
- `Cancelled`
- `Unknown`

## `send_code_to_revit`

Custom C# execution for this fork is Revit 2026 / .NET 8 / Roslyn only.

Required gates remain:

1. `EnableCodeExecution` must be enabled.
2. Code must pass the sandbox.
3. Router permissions, disabled-tool settings and user-selected read-only mode still apply.
4. Critical confirmation is requested before execution.
5. Invocation is audited.

There is no Premium activation/expiry/license gate in this fork.

Available script globals:

- `document`
- `uiDocument`
- `app`

Use `ElementId.Value` for Revit 2026 API code.

Do not use modal family-editing flows such as `Document.EditFamily` from the MCP external-event execution path.

## Critical script confirmation / Auto-run

`send_code_to_revit` uses `UI/CriticalConfirmationWindow`.

The dialog offers:

- **Yes** — approve now
- **No** — cancel
- **Allow auto-run** — session-only optional automatic approval

When `Allow auto-run` is enabled, the Yes action displays a visible **3-second countdown**. At zero, the current script is automatically approved. The user can still press Yes or No during the countdown.

The auto-run preference is deliberately held only in process memory and resets when Revit closes. Do not persist it to `settings.json` without an explicit product decision.

Auto-run automates only the last approval step. It must never bypass sandbox validation, read-only mode, auditing, code-execution settings or tool selection rules.

## UI rules

- `SettingsWindow` must retain navigation to General and Tools pages.
- Do not re-add a License & Account page unless the fork deliberately adopts a new entitlement model.
- `GeneralSettingsPage.xaml` control names must remain aligned with its code-behind.
- `ToolsSettingsPage.xaml` must retain `CodeExecToggle` because the code-behind uses it.
- Revit 2026 wording should be used in fork-specific visible descriptions.
- Normal destructive confirmations remain handled by `ConfirmationHelper` / native TaskDialog behavior.
- Critical custom-C# confirmation uses the dedicated WPF window.
- `Diagnostic Report` is local-only; it must not silently email or upload data.

## Deployment

Machine scope:

```powershell
.\deploy.ps1
```

Side-by-side dev profile:

```powershell
.\deploy-dev.ps1
```

User scope:

```powershell
.\deploy-userscope.ps1
```

Diagnostics:

```powershell
.\check-install.ps1
```

All of these target Revit 2026 only.

## Releases and updates

`build-release.ps1` builds only `Release R26` and produces an R26-labelled ZIP.

`release.ps1` is fork-safe and must not publish into `LuDattilo` release repositories.

Automatic upstream updates are disabled in `UpdateChecker` until this fork has its own release channel. Do not re-enable the upstream manifest because it can replace fork-specific binaries with an upstream package.

## Security

Maintain these controls:

- sandbox validation for arbitrary C#;
- audit logging;
- read-only enforcement;
- disabled-tool enforcement;
- structured router errors;
- transaction rollback/failure checks;
- localhost bridge behavior;
- explicit confirmation semantics for destructive/critical operations.

Do not weaken security controls just to eliminate user interaction. The session-only 3-second auto-run confirmation is the intended convenience mechanism for critical C# scripts.

## Documentation source of truth

Fork behavior should be consistent across:

- `README.md`
- `AGENTS.md`
- `CLAUDE.md`
- `distribution/README.txt`
- `distribution/LEGGIMI.md`
- `ai-skills/revitcortex/`

Large upstream historical/specification documents may still describe the original multi-version project. When they conflict with this file on target version/build/deploy/access policy, the **Revit 2026 fork rules in this file win**.
