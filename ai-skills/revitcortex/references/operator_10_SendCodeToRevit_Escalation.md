# 10 — `send_code_to_revit` Escalation (Revit 2026)

Revit 2026 multi-instance policy (2026-10-09): the server starts once automatically at the first Idling event, including empty Revit. Manual Stop stays off until manual Start or restart. Release selects 8080 → 8888 → 8880 → 8088; Dev selects 8081 → 8889 → 8083 → 8891. Every MCP entry has an explicit fixed port; never fall back to another connection. Inspect get_connection_status/list_revit_instances for cached identity, then get_project_info or say_hello to verify the intended document. Automatic execution defaults to ON at every Revit launch for scripts and ordinary operations. The ribbon toggle and General Settings control one process-local policy. When ON there are no approval windows or countdowns; when OFF manual per-request confirmation remains. Orange indicates C# execution; blue indicates other tool execution. UI-thread work can pause blinking. No approval flags are persisted. The first C# request with no active document prepares a private metric service project. Never substitute that project for a user-named model. Shared settings must not be edited concurrently; temp scripts are isolated by process.

**Scope:** When to use custom C# instead of dedicated RevitCortex tools.
**Target:** Autodesk Revit 2026 only.
**Last verified for fork scope:** 2026-09-11.

## Fundamental rule

Prefer dedicated RevitCortex tools. `send_code_to_revit` is a **last-resort** path for operations not covered by a dedicated tool.

The MCP server instructions must not select arbitrary C# autonomously when a dedicated tool can perform the task.

## Before script execution

1. Confirm that no dedicated tool covers the operation adequately.
2. Work within the user-authorized task; do not add a separate chat-consent step. The configured Revit confirmation still applies.
3. `EnableCodeExecution` must be enabled in RevitCortex settings.
4. The code must pass `CodeSandbox.Validate`.
5. Critical confirmation is requested inside Revit before execution.

## Host approval in this fork

The ribbon **Автовыполнение** toggle and **Settings → General → Автоматическое выполнение** share one process-local policy. It defaults to ON at each Revit launch, as explicitly requested by the user on 2026-10-21. Switching documents does not reset it. Nothing is persisted to settings.json and other Revit processes are unaffected.

When ON, scripts and ordinary operations pass the host approval step immediately, without windows, countdowns or focus changes. When OFF, ordinary modifying operations use Allow once and critical C# uses Yes/No; X/Escape cancels. Read-only tools retain their existing confirmation requirements (normally none).

Cortex Switch is gray when stopped, green when idle, orange while handling C# and blue while handling other ExternalEvent tools. Busy icons alternate bright/dim when the UI Dispatcher is available. Long synchronous Revit work may pause both blinking and repaint; this is not a liveness or progress guarantee. No nested message pump, screen access or window activation is used. Connection probes do not blink. The indicator clears when the handler actually finishes, not when the MCP caller times out.

Sandbox checks, EnableCodeExecution, disabled tools, read-only mode, context checks, expiration and audit remain mandatory. Scripts cannot change host controls. Do not reintroduce per-dialog automatic approval or chat approval for already authorized work.

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

`Document.EditFamily` is supported in a valid ExternalEvent API context; it is not inherently a modal UI command. Cortex rejects script entry with an open transaction and instruments direct EditFamily calls with runtime precondition checks. Unsupported delegate/conditional call forms fail before execution. ScriptPreconditionFailed reports whether script execution began; earlier effects are not claimed rolled back. Custom family-edit scripts must use `transactionMode: "none"`, not `auto`/`group`. Check the source document and family preconditions; manage family transactions and cleanup. Use the shipped FamilyLoadPolicy with both choices explicit, and Configure(tx, rollbackOnWarnings: true) for strict tasks. In none/group return capture diagnostics yourself; check the Close(false) boolean result. SaveAs uses a literal path in an existing folder; filesystem/backup checks stay outside the sandbox. Never invoke interactive editor/dialog flows. `none` does not provide a cross-document/file rollback. A timeout does not abort running API code. See [family editing](operator_11_Family_Editing.md). Compiler checks run once before confirmation and script persistence. In auto, use strictWarnings=true for tasks requiring rollback on warnings (default false). This flag is rejected in none/group; configure each owned transaction with rollbackOnWarnings: true instead.

## Required checks

- [ ] Dedicated-tool alternative checked first.
- [ ] Code execution enabled.
- [ ] Sandbox validation remains active.
- [ ] Family editing, if needed, uses `none`, valid document/family preconditions, failure handling and guaranteed cleanup; no interactive UI flow.
- [ ] Critical confirmation is not bypassed in code.
- [ ] Automatic execution is a process-local host setting, enabled at startup.

## Avoid

- Do not use custom C# just to save one ordinary dedicated-tool call.
- Do not add persistence for `Allow auto-run` without an explicit product decision.
- Do not describe auto-run as disabling security; it only automates the final critical approval under the user-controlled process-local policy.

## Results and Revit failures

Return scalars, anonymous data, string-keyed dictionaries, arrays or LINQ projections. Convert Revit objects to numbers/strings yourself (ElementId.Value, XYZ.X/Y/Z); never return raw API objects, including collectors. Limits: depth 16, 100000 nodes, 256 KiB per string, 1 MiB response with transport reserve. ResultSerializationFailed includes reason, resultPath and transactionState. Auto/group prepare the response before commit and attempt rollback on invalid results; none cannot promise rollback. Never blindly retry; verify state and external effects.

Every mutation script must configure transaction-level IFailuresPreprocessor and SetClearAfterRollback(true) before changes. Auto mode does so automatically. In group/none, call RevitCortex.Tools.CodeExecution.ScriptFailureHandling.Configure(tx) after every tx.Start() and before mutations. Errors roll back the affected transaction; warnings are captured and removed from the Revit failure dialog, then returned to the agent. Retain failure descriptions, severity and numeric element IDs, check TransactionStatus, retain the diagnostic JSON and use a bounded dry-run before bulk replacement. Never force-accept unresolved errors or delete affected elements as automatic recovery. Cancelled alone is ambiguous; verify context/state. This cannot intercept native crashes, all modal windows or iterator code that never returns. Cortex approval/security dialogs remain unchanged; no persistent auto-approval.

## Confirmation failures, modes and native query scope

Only auto/none/group are valid transaction modes. manual/readonly are rejected; none does not enforce read-only access. ConfirmationFailed is a technical dialog failure, not user refusal: consult support-reports/confirmation and audit.requests-<PID>.jsonl; do not blindly retry. Verify buildId with say_hello before relying on new helpers.

Avoid scanning every parameter of every element to find a level reference. Prefer ElementLevelFilter for level association and level.GetDependentElements for logical dependencies; their sets are not equivalent to all arbitrary ElementId-valued references. Return paginated numeric IDs; inspect only known parameters on bounded selections. An AccessViolation inside native API code cannot be made safe by the result projector or ordinary catch. Do not reproduce such a crash on the working model.
