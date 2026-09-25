# RevitCortex 2026 — Security Model

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

The Revit-side bridge is controlled by **Cortex Switch** and is not started automatically.

Revit API write operations are executed in the appropriate Revit API context through the plugin dispatcher.

---

## Read-only mode

RevitCortex has a configurable read-only mode. When enabled, tools classified as write operations are blocked by the router.

Custom C# must not be used as a workaround for read-only mode.

---

## Destructive-operation confirmation

Normal destructive/bulk tools can request confirmation through `CortexSession.RequestConfirmation(...)`.

Every normal confirmation request displays `OperationConfirmationWindow` with
one Allow once button and an auto-run checkbox. Normal auto-run defaults to ON
at process startup, with a visible 3-second delay for each request. Unchecking
waits for manual approval. X/Escape cancels the current request, and a late timer
tick cannot turn that cancellation into approval. Closing preserves the checkbox
preference for the next request. The preference is not written to settings.json.

The old two-minute/unlimited UI choices and floating Auto mode ON window are
removed. Legacy Core approval flags remain for compatibility; this UI never
arms them. Normal approval returns true only for the current request. Critical
custom-C# confirmation remains separate: Yes/No and opt-in session auto-run.

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

## Critical C# confirmation and `Allow auto-run`

This fork adds a convenience option to the critical script confirmation window.

The user can choose:

- **Yes** — approve the current script immediately;
- **No** — cancel;
- **Allow auto-run** — allow timed approval for critical scripts during the current Revit process.

When **Allow auto-run** is enabled, the Yes action displays a visible **3-second countdown**. If the user does nothing, the current script is approved at zero. Yes and No remain available throughout the countdown.

### Important boundaries

`Allow auto-run`:

- is **off by default after Revit starts**;
- is stored only in process memory;
- resets when Revit closes;
- does not write a permanent trust flag to `settings.json`;
- does not bypass sandbox validation;
- does not bypass `EnableCodeExecution`;
- does not bypass user-selected read-only or disabled-tool restrictions;
- does not disable audit logging.

It automates only the final critical approval step after a visible delay.

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

Do not run modal family-editing flows such as `Document.EditFamily` from the MCP external-event execution context. Modal Revit UI/API workflows can block the external-event request and deadlock the caller.

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
- `Allow auto-run` remains session-only unless the product policy is deliberately changed;
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
