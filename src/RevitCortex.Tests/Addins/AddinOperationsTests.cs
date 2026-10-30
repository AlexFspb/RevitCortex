using Newtonsoft.Json.Linq;
using RevitCortex.Core.Addins;
using RevitCortex.Core.Results;
using Xunit;

namespace RevitCortex.Tests.Addins;

public class AddinOperationsTests
{
    private const string Empty = "{\"type\":\"object\",\"additionalProperties\":false}";
    private sealed class Scheduler : IAddinScheduler
    {
        public bool Busy, Throw;
        public Func<object?, CortexResult<object>>? Work;
        private Action<CortexResult<object>>? _completed;
        public bool TrySchedule(Func<object?, CortexResult<object>> run, Action<CortexResult<object>> completed)
        {
            if (Throw) throw new Exception("raise failed");
            if (Busy) return false;
            Assert.Null(Work); Work = run; _completed = completed; return true;
        }
        public void Drain()
        {
            var work = Work!; Work = null; var done = _completed!;
            done(work(null));
        }
        public void Reject() { Work = null; _completed!(CortexResult<object>.Fail(CortexErrorCode.Cancelled, "document changed before event")); }
    }
    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
    private sealed class Provider : IReloadableAddinProvider
    {
        public AddinProviderInfo Info = new() { PluginId = "test", Name = "Test", LoadedVersion = "1", ModuleId = Guid.NewGuid().ToString(), Operations = new[] {
            new AddinCommandInfo { Name = "read", RequiresDocument = true, ArgumentsSchema = Empty, ResultSchema = Empty },
            new AddinCommandInfo { Name = "write", MutatesDocument = true, ArgumentsSchema = Empty, ResultSchema = Empty,
                Modes = new[] { "preview", "probeRollback", "commit" }, SupportsCancellation = true }
        }};
        public bool Busy, ThrowCleanup, ReloadThrows;
        public Action? Cleanup;
        public int Calls, Disposals;
        public Func<AddinExecutionContext, AddinCommandResult>? Body;
        public Provider? Replacement;
        public AddinProviderInfo Describe() => Info;
        public bool TryEnter(out IDisposable? lease)
        {
            lease = Busy ? null : new Lease(() => { Disposals++; Cleanup?.Invoke(); if (ThrowCleanup) throw new Exception("cleanup failed"); });
            return !Busy;
        }
        public AddinCommandResult Execute(AddinExecutionContext ctx)
        {
            Calls++;
            return Body?.Invoke(ctx) ?? new AddinCommandResult { Status = ctx.Mode == "probeRollback" ? "rolledBack" : "succeeded",
                ModelChanged = ctx.Mode == "commit", ExternalEffects = false,
                TransactionOutcome = ctx.Mode == "commit" ? "committed" : ctx.Mode == "probeRollback" ? "rolledBack" : "none" };
        }
        public IAddinCommandProvider Reload() => ReloadThrows ? throw new Exception("load failed") : Replacement ?? new Provider();
    }
    private sealed class Fixture
    {
        public readonly Provider Provider = new();
        public readonly Scheduler Scheduler = new();
        public readonly AddinRegistry Registry = new();
        public readonly JObject Context = new() { ["instanceId"] = "process-A", ["bridgePort"] = 8080, ["documentPresent"] = true, ["documentToken"] = "doc-A", ["documentGeneration"] = 1 };
        public bool ReadOnly, Enabled = true, Approved = true, InvalidApi;
        public Action? ConfirmAction;
        public DateTime Now = DateTime.UtcNow;
        public List<JObject> Audit = new();
        public AddinOperations Service;
        public Fixture()
        {
            Registry.Register(Provider);
            Service = new(Registry, Scheduler, () => (JObject)Context.DeepClone(), () => ReadOnly, _ => Enabled,
                _ => { ConfirmAction?.Invoke(); return Approved; }, (_, _, _) => { if (InvalidApi) throw new AddinProtocolException("OpenTransaction", "open transaction"); },
                Audit.Add, () => Now);
        }
        public JObject Request(string operation = "write", string mode = "commit") => new() {
            ["requestId"] = Guid.NewGuid().ToString("D"), ["pluginId"] = "test", ["operationName"] = operation, ["mode"] = mode,
            ["arguments"] = new JObject(), ["expectedContext"] = new JObject { ["instanceId"] = "process-A", ["documentToken"] = "doc-A", ["documentGeneration"] = 1, ["pluginGeneration"] = 1 }
        };
        public JObject Submit(JObject request) => Data(Service.Handle("addin_execute", request));
        public JObject Inspect(JObject request, bool cancel = false, int offset = 0, int count = 16384) => Data(Service.Handle(cancel ? "addin_cancel_operation" : "addin_get_operation", new JObject {
            ["instanceId"] = "process-A", ["requestId"] = request["requestId"], ["resultOffset"] = offset, ["resultCount"] = count }));
    }
    private static JObject Data(CortexResult<object> result) { Assert.True(result.Success, result.Error?.Message); return Assert.IsType<JObject>(result.Data); }
    private static void Error(CortexResult<object> result, string reason) { Assert.False(result.Success); Assert.Equal(reason, result.Error!.Context!["reason"]); }

    [Fact] public void CatalogHasActualVersionSchemaAndDocumentIdentity()
    {
        var f = new Fixture(); var data = Data(f.Service.Handle("addin_list", new()));
        Assert.Equal("doc-A", (string?)data["context"]?["documentToken"]);
        Assert.Equal(f.Provider.Info.ModuleId, (string?)data["providers"]?[0]?["moduleId"]);
        Assert.Equal("object", (string?)data["providers"]?[0]?["operations"]?[0]?["argumentsSchema"]?["type"]);
        Assert.Equal("ready", (string?)data["providers"]?[0]?["state"]);
    }
    [Fact] public void AdmissionDoesNotExecute_OneSlot_NoFollowingQueue()
    {
        var f = new Fixture(); var q = f.Request(); Assert.Equal("queued", (string?)f.Submit(q)["state"]); Assert.Equal(0, f.Provider.Calls);
        Error(f.Service.Handle("addin_execute", f.Request()), "Busy");
        f.Scheduler.Drain(); Assert.Equal("succeeded", (string?)f.Inspect(q)["state"]); Assert.Equal(1, f.Provider.Calls);
        Assert.Equal(1, f.Provider.Disposals); Assert.Equal(3, f.Audit.Count);
        Assert.DoesNotContain("arguments", f.Audit[1].ToString());
    }
    [Fact] public void LostReplyAndDuplicateRequestDoNotRepeatWrite_EvenAfterDocumentSwitch()
    {
        var f = new Fixture(); var q = f.Request(); var first = f.Submit(q);
        Assert.Equal(first["operationId"], f.Submit(q)["operationId"]); f.Scheduler.Drain();
        f.Context["documentToken"] = "doc-B"; f.Context["documentGeneration"] = 2;
        Assert.Equal(first["operationId"], f.Submit(q)["operationId"]); Assert.Equal(1, f.Provider.Calls);
        q["mode"] = "preview"; Error(f.Service.Handle("addin_execute", q), "RequestIdConflict");
    }
    [Fact] public void UnknownIdOrDifferentInstanceDoesNotClaimNoWrite()
    {
        var f = new Fixture(); var q = new JObject { ["instanceId"] = "process-A", ["requestId"] = Guid.NewGuid().ToString() };
        Error(f.Service.Handle("addin_get_operation", q), "UnknownOutcome"); q["instanceId"] = "process-B";
        Error(f.Service.Handle("addin_get_operation", q), "InstanceChanged");
        var submit = f.Request(); submit["expectedContext"]!["instanceId"] = "process-B";
        Error(f.Service.Handle("addin_execute", submit), "InstanceChanged"); Assert.Null(f.Scheduler.Work);
    }
    [Theory] [InlineData("unknown", "preview", "UnknownOperation")] [InlineData("read", "commit", "UnsupportedMode")]
    public void UnknownCommandAndModeReject(string name, string mode, string code)
    { var f = new Fixture(); Error(f.Service.Handle("addin_execute", f.Request(name, mode)), code); Assert.Null(f.Scheduler.Work); }
    [Fact] public void WrongArgumentsAndGenerationRejectBeforeScheduling()
    {
        var f = new Fixture(); var q = f.Request(); q["arguments"]!["unexpected"] = true;
        Error(f.Service.Handle("addin_execute", q), "SchemaMismatch"); q["arguments"] = new JObject(); q["expectedContext"]!["pluginGeneration"] = 2;
        Error(f.Service.Handle("addin_execute", q), "PluginChanged"); Assert.Equal(0, f.Provider.Calls);
    }
    [Theory] [InlineData("documentToken")] [InlineData("documentGeneration")]
    public void ContextChangeWhileWaitingNeverInvokes(string key)
    {
        var f = new Fixture(); var q = f.Request(); f.Submit(q); f.Context[key] = key == "documentToken" ? new JValue("doc-B") : new JValue(2);
        f.Scheduler.Drain(); Assert.Equal("DocumentChanged", (string?)f.Inspect(q)["error"]?["code"]); Assert.Equal(0, f.Provider.Calls);
    }
    [Fact] public void ContextChangesDuringApprovalAlsoReject()
    {
        var f = new Fixture(); f.ConfirmAction = () => f.Context["documentGeneration"] = 2;
        var q = f.Request(); f.Submit(q); f.Scheduler.Drain(); Assert.Equal(0, f.Provider.Calls);
        Assert.Equal("DocumentChanged", (string?)f.Inspect(q)["error"]?["code"]);
    }
    [Fact] public void HandlerContextRejectionCompletesAndReleasesReservation()
    {
        var f = new Fixture(); var q = f.Request(); f.Submit(q); f.Scheduler.Reject();
        Assert.Equal("failed", (string?)f.Inspect(q)["state"]); Assert.Equal("ready", (string?)f.Registry.List()[0]["state"]);
        Assert.Equal(0, f.Provider.Calls); Assert.Equal("queued", (string?)f.Submit(f.Request())["state"]);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void SchedulerBusyOrRaiseExceptionDoesNotInvoke(bool throws)
    {
        var f = new Fixture(); f.Scheduler.Busy = true; f.Scheduler.Throw = throws;
        var q = f.Request(); Assert.Equal("failed", (string?)f.Submit(q)["state"]);
        Assert.False(f.Inspect(q).Value<bool>("providerInvoked")); Assert.Equal("ready", (string?)f.Registry.List()[0]["state"]);
        Assert.Equal(0, f.Provider.Calls);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void QueuedCancelOrExpiryNeverExecutesLate(bool cancel)
    {
        var f = new Fixture(); var q = f.Request(); f.Submit(q);
        if (cancel) f.Inspect(q, true); else f.Now = f.Now.AddMinutes(3);
        Assert.Equal(cancel ? "cancelled" : "expired", (string?)f.Inspect(q)["state"]);
        Error(f.Service.Handle("addin_execute", f.Request()), "Busy"); // Event retains its slot until drained.
        f.Scheduler.Drain(); Assert.Equal(0, f.Provider.Calls); Assert.Equal("queued", (string?)f.Submit(f.Request())["state"]);
    }
    [Fact] public async Task StatusAndCooperativeCancelWorkWhileProviderIsBlocked()
    {
        var f = new Fixture(); var entered = new TaskCompletionSource(); var release = new ManualResetEventSlim();
        f.Provider.Body = ctx => { entered.TrySetResult(); ctx.Cancellation.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            release.Wait(TimeSpan.FromSeconds(10)); return new AddinCommandResult { Status = "cancelled", ModelChanged = false, ExternalEffects = false, TransactionOutcome = "rolledBack" }; };
        var q = f.Request(); f.Submit(q); var running = Task.Run(f.Scheduler.Drain); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { Assert.Equal("running", (string?)f.Inspect(q)["state"]); Assert.Equal("cancelRequested", (string?)f.Inspect(q, true)["state"]); }
        finally { release.Set(); }
        await running.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal("cancelled", (string?)f.Inspect(q)["state"]);
    }
    [Fact] public void CancellationBeforeInvocationWorksEvenForNonCancellableCommand()
    {
        var f = new Fixture(); var q = f.Request();
        f.ConfirmAction = () => f.Inspect(q, true);
        f.Submit(q); f.Scheduler.Drain(); Assert.Equal(0, f.Provider.Calls); Assert.Equal("cancelled", (string?)f.Inspect(q)["state"]);
    }
    [Fact] public void CancelAfterCommitDoesNotRewriteSuccess()
    { var f = new Fixture(); var q = f.Request(); f.Submit(q); f.Scheduler.Drain(); Assert.Equal("succeeded", (string?)f.Inspect(q, true)["state"]); }
    [Theory] [InlineData("commit")] [InlineData("probeRollback")] [InlineData("preview")]
    public void ReadOnlyRejectsWriteMetadataInEveryMode(string mode)
    { var f = new Fixture { ReadOnly = true }; Error(f.Service.Handle("addin_execute", f.Request("write", mode)), "ReadOnly"); Assert.Null(f.Scheduler.Work); }
    [Fact] public void ReadOnlyAllowsDeclaredReadOperation()
    { var f = new Fixture { ReadOnly = true }; var q = f.Request("read", "preview"); f.Submit(q); f.Scheduler.Drain(); Assert.Equal("succeeded", (string?)f.Inspect(q)["state"]); }
    [Theory] [InlineData("disabled")] [InlineData("readOnly")] [InlineData("api")] [InlineData("providerBusy")] [InlineData("approval")]
    public void ExecutionRechecksGates(string gate)
    {
        var f = new Fixture(); var q = f.Request(); f.Submit(q);
        if (gate == "disabled") f.Enabled = false; if (gate == "readOnly") f.ReadOnly = true;
        if (gate == "api") f.InvalidApi = true; if (gate == "providerBusy") f.Provider.Busy = true; if (gate == "approval") f.Approved = false;
        f.Scheduler.Drain(); f.Enabled = true; Assert.Equal(0, f.Provider.Calls); Assert.False(f.Inspect(q).Value<bool>("providerInvoked"));
    }
    [Fact] public void ProviderExceptionIsRetainedWithUnknownEffectsAndNoRepeat()
    {
        var f = new Fixture(); f.Provider.Body = _ => throw new InvalidOperationException("bad calculation");
        var q = f.Request(); f.Submit(q); f.Scheduler.Drain(); var result = f.Inspect(q);
        Assert.Equal("failed", (string?)result["state"]); Assert.True(result["error"]!.Value<bool>("effectsMayRemain"));
        Assert.Equal("unknown", (string?)result["transactionOutcome"]); f.Submit(q); Assert.Equal(1, f.Provider.Calls);
    }
    [Fact] public void CleanupExceptionDoesNotReportSuccess()
    { var f = new Fixture(); f.Provider.ThrowCleanup = true; var q = f.Request(); f.Submit(q); f.Scheduler.Drain(); Assert.Equal("ProviderCleanupFailed", (string?)f.Inspect(q)["error"]?["code"]); }
    [Fact] public async Task TerminalResultIsNotVisibleUntilCleanupFinishes()
    {
        var f = new Fixture(); var entered = new TaskCompletionSource(); using var release = new ManualResetEventSlim();
        f.Provider.Cleanup = () => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)); };
        var q = f.Request(); f.Submit(q); var executing = Task.Run(f.Scheduler.Drain); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { Assert.Equal("running", (string?)f.Inspect(q)["state"]); Assert.Null(f.Inspect(q)["finishedUtc"]); }
        finally { release.Set(); }
        await executing; Assert.Equal("succeeded", (string?)f.Inspect(q)["state"]);
    }
    [Theory] [InlineData("committed", true)] [InlineData("rolledBack", true)] [InlineData("none", false)]
    public void ProbeRequiresVerifiedRollbackReceipt(string outcome, bool changed)
    {
        var f = new Fixture(); f.Provider.Body = _ => new() { Status = "rolledBack", TransactionOutcome = outcome, ModelChanged = changed, ExternalEffects = false };
        var q = f.Request("write", "probeRollback"); f.Submit(q); f.Scheduler.Drain(); Assert.Equal("failed", (string?)f.Inspect(q)["state"]);
    }
    [Fact] public void ProbeAcceptsExplicitRollbackReceipt()
    { var f = new Fixture(); var q = f.Request("write", "probeRollback"); f.Submit(q); f.Scheduler.Drain(); Assert.Equal("rolledBack", (string?)f.Inspect(q)["state"]); }
    [Fact] public void InvalidResultReportsFailureButPreservesKnownCommit()
    {
        var f = new Fixture(); f.Provider.Body = _ => new() { Status = "succeeded", TransactionOutcome = "committed", ModelChanged = true, ExternalEffects = false, ResultJson = "{\"wrong\":1}" };
        var q = f.Request(); f.Submit(q); f.Scheduler.Drain(); var result = f.Inspect(q);
        Assert.Equal("SchemaMismatch", (string?)result["error"]?["code"]); Assert.True(result.Value<bool>("modelChanged")); Assert.Equal("committed", (string?)result["transactionOutcome"]);
    }
    [Fact] public void ReloadChangesMvidGeneration_RejectsOldRequestContext()
    {
        var f = new Fixture(); var request = f.Request(); request.Remove("operationName"); request.Remove("mode"); request.Remove("arguments");
        var initial = Data(f.Service.Handle("addin_reload", request)); f.Scheduler.Drain();
        Assert.Equal("succeeded", (string?)f.Inspect(request)["state"]); Assert.Equal(2, f.Registry.List()[0].Value<int>("pluginGeneration"));
        Assert.NotEqual(f.Provider.Info.ModuleId, (string?)f.Registry.List()[0]["moduleId"]);
        Error(f.Service.Handle("addin_execute", f.Request()), "PluginChanged");
        Assert.Equal(initial["operationId"], Data(f.Service.Handle("addin_reload", request))["operationId"]);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void FailedOrUnverifiedReloadDisablesOldHandler(bool sameMvid)
    {
        var f = new Fixture(); f.Provider.ReloadThrows = !sameMvid; f.Provider.Replacement = f.Provider;
        var request = f.Request(); request.Remove("operationName"); request.Remove("mode"); request.Remove("arguments");
        Data(f.Service.Handle("addin_reload", request)); f.Scheduler.Drain();
        Assert.Equal("failed", (string?)f.Inspect(request)["state"]); Assert.Equal("unavailable", (string?)f.Registry.List()[0]["state"]);
        Error(f.Service.Handle("addin_execute", f.Request()), "Unavailable");
    }
    [Fact] public void UnregisterRejectsBusy_StaleDisposeCannotUnregisterReplacement()
    {
        var f = new Fixture(); f.Submit(f.Request()); Assert.Throws<AddinProtocolException>(() => f.Registry.Unregister("test")); f.Scheduler.Drain();
        f.Registry.Unregister("test"); Error(f.Service.Handle("addin_execute", f.Request()), "Unavailable");
        var old = f.Registry.Register(new Provider()); f.Registry.Unregister("test"); f.Registry.Register(new Provider()); old.Dispose();
        Assert.Equal("ready", (string?)f.Registry.List()[0]["state"]);
    }
    [Fact] public void ResultPaginationPreservesUnicode()
    {
        var f = new Fixture(); f.Registry.Unregister("test"); f.Provider.Info.Operations[0].ResultSchema = "{\"type\":\"string\"}";
        f.Registry.Register(f.Provider); f.Provider.Body = _ => new() { Status = "succeeded", TransactionOutcome = "none", ModelChanged = false, ExternalEffects = false, ResultJson = "\"a😀b\"" };
        var q = f.Request("read", "preview"); q["expectedContext"]!["pluginGeneration"] = 2; f.Submit(q); f.Scheduler.Drain();
        var text = ""; int? offset = 0;
        while (offset.HasValue) { var page = f.Inspect(q, offset: offset.Value, count: 1)["resultPage"]!; text += (string?)page["text"]; offset = (int?)page["nextOffset"]; }
        Assert.Equal("a😀b", (string?)JToken.Parse(text));
    }
    [Fact] public void EvictedPayloadRetainsDeduplicationTombstone()
    {
        var f = new Fixture(); var first = f.Request(); f.Submit(first); f.Scheduler.Drain();
        for (int i = 0; i < 129; i++) { f.Submit(f.Request()); f.Scheduler.Drain(); }
        Assert.True(f.Submit(first).Value<bool>("resultExpired")); Assert.Equal(130, f.Provider.Calls); Assert.Null(f.Scheduler.Work);
    }
    [Theory]
    [InlineData("{\"type\":\"string\",\"maxLength\":-1}")]
    [InlineData("{\"type\":\"object\",\"additionalProperties\":true}")]
    [InlineData("{\"type\":\"array\"}")]
    [InlineData("{\"type\":\"number\",\"minimum\":\"0\"}")]
    [InlineData("{\"type\":\"string\",\"minimum\":0}")]
    [InlineData("{\"type\":\"string\",\"pattern\":\"x\"}")]
    public void UnsupportedOrMalformedSchemaFailsRegistration(string schema)
    { var p = new Provider(); p.Info.Operations[0].ResultSchema = schema; Assert.Throws<AddinProtocolException>(() => new AddinRegistry().Register(p)); }
    [Fact] public void IncompatibleContractAndDuplicateJsonAreRejected()
    {
        var p = new Provider(); p.Info.ApiVersion = 2; var registry = new AddinRegistry();
        Assert.Equal("Incompatible", Assert.Throws<AddinProtocolException>(() => registry.Register(p)).Code);
        Assert.Equal("incompatible", (string?)registry.List()[0]["state"]);
        Assert.ThrowsAny<Exception>(() => AddinJson.Parse("{\"x\":1,\"x\":2}"));
    }
}
