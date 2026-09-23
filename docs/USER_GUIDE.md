# RevitCortex 2026 — User Guide

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
6. [Allow auto-run and 10-second approval](#allow-auto-run-and-10-second-approval)
7. [Settings](#settings)
8. [Build and installation](#build-and-installation)
9. [Troubleshooting](#troubleshooting)
10. [Current tool reference](#current-tool-reference)

---

## Quick start

1. Start **Autodesk Revit 2026** and open a project.
2. Open the RevitCortex ribbon panel.
3. Click **Cortex Switch** to start the local Revit bridge.
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

The bridge is not started automatically. **Cortex Switch** controls whether the Revit-side service is listening.

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

RevitCortex also uses confirmation dialogs for destructive operations and checks Revit transaction commit status so a rollback is not reported as success.

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
4. Revit shows a **critical confirmation window**.
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

Do not use modal family-editing flows such as `Document.EditFamily` from the MCP external-event execution path. Modal Revit API flows can deadlock the request.

---

## Allow auto-run and 10-second approval

This fork changes the critical confirmation flow for custom C# scripts.

The dialog contains:

- **Yes** — run immediately;
- **No** — cancel;
- **Allow auto-run** — optional automatic approval for the current Revit process.

When **Allow auto-run** is checked:

1. the Yes action changes to a visible countdown such as `Yes — auto approve in 10 s`;
2. the counter decreases once per second;
3. when it reaches zero, the current script is approved automatically;
4. the user may still click **Yes** or **No** at any time;
5. the option remains enabled for later critical script confirmations during the same Revit session;
6. the option resets when Revit closes.

The preference is deliberately **not written to `settings.json`**.

Auto-run changes only the final critical approval step. It does **not** disable:

- `EnableCodeExecution`;
- sandbox validation;
- user-selected read-only mode;
- disabled-tool restrictions;
- audit logging.

---

## Settings

For simultaneous Revit processes with separate AI clients, follow
[Two Revit instances](MULTIPLE_REVIT_INSTANCES.md). Launch each with its own
`REVITCORTEX_PORT` and set the same value on that client's MCP server. A port
supplied at launch is displayed read-only and is not saved into shared settings.

Open **RevitCortex → Settings**.

### General

The General page includes the current connection state, server port, log level, read-only mode, telemetry/diagnostic-report settings and version information.

This fork is labelled for **Revit 2026**. There is no **License & Account** page or Premium activation flow.

### Tools

The Tools page lets you enable or disable individual tools.

`send_code_to_revit` has a separate **Allow custom C# execution** gate and remains disabled by default until explicitly enabled.

The page also explains the session-only **Allow auto-run** behavior and 10-second countdown.

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

That is the normal critical-confirmation behavior. If you intentionally want hands-off approval during the current Revit session, check **Allow auto-run** in the critical dialog. Each critical script will then show a 10-second countdown before approval.

### Auto-run should stop

Uncheck **Allow auto-run** in a critical confirmation dialog, or restart Revit. The preference is session-only.

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
