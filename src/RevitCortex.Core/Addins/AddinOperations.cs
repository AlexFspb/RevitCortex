using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;

namespace RevitCortex.Core.Addins;

/// <summary>One admitted operation; bounded results plus request tombstones for this process lifetime.</summary>
public sealed class AddinOperations
{
    private readonly object _gate = new();
    private readonly AddinRegistry _registry;
    private readonly IAddinScheduler _scheduler;
    private readonly Func<JObject> _context;
    private readonly Func<bool> _readOnly;
    private readonly Func<string, bool> _enabled;
    private readonly Func<string, bool> _confirm;
    private readonly Action<object?, bool, bool> _checkApi;
    private readonly Action<JObject> _audit;
    private readonly Func<DateTime> _now;
    private readonly Dictionary<string, Operation> _requests = new(StringComparer.Ordinal);
    private Operation? _active;
    private long _finishedCount;
    private sealed class Operation
    {
        public string Hash = "";
        public JObject Request = new();
        public JObject State = new();
        public string? ResultJson;
        public CancellationTokenSource? Cancellation = new();
        public string? FinalState;
        public DateTime Deadline;
        public bool Invoked;
        public bool SupportsCancellation;
        public long FinishedOrder;
        public bool CancelRequested;
        public RevitCortex.Core.Session.ToolRequestLifetime? Lifetime;
    }
    public AddinOperations(AddinRegistry registry, IAddinScheduler scheduler, Func<JObject> context,
        Func<bool> readOnly, Func<string, bool> enabled, Func<string, bool> confirm,
        Action<object?, bool, bool> checkApi, Action<JObject>? audit = null, Func<DateTime>? now = null)
    {
        _registry = registry; _scheduler = scheduler; _context = context; _readOnly = readOnly;
        _enabled = enabled; _confirm = confirm; _checkApi = checkApi; _audit = audit ?? (_ => { }); _now = now ?? (() => DateTime.UtcNow);
    }
    public static bool Handles(string name) => name is "addin_list" or "addin_execute" or "addin_get_operation" or "addin_cancel_operation" or "addin_reload";
    public CortexResult<object> Handle(string name, JObject input)
    {
        try
        {
            if (!_enabled(name)) throw new AddinProtocolException("Disabled", "Tool is disabled: " + name);
            if (name == "addin_list") return CortexResult<object>.Ok(new JObject { ["context"] = _context(), ["providers"] = _registry.List(), ["contractVersion"] = 1 });
            if (name is "addin_get_operation" or "addin_cancel_operation") return CortexResult<object>.Ok(Inspect(input, name == "addin_cancel_operation"));
            return CortexResult<object>.Ok(Submit(input, name == "addin_reload"));
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(ex is AddinProtocolException p && p.Code is "ReadOnly" or "Disabled" ? CortexErrorCode.PermissionDenied : CortexErrorCode.InvalidInput,
                ex.Message, context: new Dictionary<string, object> { ["reason"] = (ex as AddinProtocolException)?.Code ?? "InvalidRequest", ["admittedByThisCall"] = false });
        }
    }
    private static string Required(JObject obj, string key) => obj[key]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)obj[key])
        ? (string)obj[key]! : throw new AddinProtocolException("InvalidRequest", "Missing string: " + key);
    private static string Uuid(JObject input, string key)
    {
        if (!Guid.TryParseExact(Required(input, key), "D", out var id))
            throw new AddinProtocolException("InvalidRequest", key + " must be a hyphenated UUID.");
        return id.ToString("D");
    }
    private void CheckContext(JObject expected, bool document)
    {
        var actual = _context();
        if (Required(expected, "instanceId") != actual.Value<string>("instanceId")) throw new AddinProtocolException("InstanceChanged", "Revit instance changed. Do not replay operations in another process.");
        if (document && (actual.Value<bool?>("documentPresent") != true || Required(expected, "documentToken") != actual.Value<string>("documentToken")
            || expected.Value<long?>("documentGeneration") != actual.Value<long?>("documentGeneration")))
            throw new AddinProtocolException("DocumentChanged", "The explicitly requested document context is no longer active.");
    }
    private JObject Submit(JObject input, bool reload)
    {
        var request = (JObject)input.DeepClone(); request.Remove("_cortexExpected");
        if (request.Properties().Any(p => !new[] { "requestId", "pluginId", "operationName", "expectedContext", "mode", "arguments", "queueTimeoutSeconds" }.Contains(p.Name)))
            throw new AddinProtocolException("InvalidRequest", "Unknown request property.");
        var requestId = Uuid(request, "requestId"); request["requestId"] = requestId;
        if (!(request["expectedContext"] is JObject expected)) throw new AddinProtocolException("InvalidRequest", "expectedContext is required.");
        if (expected.Properties().Any(p => !new[] { "instanceId", "documentToken", "documentGeneration", "pluginGeneration" }.Contains(p.Name))
            || expected["documentToken"] == null || expected["documentToken"]!.Type is not (JTokenType.Null or JTokenType.String)
            || ((string?)expected["documentToken"])?.Length > 128
            || expected["documentGeneration"]?.Type != JTokenType.Integer || expected.Value<long>("documentGeneration") < 0
            || expected["pluginGeneration"]?.Type != JTokenType.Integer || expected.Value<long>("pluginGeneration") < 1)
            throw new AddinProtocolException("InvalidRequest", "expectedContext needs only instanceId, documentToken (string/null), documentGeneration and pluginGeneration integers.");
        CheckContext(expected, false);
        if (reload && new[] { "operationName", "mode", "arguments" }.Any(k => request[k] != null))
            throw new AddinProtocolException("InvalidRequest", "Reload accepts only requestId, pluginId, expectedContext and queueTimeoutSeconds.");
        if (reload) { request["operationName"] = "$reload"; request["mode"] = "commit"; request["arguments"] = new JObject(); }
        var wait = request.Value<int?>("queueTimeoutSeconds") ?? 30;
        if (wait < 1 || wait > 120) throw new AddinProtocolException("InvalidRequest", "queueTimeoutSeconds must be 1..120.");
        request["queueTimeoutSeconds"] = wait;
        var json = AddinJson.Canonical(request).ToString(Formatting.None); AddinJson.Parse(json);
        using var sha = SHA256.Create(); var hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(json)));
        lock (_gate)
        {
            if (_requests.TryGetValue(requestId, out var old))
            {
                if (old.Hash != hash) throw new AddinProtocolException("RequestIdConflict", "This requestId already belongs to different arguments/context.");
                return Snapshot(old, 0, 16384);
            }
            if (_active != null) throw new AddinProtocolException("Busy", "An operation is pending/running. This request was not queued or started.");
            if (_requests.Count >= 10000) throw new AddinProtocolException("HistoryFull", "Request tombstone capacity reached; no old request may be replayed. Restart and verify model state.");
            var entry = _registry.Find(Required(request, "pluginId"));
            var command = reload ? new AddinCommandInfo { Name = "$reload", RequiresDocument = false, ExternalEffects = true, Modes = new[] { "commit" } }
                : entry.Info.Operations.SingleOrDefault(o => o.Name == Required(request, "operationName"))
                    ?? throw new AddinProtocolException("UnknownOperation", "Operation is not registered.");
            if (reload && entry.Loader == null) throw new AddinProtocolException("ReloadUnsupported", "The stable provider loader does not support reload.");
            var mode = Required(request, "mode");
            if (!command.Modes.Contains(mode)) throw new AddinProtocolException("UnsupportedMode", "Mode is not declared by this operation.");
            if (_readOnly() && (command.MutatesDocument || command.ExternalEffects)) throw new AddinProtocolException("ReadOnly", "This operation is blocked in read-only mode, including probeRollback.");
            CheckContext(expected, command.RequiresDocument);
            if (expected.Value<long?>("pluginGeneration") != entry.Generation) throw new AddinProtocolException("PluginChanged", "Inspect the loaded provider generation again.");
            if (!(request["arguments"] is JObject args)) throw new AddinProtocolException("InvalidRequest", "arguments must be an object.");
            if (!reload) AddinJson.Validate(args, AddinJson.Parse(command.ArgumentsSchema));
            var op = new Operation
            {
                Hash = hash, Request = request, Deadline = _now().AddSeconds(wait), SupportsCancellation = command.SupportsCancellation,
                State = new JObject
                {
                    ["operationId"] = Guid.NewGuid().ToString("D"), ["requestId"] = requestId,
                    ["instanceId"] = expected["instanceId"]!.DeepClone(), ["pluginId"] = entry.Info.PluginId,
                    ["operationName"] = command.Name, ["mode"] = mode, ["pluginGeneration"] = entry.Generation,
                    ["loadedVersion"] = entry.Info.LoadedVersion, ["moduleId"] = entry.Info.ModuleId,
                    ["buildId"] = "mvid:" + entry.Info.ModuleId, ["loadedModuleSha256"] = entry.Info.LoadedModuleSha256,
                    ["bridgePort"] = _context()["bridgePort"]?.DeepClone(),
                    ["documentToken"] = expected["documentToken"]?.DeepClone(), ["documentGeneration"] = expected["documentGeneration"]?.DeepClone(),
                    ["expectedContext"] = expected.DeepClone(), ["state"] = "queued", ["queuedUtc"] = _now().ToString("o"),
                    ["modelChanged"] = false, ["externalEffects"] = false, ["transactionOutcome"] = "notStarted"
                }
            };
            _registry.Reserve(entry, entry.Generation);
            _requests.Add(requestId, op); _active = op;
            bool accepted;
            try
            {
                accepted = _scheduler.TrySchedule(api => Run(op, entry, command, reload, api), result => Complete(op, entry, result));
            }
            catch (Exception ex)
            {
                Complete(op, entry, CortexResult<object>.Fail(CortexErrorCode.Unknown, "Scheduler rejected the event: " + ex.Message));
                return Snapshot(op, 0, 16384);
            }
            if (!accepted)
            {
                _requests.Remove(requestId); _registry.Release(entry); _active = null;
                op.Cancellation?.Dispose(); op.Cancellation = null;
                throw new AddinProtocolException("Busy", "Revit did not accept the event. Nothing started; no operation/UUID was retained. Retry the same request after it becomes available.");
            }
            Audit(op);
            return Snapshot(op, 0, 16384);
        }
    }
    private CortexResult<object> Run(Operation op, AddinRegistry.Entry entry, AddinCommandInfo command, bool reload, object? api)
    {
        IDisposable? lease = null;
        try
        {
            lock (_gate)
            {
                ExpireQueued(op);
                if ((string?)op.State["state"] != "queued") return CortexResult<object>.Ok(new object());
                CheckContext((JObject)op.Request["expectedContext"]!, command.RequiresDocument);
                if (entry.Provider == null || entry.Generation != op.State.Value<long>("pluginGeneration")) throw new AddinProtocolException("PluginChanged", "Provider was unloaded/replaced before execution.");
                var tool = reload ? "addin_reload" : "addin_execute";
                if (!_enabled(tool)) throw new AddinProtocolException("Disabled", "Tool was disabled before execution.");
                if (_readOnly() && (command.MutatesDocument || command.ExternalEffects)) throw new AddinProtocolException("ReadOnly", "Read-only mode enabled before execution.");
                op.Lifetime = RevitCortex.Core.Session.ToolRequestLifetime.Current;
                op.State["state"] = "running"; op.State["startedUtc"] = _now().ToString("o");
            }
            _checkApi(api, command.RequiresDocument, command.MutatesDocument);
            var gateOwner = (IAddinCommandProvider?)entry.Loader ?? entry.Provider!;
            if (!gateOwner.TryEnter(out lease) || lease == null) throw new AddinProtocolException("ProviderBusy", "Provider UI or another operation is busy. Nothing was invoked.");
            if ((command.MutatesDocument || command.ExternalEffects) && !_confirm(entry.Info.PluginId + ": " + command.Name))
                throw new AddinProtocolException("Cancelled", "Operation was not approved; provider was not invoked.");
            _checkApi(api, command.RequiresDocument, command.MutatesDocument);
            lock (_gate)
            {
                CheckContext((JObject)op.Request["expectedContext"]!, command.RequiresDocument);
                if (op.CancelRequested || op.Cancellation!.IsCancellationRequested) { op.FinalState = "cancelled"; return CortexResult<object>.Ok(new object()); }
                if (!_enabled(reload ? "addin_reload" : "addin_execute") || (_readOnly() && (command.MutatesDocument || command.ExternalEffects)))
                    throw new AddinProtocolException("PermissionChanged", "Permissions changed during confirmation; nothing was invoked.");
                op.Invoked = true; op.State["modelChanged"] = null; op.State["externalEffects"] = null; op.State["transactionOutcome"] = "unknown";
                op.State["actualContext"] = _context();
                Audit(op);
            }
            if (reload)
            {
                _registry.Reload(entry);
                lock (_gate)
                {
                    op.ResultJson = AddinRegistry.Describe(entry).ToString(Formatting.None);
                    op.FinalState = "succeeded"; op.State["modelChanged"] = false; op.State["externalEffects"] = true; op.State["transactionOutcome"] = "none";
                }
            }
            else
            {
                var result = entry.Provider.Execute(new AddinExecutionContext(command.Name, (string)op.Request["mode"]!, op.Request["arguments"]!.ToString(Formatting.None), api,
                    op.Cancellation!.Token, stage => { lock (_gate) if (ReferenceEquals(_active, op)) op.State["stage"] = stage?.Substring(0, Math.Min(stage.Length, 256)); }));
                CaptureProviderReceipt(op, result);
                _checkApi(api, command.RequiresDocument, command.MutatesDocument);
                lock (_gate)
                {
                    op.State["postExecutionCheckPassed"] = true;
                    if (result == null) throw new AddinProtocolException("InvalidResult", "Provider returned no transaction/effect receipt.");
                    op.State["modelChanged"] = result.ModelChanged; op.State["externalEffects"] = result.ExternalEffects; op.State["transactionOutcome"] = result.TransactionOutcome;
                    var data = AddinJson.Parse(result.ResultJson, 8388608); var diagnostic = AddinJson.Parse(result.DiagnosticsJson);
                    if (!(diagnostic is JArray)) throw new AddinProtocolException("InvalidResult", "Diagnostics must be a JSON array.");
                    op.State["diagnostics"] = diagnostic;
                    if (!new[] { "succeeded", "failed", "cancelled", "rolledBack" }.Contains(result.Status) || result.ModelChanged == null || result.ExternalEffects == null
                        || !new[] { "none", "committed", "rolledBack", "unknown" }.Contains(result.TransactionOutcome))
                        throw new AddinProtocolException("InvalidResult", "Explicit status and model/external effects are required.");
                    var mode = (string)op.Request["mode"]!;
                    if (result.Status is "succeeded" or "rolledBack") AddinJson.Validate(data, AddinJson.Parse(command.ResultSchema));
                    if (result.Status == "succeeded")
                    {
                        if (result.TransactionOutcome != (command.MutatesDocument && mode == "commit" ? "committed" : "none")) throw new AddinProtocolException("TransactionNotCommitted", "Success requires a valid final transaction outcome.");
                    }
                    if ((mode == "preview" && (result.ModelChanged != false || result.ExternalEffects != false))
                        || (mode == "probeRollback" && !ValidProbeReceipt(result))
                        || (result.Status is "cancelled" or "rolledBack" && (result.ModelChanged != false || result.ExternalEffects != false))
                        || (result.Status == "rolledBack" && result.TransactionOutcome != "rolledBack")
                        || (result.Status == "cancelled" && result.TransactionOutcome is not ("none" or "rolledBack"))
                        || (!command.MutatesDocument && result.ModelChanged != false)
                        || (!command.ExternalEffects && result.ExternalEffects != false))
                        throw new AddinProtocolException("EffectsMismatch", "Provider did not confirm the required no-effects/rollback outcome. Inspect the model; never retry blindly.");
                    op.ResultJson = data.ToString(Formatting.None); op.FinalState = result.Status;
                }
            }
            return CortexResult<object>.Ok(new object());
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                op.FinalState = !op.Invoked && (op.CancelRequested || ex is AddinProtocolException p && p.Code == "Cancelled") ? "cancelled" : "failed";
                op.State["error"] = new JObject { ["code"] = (ex as AddinProtocolException)?.Code ?? "ProviderException", ["message"] = ShortMessage(ex.Message),
                    ["effectsMayRemain"] = op.Invoked, ["suggestion"] = "Inspect diagnostics and model state before a new request; do not retry blindly." };
            }
            return CortexResult<object>.Ok(new object());
        }
        finally
        {
            try { lease?.Dispose(); }
            catch (Exception ex)
            {
                lock (_gate) { op.FinalState = "failed"; op.State["error"] = new JObject {
                    ["code"] = "ProviderCleanupFailed", ["message"] = ShortMessage(ex.Message), ["effectsMayRemain"] = op.Invoked }; }
            }
        }
    }
    private static bool ValidProbeReceipt(AddinCommandResult result) =>
        result.ModelChanged == false && result.ExternalEffects == false
        && ((result.TransactionOutcome == "rolledBack" && result.Status is "rolledBack" or "failed" or "cancelled")
            || (result.TransactionOutcome == "none" && result.Status is "failed" or "cancelled"));

    private void CaptureProviderReceipt(Operation op, AddinCommandResult? result)
    {
        lock (_gate)
        {
            op.State["postExecutionCheckPassed"] = false;
            if (result == null) return;
            // Preserve reported facts before checking Revit. They are not independently verified rollback/commit.
            var receipt = new JObject { ["status"] = ShortMessage(result.Status ?? ""),
                ["transactionOutcome"] = ShortMessage(result.TransactionOutcome ?? ""),
                ["modelChanged"] = result.ModelChanged, ["externalEffects"] = result.ExternalEffects };
            try
            {
                var diagnostics = AddinJson.Parse(result.DiagnosticsJson);
                if (!(diagnostics is JArray)) throw new AddinProtocolException("InvalidResult", "Diagnostics must be a JSON array.");
                receipt["diagnostics"] = diagnostics;
            }
            catch (Exception ex) { receipt["diagnosticsError"] = ShortMessage(ex.Message); }
            op.State["providerReceipt"] = receipt;
        }
    }
    private void Complete(Operation op, AddinRegistry.Entry entry, CortexResult<object> result)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_active, op)) return;
            if (op.FinalState != null) op.State["state"] = op.FinalState;
            if ((string?)op.State["state"] is "queued" or "running" or "cancelRequested")
            {
                op.State["state"] = "failed";
                op.State["error"] = new JObject { ["code"] = "DispatchFailed", ["message"] = result.Error?.Message ?? "Operation ended without a final receipt.", ["effectsMayRemain"] = op.Invoked };
            }
            op.State["finishedUtc"] = _now().ToString("o"); _registry.Release(entry); _active = null;
            op.Request = new JObject(); // Arguments are never needed after completion; retain only the hash.
            op.Lifetime = null;
            op.Cancellation?.Dispose(); op.Cancellation = null; // Release callback roots into collectible provider modules.
            op.FinishedOrder = ++_finishedCount;
            // Bound diagnostics AND results. Compact old records, but keep deduplication tombstones.
            var completed = _requests.Values.Where(o => o.FinishedOrder > 0 && o.State.Value<bool?>("resultExpired") != true)
                .OrderBy(o => o.FinishedOrder).ToList();
            long bytes = completed.Sum(o => (long)Encoding.UTF8.GetByteCount(o.ResultJson ?? "") + Encoding.UTF8.GetByteCount(o.State.ToString(Formatting.None)));
            int retained = completed.Count;
            foreach (var old in completed)
            {
                if (retained <= 128 && bytes <= 33554432) break;
                bytes -= Encoding.UTF8.GetByteCount(old.ResultJson ?? "") + Encoding.UTF8.GetByteCount(old.State.ToString(Formatting.None));
                old.ResultJson = null; old.State.Remove("diagnostics"); old.State.Remove("error"); old.State.Remove("stage");
                old.State.Remove("actualContext"); old.State.Remove("expectedContext"); old.State.Remove("providerReceipt"); old.State["resultExpired"] = true; retained--;
            }
            Audit(op);

        }
    }
    private static string ShortMessage(string message) => message.Length <= 4096 ? message : message.Substring(0, 4096);
    private void Audit(Operation op)
    {
        try { var receipt = new JObject(op.State.Properties().Where(p => new[] {
            "requestId", "operationId", "instanceId", "pluginId", "operationName", "mode", "pluginGeneration", "moduleId",
            "queuedUtc", "startedUtc", "finishedUtc", "state", "modelChanged", "externalEffects", "transactionOutcome", "bridgePort", "documentToken", "documentGeneration"
        }.Contains(p.Name)).Select(p => new JProperty(p.Name, p.Value.DeepClone())));
            receipt["errorCode"] = op.State["error"]?["code"]?.DeepClone();
            receipt["diagnosticCount"] = (op.State["diagnostics"] as JArray)?.Count;
            _audit(receipt);
        } catch { }
    }
    private void ExpireQueued(Operation op)
    {
        if ((string?)op.State["state"] == "queued" && _now() >= op.Deadline)
        { op.State["state"] = "expired"; op.State["finishedUtc"] = _now().ToString("o"); }
    }
    private JObject Inspect(JObject input, bool cancel)
    {
        if (Required(input, "instanceId") != _context().Value<string>("instanceId")) throw new AddinProtocolException("InstanceChanged", "Operation belongs to another process; result is unknown here.");
        var requestId = input["requestId"] == null ? null : Uuid(input, "requestId");
        var operationId = input["operationId"] == null ? null : Uuid(input, "operationId");
        if (requestId == null && operationId == null) throw new AddinProtocolException("InvalidRequest", "operationId or requestId required.");
        var offset = input.Value<int?>("resultOffset") ?? 0; var count = input.Value<int?>("resultCount") ?? 16384;
        if (offset < 0 || count < 1 || count > 65536) throw new AddinProtocolException("InvalidRequest", "resultOffset >= 0; resultCount 1..65536.");
        lock (_gate)
        {
            var op = _requests.Values.FirstOrDefault(o => (requestId == null || (string?)o.State["requestId"] == requestId) && (operationId == null || (string?)o.State["operationId"] == operationId));
            if (op == null) throw new AddinProtocolException("UnknownOutcome", "No retained operation with this ID in this process. Do not infer that an earlier write did not happen.");
            ValidatePage(op, offset); // Validate even result-dependent bounds before cancellation/expiration.
            ExpireQueued(op);
            if (cancel)
            {
                var state = (string?)op.State["state"];
                if (state == "queued") { op.Cancellation?.Cancel(); op.State["state"] = "cancelled"; op.State["finishedUtc"] = _now().ToString("o"); }
                else if (state is "running" or "cancelRequested")
                {
                    if (op.Invoked && !op.SupportsCancellation) throw new AddinProtocolException("CancellationUnsupported", "Provider cannot cooperatively cancel this operation.");
                    // Cancel callbacks may be provider code: never invoke them while holding our lock/UI wait.
                    op.CancelRequested = true; op.State["state"] = "cancelRequested";
                    var lifetime = !op.Invoked ? op.Lifetime : null;
                    ThreadPool.QueueUserWorkItem(_ => { try { lifetime?.Expire("addin_execute", 0); op.Cancellation?.Cancel(); } catch { } });
                }
            }
            return Snapshot(op, offset, count);
        }
    }
    private static JObject Snapshot(Operation op, int offset, int count)
    {
        var result = (JObject)op.State.DeepClone(); result["providerInvoked"] = op.Invoked;
        if (op.ResultJson != null)
        {
            var text = op.ResultJson;
            ValidatePage(op, offset);
            int length = Math.Min(count, text.Length - offset);
            if (length > 0 && offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1]))
                length += 1; // One extra UTF-16 unit keeps an astral character intact.
            result["resultPage"] = new JObject { ["encoding"] = "json-text-utf16", ["offset"] = offset, ["totalLength"] = text.Length,
                ["text"] = text.Substring(offset, length), ["nextOffset"] = offset + length < text.Length ? offset + length : (int?)null };
        }
        return result;
    }
    private static void ValidatePage(Operation op, int offset)
    {
        var text = op.ResultJson;
        if (text == null) return;
        if (offset > text.Length) throw new AddinProtocolException("InvalidRequest", "resultOffset exceeds the result length.");
        if (offset > 0 && offset < text.Length && char.IsLowSurrogate(text[offset]) && char.IsHighSurrogate(text[offset - 1]))
            throw new AddinProtocolException("InvalidRequest", "Offset splits a Unicode character; use nextOffset.");
    }
}
