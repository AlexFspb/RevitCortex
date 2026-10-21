# RevitCortex 2026 — Security Model

Revit 2026 multi-instance policy (2026-10-09): the server starts once automatically at the first Idling event, including empty Revit. Manual Stop stays off until manual Start or restart. Automatic listener restart prefers its previous port, then tries the other profile ports if that port is occupied. Explicit REVITCORTEX_PORT stays fixed; MCP clients never switch ports automatically. Release selects 8080 → 8888 → 8880 → 8088; Dev selects 8081 → 8889 → 8083 → 8891. Every MCP entry has an explicit fixed port; never fall back to another connection. Inspect get_connection_status/list_revit_instances for cached identity, then get_project_info or say_hello to verify the intended document. Automatic execution defaults to ON at every Revit launch for scripts and ordinary operations. The ribbon toggle and General Settings control one process-local policy. When ON there are no approval windows or countdowns; when OFF manual per-request confirmation remains. Orange indicates C# execution; blue indicates other tool execution. UI-thread work can pause blinking. No approval flags are persisted. The first C# request with no active document prepares a private metric service project. Never substitute that project for a user-named model. Shared settings must not be edited concurrently; temp scripts are isolated by process.

This document describes the current security behavior of the **Autodesk Revit 2026** fork.

## Scope

RevitCortex consists of:

- an MCP server launched by the user's AI client;
- a Revit 2026 add-in;
- a localhost TCP/JSON-RPC bridge between them;
- optional integrations such as Power BI;
- an optional custom C# execution tool (`send_code_to_revit`).

The Revit bridge is local to the workstation. Some optional integrations can perform outbound HTTPS requests when explicitly configured and invoked.

The upstream experimental **Premium / License & Account** entitlement subsystem is not included in this fork. This does not alter the project's MIT software license in `LICENSE`.

---

## Local Revit bridge

The normal MCP-to-Revit command path is:

```text
MCP client
  -> RevitCortex.Server
  -> localhost TCP bridge
  -> RevitCortex.Plugin
  -> Revit API
```

The Revit-side bridge starts once at the first Idling event, including empty Revit. **Cortex Switch** controls manual start/stop. Manual Stop stays off until manual Start or restart.

Revit API write operations are executed in the appropriate Revit API context through the plugin dispatcher.

---

## Read-only mode

RevitCortex has a configurable read-only mode. When enabled, tools classified as write operations are blocked by the router.

Custom C# must not be used as a workaround for read-only mode.

---

## Destructive-operation confirmation

Normal destructive/bulk tools can request confirmation through `CortexSession.RequestConfirmation(...)`.

Automatic execution approves these requests immediately when ON. When OFF, `OperationConfirmationWindow` offers one Allow once action; X/Escape cancels. No per-dialog timer or auto-run checkbox remains. A missing confirmation callback fails closed. Legacy Core ApproveAll/AutoMode flags and the old floating Auto mode window have been removed.

---

## Custom C# execution

`send_code_to_revit` is the highest-risk capability in the project and is treated as a **last resort**.

Before custom C# runs, the following gates remain active:

1. `EnableCodeExecution` must be explicitly enabled.
2. The code must pass sandbox validation.
3. Router permission, disabled-tool and user-selected read-only rules still apply.
4. A critical confirmation decision is required in Revit.
5. The invocation is written to the audit trail.

There is no Premium activation, expiry or license-based read-only gate in this fork.

The tool is intended for Revit API operations not adequately covered by dedicated tools. Dedicated RevitCortex tools should be preferred.

### Sandbox

The sandbox blocks dangerous namespace/API patterns used for unrestricted filesystem, network, process, registry, emit/interoperability access. The exact implementation in `CodeSandbox` / `CodeSandboxV2` is authoritative.

Examples of restricted areas include:

- `System.IO`
- `System.Net`
- `System.Diagnostics.Process`
- `Microsoft.Win32`
- `System.Reflection.Emit`
- `System.Runtime.InteropServices`

Do not weaken sandbox rules merely to make an arbitrary script easier to execute.

### Audit

Custom C# invocations are audited. The router also records normal tool activity and execution outcomes using the configured audit logger.

---

## Automatic execution and manual confirmation

Automatic execution is ON at each Revit launch. The ribbon **Автовыполнение** toggle and **Settings → General → Автоматическое выполнение** control the same process-local setting for ordinary tools and C#.

When ON, approval proceeds without a window, countdown or focus change. When OFF, ordinary modifying operations use **Allow once**, and critical C# uses **Yes/No**. Closing a manual dialog or pressing Escape cancels that request. Manual dialogs contain no automatic-approval checkbox or timer.

The setting is not saved to settings.json. Switching documents does not reset it; other Revit processes are unaffected. Restarting Revit enables it again. Turning it OFF affects subsequent approval decisions and does not abort a running script.

Automatic execution does not bypass sandbox validation, EnableCodeExecution, read-only mode, disabled tools, document-context checks, expiration or audit logging.

Default-on automatic execution is an intentional product decision for autonomous work. Agents must not disable it or reintroduce a delay as an unsolicited security change. Manual confirmation failures return a technical failure, not a fabricated user rejection.

### Script access to host controls

Before executable output, the compiler rejects references to Cortex host-control types, `UIApplication.PostCommand`, `RevitCommandId`, and `Autodesk.Windows` types. Aliases, escaped identifiers and method groups use resolved symbols. Documented family/failure helpers and direct model APIs remain available.

Posted commands execute after the script returns and could otherwise toggle Cortex controls outside the guarded request. Use direct model APIs or dedicated tools instead. Rejection returns `PermissionDenied`, `HostControlAccess`, and `scriptExecuted=false`; the agent receives the offending API and line. These checks are not complete isolation for arbitrary .NET code and do not prevent native Revit crashes.

---

## Revit transaction safety

Write tools should:

- validate inputs before opening a transaction;
- use preview/dry-run modes where supported;
- open the appropriate Revit transaction boundary;
- roll back on errors;
- verify commit status;
- return a structured failure if Revit rejects or rolls back the operation.

A failed or rolled-back transaction must not be reported as success.

---

## Modal Revit API operations

`Document.EditFamily` is supported in a valid ExternalEvent API context; it is not inherently a modal UI command. Cortex rejects script entry with an open transaction and instruments direct EditFamily calls with runtime precondition checks. Unsupported delegate/conditional call forms fail before execution. ScriptPreconditionFailed reports whether script execution began; earlier effects are not claimed rolled back. Custom family-edit scripts must use `transactionMode: "none"`, not `auto`/`group`. Check the source document and family preconditions; manage family transactions and cleanup. Use the shipped FamilyLoadPolicy with both choices explicit, and Configure(tx, rollbackOnWarnings: true) for strict tasks. In none/group return capture diagnostics yourself; check the Close(false) boolean result. SaveAs uses a literal path in an existing folder; filesystem/backup checks stay outside the sandbox. Never invoke interactive editor/dialog flows. `none` does not provide a cross-document/file rollback. A timeout does not abort running API code. See [family editing](family-editing.md). Compiler checks run once before confirmation and script persistence. In auto, use strictWarnings=true for tasks requiring rollback on warnings (default false). This flag is rejected in none/group; configure each owned transaction with rollbackOnWarnings: true instead.

---

## Path safety

Tools that accept filesystem paths should use the project's path-safety helpers rather than arbitrary raw paths.

The implementation may distinguish between ordinary import/export paths and linked-model/network-share workflows. The current `PathSafety` code is authoritative for allowed locations.

---

## Power BI and outbound network access

Power BI functionality can perform outbound HTTPS requests when the user explicitly signs in and invokes Power BI tools.

Typical destinations include Microsoft authentication and Power BI service endpoints.

Model-derived information can therefore leave the local workstation when the user intentionally publishes to Power BI. Organizations should evaluate those flows under their own Microsoft 365/Power BI governance and data-protection policies.

The local Power BI selection listener is intended for localhost interaction and should not be treated as a public network API.

---

## Telemetry

Where telemetry exists, it must remain opt-in according to the current settings/consent implementation. Telemetry failures must not break or block normal Revit operation.

Do not add raw model data, document paths, user credentials or arbitrary tool inputs to telemetry events.

---

## Diagnostic reports

The **Diagnostic Report** ribbon action creates a local ZIP and opens it in Explorer. The fork does not automatically email the upstream author or upload the report.

---

## Automatic updates

The **upstream automatic update channel is disabled in this fork**.

This is intentional: consuming the upstream `LuDattilo` release channel could replace the customized Revit 2026 binaries with an upstream build.

Until the fork has a dedicated `AlexFspb` release channel, updates are installed manually from this fork.

Download URL validation and SHA-256 helper functions are retained for future use by a fork-owned updater.

---

## Installation scope

Revit can load add-ins from both machine scope and user scope. A stale duplicate installation can cause the wrong DLL to load.

Use `check-install.ps1` to diagnose duplicate RevitCortex 2026 installations before debugging unexpected behavior.

Development/deployment scripts in this fork target **Revit 2026 only**.

---

## Security review checklist

When changing the plugin or adding tools, verify that:

- read-only mode still blocks write operations;
- destructive writes use confirmation/preview where appropriate;
- custom C# still requires the code-execution gate and sandbox;
- critical confirmation fails closed if its UI cannot be shown;
- automatic execution remains process-local, defaults ON at startup, and has no per-dialog timer;
- audit logging remains active;
- transaction failures return structured errors;
- localhost services are not accidentally widened to public network interfaces;
- outbound integrations are explicit and documented;
- the fork does not silently restore the upstream update channel or Premium entitlement gate.

---

## Source of truth

For current behavior, use this precedence:

1. current Revit 2026 source code/project files;
2. `AGENTS.md`;
3. `README.md`, `CLAUDE.md`, this security document and current AI-skill references;
4. dated upstream design/review documents as historical context only.
