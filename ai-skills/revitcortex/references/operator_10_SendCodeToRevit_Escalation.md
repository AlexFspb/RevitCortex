# 10 — `send_code_to_revit` Escalation (Revit 2026)

**Scope:** When to use custom C# instead of dedicated RevitCortex tools.
**Target:** Autodesk Revit 2026 only.
**Last verified for fork scope:** 2026-09-11.

## Fundamental rule

Prefer dedicated RevitCortex tools. `send_code_to_revit` is a **last-resort** path for operations not covered by a dedicated tool.

The MCP server instructions must not select arbitrary C# autonomously when a dedicated tool can perform the task.

## Before script execution

1. Confirm that no dedicated tool covers the operation adequately.
2. Explain the script approach when user consent is required by the client workflow.
3. `EnableCodeExecution` must be enabled in RevitCortex settings.
4. The code must pass `CodeSandbox.Validate`.
5. Critical confirmation is requested inside Revit before execution.

## Critical confirmation in this fork

Revit 2026 uses a dedicated critical confirmation window for `send_code_to_revit`.

The user has two manual actions:

- **Yes** — execute the current script immediately.
- **No** — cancel the current script.

The window also includes **Allow auto-run**:

- off by default when Revit starts;
- when enabled, the Yes action displays a visible **3-second countdown**;
- at zero, the current script is approved automatically;
- Yes/No remain available while the countdown runs;
- the preference is process/session-only and resets when Revit closes.

Auto-run does **not** bypass sandbox validation, code-execution settings, audit logging, router permissions, or read-only mode.

## Sandbox

Blocked namespace patterns include:

- `System.IO`
- `System.Net`
- `System.Diagnostics.Process`
- `Microsoft.Win32`
- `System.Reflection.Emit`
- `System.Runtime.InteropServices`

Validation is performed by `CodeSandbox.Validate(string code)`.

## Revit 2026 globals

Available script globals:

- `document` — active `Document`
- `uiDocument` — active `UIDocument`
- `app` — Revit `Application`

Use `ElementId.Value` for Revit 2026 API code.

## Transaction modes

`send_code_to_revit` supports the existing transaction modes such as `auto`, `none`, and `group`. Do not open conflicting nested Revit transactions from user code when the selected mode already owns the transaction boundary.

## Important limitation

Do not call modal family editing flows such as `Document.EditFamily` from the external-event execution context. Modal Revit API flows can deadlock the MCP request path.

## Required checks

- [ ] Dedicated-tool alternative checked first.
- [ ] Code execution enabled.
- [ ] Sandbox validation remains active.
- [ ] No prohibited modal `EditFamily` flow.
- [ ] Critical confirmation is not bypassed in code.
- [ ] Auto-run, if enabled by the user, is treated as session-only approval behavior.

## Avoid

- Do not use custom C# just to save one ordinary dedicated-tool call.
- Do not add persistence for `Allow auto-run` without an explicit product decision.
- Do not describe auto-run as disabling security; it only automates the final critical approval after the visible countdown.
