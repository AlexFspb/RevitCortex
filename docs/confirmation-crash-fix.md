# Confirmation crash fix — 2026-09-25

Build ID: `2026.09.26-confirmation-timeout.3`. This revision includes the earlier bounded script-result projector and transaction warning capture and error rollback handler.

## Confirmed failure and repair

Four offline Revit dumps showed an InvalidOperationException from CriticalConfirmationWindow.Timer_Tick assigning DialogResult outside an active modal presentation. The checkbox could start a timer during construction, and failed ShowDialog paths did not guarantee cleanup. The exact initial ShowDialog exception was not preserved by the old Trace-only logger. Dispatcher suspension remains a hypothesis, not a proven cause.

Both confirmation windows now use a one-shot lifecycle. Construction and pre-show checkbox changes cannot activate approval. The approval timer starts only after ContentRendered within ShowConfirmation; finally always ends the lifecycle and stops/unsubscribes it. Late callbacks cannot approve. Completing the dialog is guarded, and an expected completion failure is captured rather than escaping the DispatcherTimer callback. Setting DialogResult closes the modal dialog; the redundant Close is removed.

A failed creation/show/completion returns `ConfirmationFailed`, not a fabricated user cancellation. Full exceptions, including inner exceptions and stacks, are saved locally in `support-reports/confirmation/<UTC>-<PID>-<unique>.txt`. Failure to write a diagnostic never grants approval. Explicit No/X remains cancellation. Neither error permits a blind retry.

All product choices remain: ordinary default-on 3 seconds, critical separate opt-in/session-only 3 seconds, no start/stop OK dialogs, automatic Release 8080/8888 and Dev 8081/8889, sandbox/read-only/audit gates. No global Dispatcher exception swallowing or persistent auto-approval is introduced.

## Transaction mode contract

Only `auto`, `none` and `group` are accepted. Null/omitted uses auto. `manual`, `readonly`, empty strings, unsupported case variants and typos return InvalidInput before bridge execution (MCP) and before confirmation/compilation (plugin/executor). They no longer silently fall through to auto. `none` means no Cortex-owned transaction, not a guarantee of read-only behavior. Prefer dedicated read-only tools for queries. The MCP description and generated schema reflect this contract.

## Request diagnostics

The legacy audit remains available. A separate profile-local `audit.requests-<PID>.jsonl` records `request_started` before routing/execution and `response_returned` when a response becomes available. Both records include a shared requestId, Revit PID, captured bridge port, document title/generation, build ID, Core module ID, tool name, input summary and C# hash. The title is cached on the UI thread; socket workers never read Revit properties for logging. Unknown/disabled tools and routing failures also receive paired records.

An unmatched start is evidence of an unfinished request, not by itself proof of a crash. A Timeout response sets executionMayStillBeRunning: the caller timing out does not interrupt the Revit operation. The old late-completion audit entry remains available but is not yet correlated by requestId. The request log is separate per process to avoid cross-process appends to one new journal.

`say_hello` and support reports expose build ID / Core module ID. Support report ZIPs include the current process request journal and the ten latest confirmation exception reports; reports stay local. The fixed build ID must change for a later release; do not rely on the inherited assembly version alone.

## Native parameter-access failure: avoid the observed traversal

The separate AccessViolation at ParamDef.getStorageType occurred inside a script, before result serialization. Neither the new projector nor transaction failure processing can repair native memory corruption. Do not blindly repeat an all-elements/all-parameters scan.

For elements associated with a level, prefer `ElementLevelFilter`; for logical dependents, query `level.GetDependentElements`. These are different relationships and are not exhaustive substitutes for every arbitrary ElementId-valued parameter. Native APIs can still fail; this is a narrower query strategy, not a no-crash guarantee.

Example for `transactionMode: "none"` (replace the level ID, paginate the two lists separately):

```csharp
long levelIdValue = 123; // choose and verify a real level ID
int offset = 0, pageSize = 200;
var level = document.GetElement(new ElementId(levelIdValue)) as Level;
if (level == null || !level.IsValidObject) throw new Exception("Invalid level");
using var collector = new FilteredElementCollector(document);
var associated = collector.WhereElementIsNotElementType()
    .WherePasses(new ElementLevelFilter(level.Id)).ToElementIds()
    .Select(id => id.Value).OrderBy(id => id).ToArray();
var dependents = level.GetDependentElements(null)
    .Select(id => id.Value).OrderBy(id => id).ToArray();
return new {
    levelId = level.Id.Value,
    associatedCount = associated.Length,
    dependentCount = dependents.Length,
    associatedIds = associated.Skip(offset).Take(pageSize).ToArray(),
    dependentIds = dependents.Skip(offset).Take(pageSize).ToArray(),
    offset, pageSize,
    scope = "Associated-level and logical-dependency IDs; not all parameter references"
};
```

The example performs no mutation and returns primitive IDs only. It has not been run on the user's model. It deliberately avoids reading every Parameter.StorageType; inspect only named/known parameters on a bounded selection if further data is required.

API sources: [ElementLevelFilter](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/1e320bfe-e33b-bf92-a19c-134a071c6d26.htm), [Level and inherited GetDependentElements](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/577e5d4e-a558-118c-9dea-3b810b061775.htm).

## Verification and remaining manual checks

Regression tests use fake modal delegates and unshown WPF construction only. They cover failed presentation, late ticks, cancellation, duplicate completion, setter/close failures, no constructor timer, independent preferences, unsupported transaction modes without bridge I/O, durable start records, paired identity, technical-error propagation and timeout semantics. No window is shown and no live Revit script is run by these checks.

After installing with all Revit processes closed, verify on a disposable model: normal/critical Yes/No/X and 3-second countdowns, repeated confirmations, technical failure reporting where reproducible, both ports, say_hello build identification, safe-result rollback and local logs. Do not reproduce the known native parameter crash on a working model. A real Revit UI lifecycle test still requires explicit permission for UI access or manual user testing.

## Invisible modal wait / timeout repair — 2026-09-26

A local live-process stack showed the Revit UI thread waiting in CriticalConfirmationWindow.ShowConfirmation / Window.ShowDialog before script persistence or Roslyn execution. The main Revit window was disabled and no confirmation was exposed by the window inventory. The user reported auto-run had been checked. The original reason the window disappeared (including whether it was off-screen) remains unproven.

- The ExternalEvent captures UIApplication.MainWindowHandle on the UI thread as the owner. Process.MainWindowHandle guessing is removed from confirmation constructors. Native monitor work-area pixels determine centering; WPF maximum dimensions use the corresponding device transform. The dialogs use a normal border and ShowInTaskbar=true to improve discoverability.
- Both approval timers require ContentRendered and IsVisible, preserving ordinary default-on and critical separate opt-in three-second preferences. An invisible dialog never authorizes via the countdown.
- A separate Dispatcher watchdog starts before ShowDialog, independently of Loaded. Failure to load/render/be visible after five seconds requests Close and returns ConfirmationFailed. A confirmation also has a 120-second maximum wait. Request timeout cancels via a high-priority Dispatcher callback. Cleanup only cancels; it never presses Yes or executes a script.
- The per-request lifetime atomically arbitrates expiration versus completion of confirmation. If expiration wins, even a late true callback cannot authorize execution. If approval wins first, a later timeout conservatively reports that execution may still complete.
- The event slot stays occupied until the accepted ExternalEvent actually drains; a timed-out queued event is skipped when it eventually arrives. Each caller owns its completion/result so a following request cannot steal or overwrite the previous response. Additional requests return busy with the active phase instead of replacing a pending/running request.
- Timeout context identifies queued / awaiting_confirmation / executing. Expired confirmation differs from user No and from a running script. Request journals preserve this distinction. Local lifecycle-PID.jsonl diagnostics record show/source/load/render/close, owner/handle, position, build/MVID and cancellation exceptions.

This is cooperative UI cancellation, not forced thread interruption: it cannot dismiss a window while the Revit Dispatcher is blocked in native code, recover a native crash, or safely abort an already-running arbitrary script. Closing an expired confirmation does not roll back earlier side effects of a multi-step tool. No global auto-approval, popup-clicking or process termination is introduced. The already-loaded old DLL cannot be replaced in an open Revit session.

Manual smoke checks after installation: two Revit instances on different monitors, mixed DPI and a removed monitor; both three-second flows and manual No/X; manual confirmation timeout; a queued request expiring while Revit is busy; simultaneous approval/expiry and repeated subsequent reads. Verify window close restores the owner and expired scripts never begin. Core tests cover the race/lifetime/position arithmetic; actual WPF/Revit modal cleanup still requires these checks.
