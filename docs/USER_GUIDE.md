# RevitCortex 2026 — User Guide

Revit 2026 multi-instance policy (2026-10-09): the server starts once automatically at the first Idling event, including empty Revit. Manual Stop stays off until manual Start or restart. Automatic listener restart prefers its previous port, then tries the other profile ports if that port is occupied. Explicit REVITCORTEX_PORT stays fixed; MCP clients never switch ports automatically. Release selects 8080 → 8888 → 8880 → 8088; Dev selects 8081 → 8889 → 8083 → 8891. Every MCP entry has an explicit fixed port; never fall back to another connection. Inspect get_connection_status/list_revit_instances for cached identity, then get_project_info or say_hello to verify the intended document. Automatic execution defaults to ON at every Revit launch for scripts and ordinary operations. The ribbon toggle and General Settings control one process-local policy. When ON there are no approval windows or countdowns; when OFF manual per-request confirmation remains. Orange indicates C# execution; blue indicates other tool execution. UI-thread work can pause blinking. No approval flags are persisted. The first C# request with no active document prepares a private metric service project. Never substitute that project for a user-named model. Shared settings must not be edited concurrently; temp scripts are isolated by process.

> This fork is maintained for **Autodesk Revit 2026 only**.
>
> The exact MCP tool count is intentionally not hard-coded in this guide. The current executable tool catalog is defined by the C# MCP wrappers and `tool-schemas.txt`.

This is an unofficial, independently maintained fork of `LuDattilo/RevitCortex`. The upstream experimental Premium/License & Account subsystem is not part of this fork. The upstream MIT software license remains unchanged in `LICENSE`.

## Contents

1. [Quick start](#quick-start)
2. [How RevitCortex works](#how-revitcortex-works)
3. [Choosing the right tool](#choosing-the-right-tool)
4. [Safe write workflow](#safe-write-workflow)
5. [Custom C# execution](#custom-c-execution)
6. [Automatic execution and activity](#automatic-execution-and-activity)
7. [Settings](#settings)
8. [Build and installation](#build-and-installation)
9. [Troubleshooting](#troubleshooting)
10. [Current tool reference](#current-tool-reference)

---

## Quick start

1. Start **Autodesk Revit 2026** and open a project.
2. Open the RevitCortex ribbon panel.
3. Check **Cortex Switch**: green means the automatically started bridge is listening. Use it to start manually if stopped.
4. Start an MCP-compatible client such as Claude Desktop, Claude Code, Codex, Cursor or another stdio MCP client configured for RevitCortex.
5. Give the AI a normal-language instruction such as:

```text
Show me the warnings in the active Revit project.
```

or:

```text
Find all walls on Level 1 and report their type names.
```

The Revit plugin communicates locally with `RevitCortex.Server`; Revit API work is executed inside the Revit 2026 process.

---

## How RevitCortex works

```text
MCP client
  -> RevitCortex.Server
  -> localhost TCP / JSON-RPC
  -> RevitCortex.Plugin in Revit 2026
  -> CortexRouter
  -> dedicated ICortexTool
  -> Autodesk Revit API
```

The bridge starts once at the first Idling event, including empty Revit. **Cortex Switch** controls manual start/stop; manual Stop remains off until Start or restart.
Successful start/stop operations update the green/gray icon without an OK dialog.

RevitCortex returns structured success/error responses instead of treating every failure as an opaque MCP exception.

---

## Choosing the right tool

The main rule is simple: **prefer a dedicated RevitCortex tool**.

Typical choices:

| Task | Preferred tool/workflow |
|---|---|
| Basic project information | `get_project_info` |
| Warnings / health check | `check_model_health`, `get_warnings` |
| Exact/simple element search | `export_elements_data` |
| Complex filtering | `ai_element_filter` |
| Elements in active view | `get_current_view_elements` |
| Spatial query | `get_elements_in_spatial_volume` |
| Read element parameters | `get_element_parameters` |
| Change a few parameters | `set_element_parameters` |
| Same value on many elements | `bulk_modify_parameter_values` |
| Different values per element | `sync_csv_parameters` |
| Copy selected properties | `match_element_properties` |
| Quick clash check | `clash_detection` |
| Visual clash workflow | `workflow_clash_review` |
| IFC workflows | `ifc_*` tools |
| Power BI workflows | `pbi_*` tools |

Do not guess custom parameter names. If the exact parameter is uncertain, inspect a representative element first.

For the current technical signatures, see [`../tool-schemas.txt`](../tool-schemas.txt).

---

## Safe write workflow

For operations that modify the model, use preview-first behavior whenever the tool supports it.

Typical pattern:

1. Read/identify the target elements.
2. Call the write tool with `dryRun: true` or its preview equivalent.
3. Check the proposed number of modified/skipped elements.
4. Execute the real operation.
5. Spot-check the result in Revit.

Example:

```text
Preview setting parameter "Comments" to "Checked" on these 50 elements.
```

Then, after inspecting the preview:

```text
Apply the change.
```

RevitCortex applies the automatic-execution policy to destructive-operation confirmation and checks transaction commit status so a rollback is not reported as success. Manual dialogs appear only when automatic execution is OFF.

`dryRun` support is **partial**, not universal. In the structural-steel toolset, these write tools currently do **not** provide a preview/dry-run parameter and therefore confirm and then write directly:

- `set_steel_connection_default_order`
- `set_steel_solid_cut_face_splitting`
- `set_steel_fabrication_unique_id`

For those non-preview mutators, review the target and requested values carefully before approving the write.

---

## Custom C# execution

`send_code_to_revit` is intentionally treated as a **last-resort** tool.

Use it only when a dedicated RevitCortex tool does not adequately cover the requested Revit API operation.

Before a script can execute:

1. **Custom C# execution must be enabled** in **Settings → Tools**.
2. The script must pass the RevitCortex sandbox validation.
3. Router permissions, user-selected read-only mode and disabled-tool settings remain active.
4. Cortex applies the automatic-execution policy: ON approves without a window or delay; OFF shows a **critical confirmation window**.
5. The invocation is recorded in the local audit trail.

There is no Premium activation/license gate in this fork.

Available script globals in Revit 2026:

- `document` — active `Autodesk.Revit.DB.Document`
- `uiDocument` — active `Autodesk.Revit.UI.UIDocument`
- `app` — Revit `Application`

For Revit 2026 code, use the current API such as `ElementId.Value`.

### Transaction modes

The script tool supports the existing transaction modes used by the implementation, including `auto`, `none` and `group`.

Avoid opening conflicting nested transactions when RevitCortex already owns the transaction boundary.

### Important limitation

`Document.EditFamily` is supported in a valid ExternalEvent API context; it is not inherently a modal UI command. Cortex rejects script entry with an open transaction and instruments direct EditFamily calls with runtime precondition checks. Unsupported delegate/conditional call forms fail before execution. ScriptPreconditionFailed reports whether script execution began; earlier effects are not claimed rolled back. Custom family-edit scripts must use `transactionMode: "none"`, not `auto`/`group`. Check the source document and family preconditions; manage family transactions and cleanup. Use the shipped FamilyLoadPolicy with both choices explicit, and Configure(tx, rollbackOnWarnings: true) for strict tasks. In none/group return capture diagnostics yourself; check the Close(false) boolean result. SaveAs uses a literal path in an existing folder; filesystem/backup checks stay outside the sandbox. Never invoke interactive editor/dialog flows. `none` does not provide a cross-document/file rollback. A timeout does not abort running API code. See [family editing](family-editing.md). Compiler checks run once before confirmation and script persistence. The first script may pause while Roslyn loads and compiles. Compilation precedes the approval decision; a manual window appears only when automatic execution is OFF. Scripts are saved only after successful compilation and confirmation; rejected scripts have no scriptPath, including reusable scripts. In auto, use strictWarnings=true for tasks requiring rollback on warnings (default false). This flag is rejected in none/group; configure each owned transaction with rollbackOnWarnings: true instead.

---

## Automatic execution and activity

Automatic execution is ON at each Revit launch. The ribbon **Автовыполнение** toggle and **Settings → General → Автоматическое выполнение** control the same process-local setting for ordinary tools and C#.

When ON, approval proceeds without a window, countdown or focus change. When OFF, ordinary modifying operations use **Allow once**, and critical C# uses **Yes/No**. Closing a manual dialog or pressing Escape cancels that request. Manual dialogs contain no automatic-approval checkbox or timer.

The setting is not saved to settings.json. Switching documents does not reset it; other Revit processes are unaffected. Restarting Revit enables it again. Turning it OFF affects subsequent approval decisions and does not abort a running script.

Automatic execution does not bypass sandbox validation, EnableCodeExecution, read-only mode, disabled tools, document-context checks, expiration or audit logging.

The automatic-execution icon shows a green play symbol when ON and a gray pause symbol when OFF. Cortex Switch retains its lightning symbol: gray stopped, green idle, orange C# execution, blue other tool execution. Busy brightness alternates when the UI thread is available. Long synchronous operations can pause blinking and repaint; the indicator is not a progress or responsiveness guarantee.

---

## Settings

For simultaneous Revit processes with separate AI clients, follow
[Four Revit instances](MULTIPLE_REVIT_INSTANCES.md). Cortex automatically selects
8080, 8888, 8880 or 8088 at automatic startup. Set the matching `REVITCORTEX_PORT` on each
client's MCP server. The assigned port is read-only and is not saved into shared
settings; the old saved `Port` value is ignored. Explicit launch overrides remain
available when client roles must not depend on activation order.

Open **RevitCortex → Settings**.

### General

The General page includes the current connection state, server port, log level, read-only mode, telemetry/diagnostic-report settings and version information.

This fork is labelled for **Revit 2026**. There is no **License & Account** page or Premium activation flow.

### Tools

The Tools page lets you enable or disable individual tools.

`send_code_to_revit` has a separate **Allow custom C# execution** gate and remains disabled by default until explicitly enabled.

The page explains the process-local automatic-execution policy. Change it on the ribbon or in General Settings.

### Read-only mode

When read-only mode is enabled, write tools are blocked. Do not try to bypass this with custom C#.

---

## Build and installation

This fork supports only these plugin/tool configurations:

- `Debug R26`
- `Release R26`

### Development build

```powershell
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Debug R26"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R26"
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj -c Release
```

### Tests

```powershell
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"
```

### Development deployment

Close Revit 2026 first.

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

### Release package

```powershell
.\build-release.ps1 -Version "1.0.51"
```

The generated package is R26-specific.

The fork does not automatically publish to or update from the upstream `LuDattilo` release repository.

---

## Troubleshooting

### MCP client cannot connect

Check that:

1. Revit 2026 is open.
2. A project is open when the tool requires one.
3. **Cortex Switch** is active.
4. The MCP client points to the installed `RevitCortex.Server.exe`.
5. The configured port matches the Revit plugin settings.

### Wrong/old DLL appears to load

Revit can scan both machine-scope and user-scope add-in folders. Use:

```powershell
.\check-install.ps1
```

If both scopes contain RevitCortex 2026, remove or synchronize the stale copy before testing again.

### Custom C# is disabled

Open **Settings → Tools** and enable custom C# execution only if the task genuinely requires `send_code_to_revit`.

### Script confirmation keeps appearing

Manual confirmation appears when automatic execution is OFF. Enable **Автовыполнение** on the ribbon or in General Settings for approval without windows or delay.

### Auto-run should stop

Turn OFF **Автовыполнение** on the ribbon or in General Settings. Subsequent requests require manual approval where applicable. This does not stop a running script. Restarting Revit enables automatic execution again.

### Diagnostic report

The **Diagnostic Report** ribbon action creates a ZIP locally and opens it in Explorer. It does not automatically email or upload the report.

### Automatic update banner is absent

Expected behavior in this fork. The upstream update channel is disabled so an upstream package cannot overwrite the customized Revit 2026 build. Until a fork-owned update channel exists, update this fork manually.

---

## Current tool reference

Do not rely on old documentation that states a fixed number of tools.

Use these sources:

- [`../tool-schemas.txt`](../tool-schemas.txt) — generated compact MCP signatures;
- [`COMMANDS.md`](COMMANDS.md) — command index and source pointers;
- [`../WORKFLOWS.md`](../WORKFLOWS.md) — operational workflows;
- [`../AGENTS.md`](../AGENTS.md) — current fork development and safety rules.

Historical upstream design documents may still mention other Revit versions. For this fork, the current Revit 2026 source/project files and fork-specific documentation take precedence.

Once enabled with Cortex Switch, Cortex remains connected when a family or project closes, even if no projects remain open. With no active document, model tools require a project; the first C# request can prepare a private service project as described above. Background families do not change the active MCP target. Commands waiting for a previous document are cancelled. Manual stop remains effective. Ordinary tools and C# share one process-local automatic-execution setting.
