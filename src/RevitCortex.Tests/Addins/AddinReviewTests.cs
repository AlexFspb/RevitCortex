using Newtonsoft.Json.Linq;
using RevitCortex.Core.Addins;
using Xunit;

namespace RevitCortex.Tests.Addins;

public partial class AddinOperationsTests
{
    [Theory]
    [InlineData("failed", "rolledBack")]
    [InlineData("cancelled", "rolledBack")]
    [InlineData("failed", "none")]
    [InlineData("cancelled", "none")]
    public void ProbePreservesFailureOrCancellationWithNoRemainingEffects(string status, string outcome)
    {
        var f = new Fixture();
        f.Provider.Body = _ => new() { Status = status, TransactionOutcome = outcome, ModelChanged = false, ExternalEffects = false,
            ResultJson = "{\"restored\":true}", DiagnosticsJson = "[{\"code\":\"WarningRollback\",\"elementIds\":[42]}]" };
        var q = f.Request("write", "probeRollback"); f.Submit(q); f.Scheduler.Drain(); var result = f.Inspect(q);
        Assert.Equal(status, (string?)result["state"]); Assert.Equal(outcome, (string?)result["transactionOutcome"]);
        Assert.Null(result["error"]); Assert.False(result.Value<bool>("modelChanged"));
        Assert.Equal("WarningRollback", (string?)result["diagnostics"]?[0]?["code"]);
        Assert.True(JObject.Parse((string)result["resultPage"]!["text"]!).Value<bool>("restored"));
    }

    [Theory]
    [InlineData("failed", "committed", false, false)]
    [InlineData("cancelled", "unknown", false, false)]
    [InlineData("failed", "rolledBack", true, false)]
    [InlineData("failed", "rolledBack", false, true)]
    [InlineData("cancelled", "none", true, false)]
    public void ProbeStillRejectsUnprovenRollbackOrRemainingEffects(string status, string outcome, bool model, bool external)
    {
        var f = new Fixture(); f.Provider.Body = _ => new() { Status = status, TransactionOutcome = outcome, ModelChanged = model, ExternalEffects = external };
        var q = f.Request("write", "probeRollback"); f.Submit(q); f.Scheduler.Drain();
        Assert.Equal("EffectsMismatch", (string?)f.Inspect(q)["error"]?["code"]);
    }

    [Fact] public void BusyDoesNotConsumeUuidOrHistoryCapacity_IdenticalRequestCanBeAdmittedLater()
    {
        var f = new Fixture(); f.Scheduler.Busy = true; var q = f.Request();
        for (int i = 0; i < 10001; i++) Error(f.Service.Handle("addin_execute", f.Request()), "Busy");
        Error(f.Service.Handle("addin_execute", q), "Busy");
        Error(f.Service.Handle("addin_get_operation", new JObject { ["instanceId"] = "process-A", ["requestId"] = q["requestId"] }), "UnknownOutcome");
        Assert.Empty(f.Audit); Assert.Equal("ready", (string?)f.Registry.List()[0]["state"]); Assert.Equal(0, f.Provider.Calls);
        f.Scheduler.Busy = false; f.Submit(q); f.Scheduler.Drain();
        Assert.Equal("succeeded", (string?)f.Inspect(q)["state"]); f.Submit(q); Assert.Equal(1, f.Provider.Calls);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void UuidLookupAndCancellationIgnoreHexLetterCase(bool useOperationId)
    {
        var f = new Fixture(); var q = f.Request(); q["requestId"] = "ABCDEFAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE";
        var admitted = f.Submit(q);
        var lookup = new JObject { ["instanceId"] = "process-A" };
        var key = useOperationId ? "operationId" : "requestId"; lookup[key] = ((string)admitted[key]!).ToUpperInvariant();
        Assert.Equal(admitted["operationId"], Data(f.Service.Handle("addin_get_operation", lookup))["operationId"]);
        Assert.Equal("cancelled", (string?)Data(f.Service.Handle("addin_cancel_operation", lookup))["state"]);
        f.Scheduler.Drain(); Assert.Equal(0, f.Provider.Calls); Assert.Equal(admitted["operationId"], f.Submit(q)["operationId"]);
    }

    [Theory] [InlineData("requestId")] [InlineData("operationId")]
    public void InvalidUuidIsAValidationError(string key)
    {
        var f = new Fixture();
        Error(f.Service.Handle("addin_get_operation", new JObject { ["instanceId"] = "process-A", [key] = "not-a-uuid" }), "InvalidRequest");
    }

    // This is a replaceable module, not the stable loader. Taking its gate is a host bug.
    private sealed class CommandModule : IAddinCommandProvider
    {
        public readonly Provider Implementation = new();
        public AddinProviderInfo Describe() => Implementation.Describe();
        public bool TryEnter(out IDisposable? lease) => throw new Exception("Replacement module must use the stable loader's gate");
        public AddinCommandResult Execute(AddinExecutionContext context) => Implementation.Execute(context);
    }
    private static JObject ReloadRequest(Fixture f, int generation)
    {
        var q = f.Request(); q.Remove("operationName"); q.Remove("mode"); q.Remove("arguments");
        q["expectedContext"]!["pluginGeneration"] = generation; return q;
    }

    [Fact] public void MultipleReloadsKeepOriginalStableLoaderAndGate_WhenModulesAreNotReloadable()
    {
        var f = new Fixture(); var first = new CommandModule(); f.Provider.Replacement = first;
        var r1 = ReloadRequest(f, 1); Data(f.Service.Handle("addin_reload", r1)); f.Scheduler.Drain();
        Assert.True(f.Registry.List()[0].Value<bool>("supportsReload"));
        var read = f.Request("read", "preview"); read["expectedContext"]!["pluginGeneration"] = 2;
        f.Submit(read); f.Scheduler.Drain(); Assert.Equal("succeeded", (string?)f.Inspect(read)["state"]);
        Assert.Equal(1, first.Implementation.Calls); Assert.Equal(0, f.Provider.Calls);
        var second = new CommandModule(); f.Provider.Replacement = second;
        var r2 = ReloadRequest(f, 2); Data(f.Service.Handle("addin_reload", r2)); f.Scheduler.Drain();
        Assert.Equal("succeeded", (string?)f.Inspect(r2)["state"]);
        Assert.Equal(3, f.Registry.List()[0].Value<int>("pluginGeneration"));
        Assert.Equal(second.Implementation.Info.ModuleId, (string?)f.Registry.List()[0]["moduleId"]);
        Assert.Equal(2, f.Provider.ReloadCalls); Assert.Equal(3, f.Provider.GateEntries); Assert.Equal(3, f.Provider.Disposals);
        Assert.Equal(first.Implementation.Info.ModuleId, (string?)JObject.Parse((string)f.Inspect(r1)["resultPage"]!["text"]!)["moduleId"]);
        f.Registration.Dispose(); Assert.Equal("unavailable", (string?)f.Registry.List()[0]["state"]);
        Assert.False(f.Registry.List()[0].Value<bool>("supportsReload"));
    }

    [Fact] public void StableLoaderFailureAfterSuccessfulReloadDisablesCurrentModule()
    {
        var f = new Fixture(); var module = new CommandModule(); f.Provider.Replacement = module;
        Data(f.Service.Handle("addin_reload", ReloadRequest(f, 1))); f.Scheduler.Drain(); f.Provider.ReloadThrows = true;
        var r2 = ReloadRequest(f, 2); Data(f.Service.Handle("addin_reload", r2)); f.Scheduler.Drain();
        Assert.Equal("failed", (string?)f.Inspect(r2)["state"]); Assert.Equal("unavailable", (string?)f.Registry.List()[0]["state"]);
        Assert.False(f.Registry.List()[0].Value<bool>("supportsReload")); Assert.Equal(0, module.Implementation.Calls);
    }

    [Fact] public void UnregistrationIsOnlyAvailableThroughOwnedRegistrationHandle()
    { Assert.Null(typeof(AddinRegistry).GetMethod("Unregister", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)); }

    [Fact] public void FailedPostExecutionCheckRetainsReportedCommitAndDiagnosticsWithoutCertifyingEffects()
    {
        var f = new Fixture(); f.Provider.Body = _ => { f.InvalidApi = true; return new() {
            Status = "succeeded", TransactionOutcome = "committed", ModelChanged = true, ExternalEffects = false,
            DiagnosticsJson = "[{\"code\":\"CommittedFirstTransaction\",\"elementIds\":[42]}]" }; };
        var q = f.Request(); f.Submit(q); f.Scheduler.Drain(); var result = f.Inspect(q);
        Assert.Equal("failed", (string?)result["state"]); Assert.Equal("OpenTransaction", (string?)result["error"]?["code"]);
        Assert.False(result.Value<bool>("postExecutionCheckPassed")); Assert.Equal("unknown", (string?)result["transactionOutcome"]);
        Assert.Equal("committed", (string?)result["providerReceipt"]?["transactionOutcome"]);
        Assert.True(result["providerReceipt"]!.Value<bool>("modelChanged"));
        Assert.Equal("CommittedFirstTransaction", (string?)result["providerReceipt"]?["diagnostics"]?[0]?["code"]);
        Assert.True(result["error"]!.Value<bool>("effectsMayRemain"));
    }

    [Theory] [InlineData(-1, 100)] [InlineData(0, 0)] [InlineData(0, 65537)]
    public void InvalidPageRequestCannotCancelQueuedOperation(int offset, int count)
    {
        var f = new Fixture(); var q = f.Request(); f.Submit(q);
        var bad = new JObject { ["instanceId"] = "process-A", ["requestId"] = q["requestId"], ["resultOffset"] = offset, ["resultCount"] = count };
        Error(f.Service.Handle("addin_cancel_operation", bad), "InvalidRequest"); Assert.Equal("queued", (string?)f.Inspect(q)["state"]);
        f.Scheduler.Drain(); Assert.Equal("succeeded", (string?)f.Inspect(q)["state"]);
    }

    [Fact] public async Task ResultDependentPageValidationAlsoPrecedesCancellation()
    {
        var f = new Fixture(); var cleaning = new TaskCompletionSource(); using var release = new ManualResetEventSlim();
        f.Provider.Cleanup = () => { cleaning.SetResult(); release.Wait(TimeSpan.FromSeconds(5)); };
        var q = f.Request(); f.Submit(q); var task = Task.Run(f.Scheduler.Drain); await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Error(f.Service.Handle("addin_cancel_operation", new JObject { ["instanceId"] = "process-A", ["requestId"] = q["requestId"], ["resultOffset"] = 999 }), "InvalidRequest");
            Assert.Equal("running", (string?)f.Inspect(q)["state"]);
        }
        finally { release.Set(); }
        await task;
    }

    [Fact] public void CancellationExceptionAloneDoesNotClaimRollback()
    {
        var f = new Fixture(); f.Provider.Body = _ => throw new OperationCanceledException("cancelled without receipt");
        var q = f.Request(); f.Submit(q); f.Scheduler.Drain(); var result = f.Inspect(q);
        Assert.Equal("failed", (string?)result["state"]); Assert.Equal("unknown", (string?)result["transactionOutcome"]);
        Assert.True(result["error"]!.Value<bool>("effectsMayRemain"));
    }

    [Fact] public void StableGateIsHeldDuringManualApprovalAndReleasedOnDecline()
    {
        var f = new Fixture(); f.Approved = false; f.ConfirmAction = () => {
            Assert.True(f.Provider.LeaseHeld); Assert.False(f.Provider.TryEnter(out var lease)); Assert.Null(lease);
        };
        var q = f.Request(); f.Submit(q); f.Scheduler.Drain();
        Assert.Equal("cancelled", (string?)f.Inspect(q)["state"]); Assert.False(f.Provider.LeaseHeld); Assert.Equal(0, f.Provider.Calls);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void OmittedAndExplicitDefaultTimeoutHaveSameIdentity(bool reload)
    {
        var f = new Fixture(); var q = reload ? ReloadRequest(f, 1) : f.Request(); var method = reload ? "addin_reload" : "addin_execute";
        var first = Data(f.Service.Handle(method, q)); q["queueTimeoutSeconds"] = 30;
        Assert.Equal(first["operationId"], Data(f.Service.Handle(method, q))["operationId"]);
        f.Scheduler.Drain(); Assert.Equal(first["operationId"], Data(f.Service.Handle(method, q))["operationId"]);
        q["queueTimeoutSeconds"] = 31; Error(f.Service.Handle(method, q), "RequestIdConflict");
    }
}
