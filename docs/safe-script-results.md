# Safe script results and transaction failures — Revit 2026

## Problem

Serializing a raw Revit object can traverse an unbounded native API object graph. Reference-cycle detection does not stop getters that return new wrappers. Catching ordinary exceptions cannot recover the process from stack overflow. The September 24 crash was a stack overflow; the suspected BoundingBoxXYZ / Transform.Inverse chain is not proven without a call stack.

The old executor serialized results after committing changes and silently suppressed serialization errors. The new executor prepares a bounded plain-data response, including script metadata, before committing its transaction or assimilating its transaction group.

## Supported returns

- Null, strings, booleans, finite numbers, characters, enums, Guid, DateTime and DateTimeOffset.
- C# anonymous objects: backing fields are read directly; arbitrary property getters are never called.
- Dictionaries with string keys, arrays, lists and lazy IEnumerable / LINQ projections.
- JObject, JArray and supported JValue data, traversed with the same budgets. JRaw and executable/custom JSON conversions are rejected.

Raw Autodesk.Revit objects, including Element, ElementId, XYZ, Transform, BoundingBoxXYZ and FilteredElementCollector, are rejected before properties or enumerators are accessed. User-defined POCOs/records are also rejected: project their values into anonymous data. No fallback ToString, custom converter or serialization callback runs.

```csharp
// Incorrect: return element.get_BoundingBox(null);
var bounds = element.get_BoundingBox(null);
return new {
    id = element.Id.Value,
    min = new { x = bounds.Min.X, y = bounds.Min.Y, z = bounds.Min.Z },
    max = new { x = bounds.Max.X, y = bounds.Max.Y, z = bounds.Max.Z }
};
```

`return elements.Select(e => new { id = e.Id.Value, name = e.Name });` remains supported. Enumeration occurs once on the calling Revit thread, before commit, and is disposed on success or failure.

Limits: depth 16 (result starts at depth 1), 100,000 visited values, 256 KiB UTF-8 per string/key, 1 MiB response budget with 4 KiB reserved for the transport envelope. The size check includes escaping and script metadata; Unicode escaping makes the check conservative. Requests reaching a limit fail rather than silently truncate. Paginate large exports. Enumeration may stop conservatively at the node boundary without probing one more item.

## Error and retry contract

`error.code = ResultSerializationFailed` includes:

```json
{
  "reason": "RevitApiObject",
  "resultPath": "result.Bounds",
  "valueType": "Autodesk.Revit.DB.BoundingBoxXYZ",
  "transactionState": "rolled_back",
  "externalEffectsMayRemain": true
}
```

Other reasons include UnsupportedObject, ReferenceCycle, DepthLimit, NodeLimit, StringLimit, ResponseSizeLimit, NonFiniteNumber and ResultEvaluationFailed. The diagnostic never formats the rejected object.

- `auto`: prepare response before Commit. On preparation failure, attempt rollback; report `rolled_back` only when Revit confirms it.
- `group`: prepare before Assimilate; a successful group rollback also undoes inner commits in that group.
- `none`: Cortex owns no transaction; preparation failure reports `not_managed`. Earlier script commits may persist.
- If rollback fails or is unavailable, report `unknown` or `not_rolled_back` instead of claiming that changes were undone.

Do not blindly retry. Verify document/context and external effects, correct the result shape, then decide whether a retry is appropriate. File writes, exports, changes outside the owned document/group and other external effects are not undone by Revit rollback. Saving the script itself is independent of model rollback.

## Revit failure dialogs

`auto` installs `ScriptFailureHandling.Configure(tx)` after Start and before running user code. This transaction-level IFailuresPreprocessor rolls back unexpected warnings AND errors with SetClearAfterRollback(true). It does not force acceptance, resolve errors by deleting elements, or click dialogs.

On failed commit Cortex returns TransactionFailed, transaction status, bounded failure descriptions, severity, numeric element IDs and a diagnostic report path. Reports are local JSON under the profile's `support-reports/script-failures` directory, with PID and unique filenames. They record **rollback_requested**, not a fabricated confirmation of rollback. A failed report write is explicit in the response. Captures are bounded to 100 failure records, 2048 characters per description and 200 IDs per failure, with truncation indicators.

For **every script-owned transaction** in `group` or `none`, call the same helper after Start and before mutations:

```csharp
using var tx = new Transaction(document, "Bounded trial");
tx.Start();
var failures = RevitCortex.Tools.CodeExecution.ScriptFailureHandling.Configure(tx);
// Perform a small, bounded trial before any bulk replacement.
// ... changes ...
var status = tx.Commit();
return new {
    committed = status == TransactionStatus.Committed,
    transactionStatus = status.ToString(),
    failures = failures.Failures,
    diagnosticReportPath = failures.DiagnosticReportPath
};
```

For this script-owned pattern, inspect `result.committed`, not only the outer tool success flag. The outer flag describes successful execution/response preparation. Stop the workflow on a failed transaction; do not continue bulk work. For groups, explicitly abort/roll back the group if a required inner transaction fails. Cancelled alone is ambiguous: verify state/context before continuing the authorized task.

This is transaction-scoped, not a global suppression of Revit dialogs. Existing dedicated tools retain their existing failure policies. Script-owned transactions must opt into the helper; the executor does not rewrite arbitrary C#. Native crashes, dialogs outside transaction failure processing, and an iterator/GetEnumerator/MoveNext/Current/Dispose that never returns are not intercepted. A size/node budget cannot interrupt arbitrary code within one iterator call. Unknown dialogs need separate, narrowly scoped handling; automatic OK for all dialogs is not implemented.

The API mechanism follows Autodesk's [IFailuresPreprocessor contract](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/56e273aa-7d84-4a95-f06c-8a12e34e8be0.htm) and [SetClearAfterRollback](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/bebe6efd-b05f-7a0b-4cc3-609ec35be42c.htm).

## Unchanged product decisions

Automatic Release 8080 → 8888; Dev 8081 → 8889; no start/stop OK dialogs; ordinary default-on 3-second auto-run; separate critical C# confirmation with 3 seconds; process-scoped TCP lifetime. No new chat-consent requirement or persistent auto-approval. Sandbox, read-only, disabled-tool and audit controls remain.

## Verification

Unit tests exercise plain and anonymous data, 3000-row LINQ export, single-thread/single-pass enumeration and disposal, Revit/POCO rejection without getters/ToString, shared references versus cycles, depth/node/string/escaped-output/metadata limits, invalid numbers/JSON/dictionary keys, iterator exceptions and rollback messaging. Source guards cover pre-commit preparation and strict failure handling; they do not substitute for Revit integration tests.

Manual checks on a disposable Revit 2026 model before installing broadly:

1. Read coordinates via anonymous data; verify the same output with array and lazy LINQ returns.
2. Make a reversible edit and return a raw Revit object: receive ResultSerializationFailed, keep Revit alive and confirm auto/group rollback.
3. Repeat with an oversized result; confirm metadata/size failure also precedes commit.
4. In none, commit a script-owned transaction then return invalid data: verify the response does not claim rollback.
5. Trigger a bounded known rotation failure and an unexpected warning in auto and in a configured script-owned transaction: verify no failure dialog, no changes, TransactionStatus and readable diagnostic JSON with description/severity/IDs.
6. Confirm both running instances and agreed confirmation controls continue working. No crash reproduction or UI automation is part of unit testing.
