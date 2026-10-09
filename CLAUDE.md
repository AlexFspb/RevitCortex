# RevitCortex 2026 — Claude / MCP Guide

Revit 2026 multi-instance policy (2026-10-09): the server starts once automatically at the first Idling event, including empty Revit. Manual Stop stays off until manual Start or restart. Release selects 8080 → 8888 → 8082 → 8890; Dev selects 8081 → 8889 → 8083 → 8891. Every MCP entry has an explicit fixed port; never fall back to another connection. Inspect get_connection_status/list_revit_instances for cached identity, then get_project_info or say_hello to verify the intended document. Critical C# auto-run now defaults to ON at each Revit launch, independently of ordinary auto-run; both retain the visible 3-second countdown, cancellation and process-local opt-out. No approval flags are persisted. The first C# request with no active document prepares a private metric service project. Never substitute that project for a user-named model. Shared settings must not be edited concurrently; temp scripts are isolated by process.

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

Cortex stays enabled across document/family closure. The active UI document is the MCP target; background families must not replace it. No active document produces a model-command error while TCP remains available. Queued commands are cancelled when their captured context changes; manual Cortex stop is respected.

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

Default-on ordinary auto-run and both 3-second countdowns are intentional product decisions for autonomous MCP work. Agents must not disable ordinary auto-run, lengthen either countdown, or add a separate chat-approval step for authorized C# work. The separate critical C# confirmation and other execution safeguards remain in place.

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

`Document.EditFamily` is supported in a valid ExternalEvent API context; it is not inherently a modal UI command. Cortex rejects script entry with an open transaction and instruments direct EditFamily calls with runtime precondition checks. Unsupported delegate/conditional call forms fail before execution. ScriptPreconditionFailed reports whether script execution began; earlier effects are not claimed rolled back. Custom family-edit scripts must use `transactionMode: "none"`, not `auto`/`group`. Check the source document and family preconditions; manage family transactions and cleanup. Use the shipped FamilyLoadPolicy with both choices explicit, and Configure(tx, rollbackOnWarnings: true) for strict tasks. In none/group return capture diagnostics yourself; check the Close(false) boolean result. SaveAs uses a literal path in an existing folder; filesystem/backup checks stay outside the sandbox. Never invoke interactive editor/dialog flows. `none` does not provide a cross-document/file rollback. A timeout does not abort running API code. See [family editing](docs/family-editing.md). Compiler checks run once before confirmation and script persistence. In auto, use strictWarnings=true for tasks requiring rollback on warnings (default false). This flag is rejected in none/group; configure each owned transaction with rollbackOnWarnings: true instead.

## Critical confirmation and `Allow auto-run`

Ordinary destructive/bulk requests have their own `OperationConfirmationWindow`:
one Allow once button, auto-run checked by default, and a fresh 3-second countdown
per request. X/Escape cancels. Unchecking disables this countdown for the current
process. The two-minute/unlimited menu and floating Auto mode window are removed.
This normal-operation preference does not enable critical C# auto-run.

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

## Script result and failure contract

Only auto/none/group transaction modes are supported; reject manual/readonly rather than silently opening an auto transaction. none is not read-only enforcement. Confirmation UI failures must return ConfirmationFailed with local full-exception diagnostics, never a fabricated user refusal. Preserve lifecycle cleanup even when ShowDialog fails. Confirmation timers require a rendered visible window; bind its owner to UIApplication.MainWindowHandle. Expire pending confirmation on the UI Dispatcher and forbid approval after expiration. Never release a timed-out ExternalEvent slot before it drains; each caller must retain its own completion/result. See [confirmation crash fix](docs/confirmation-crash-fix.md). Update CortexBuild.Id for each new distributed build.


Return plain data, never raw Revit API objects or arbitrary POCOs. Anonymous objects, string-keyed dictionaries, arrays and bounded lazy LINQ are supported. See [safe script results](docs/safe-script-results.md). ResultSerializationFailed reports the rejected path and actual rollback state; never blindly retry.

For every mutation script, configure transaction-level IFailuresPreprocessor and SetClearAfterRollback(true) before changes. Auto mode installs ScriptFailureHandling.Configure automatically; script-owned transactions in group/none must call it after Start. Errors roll back the affected transaction; warnings are captured and removed from the Revit failure dialog, then returned to the agent; never force-accept unresolved errors or delete model elements as recovery. Capture descriptions, severity and numeric element IDs, check commit status, use a bounded dry-run before bulk replacement and retain the diagnostic report. Cancelled alone is ambiguous: verify model/context before continuing. This does not intercept native crashes or every modal window and does not bypass Cortex confirmation/security controls or enable persistent auto-approval.
