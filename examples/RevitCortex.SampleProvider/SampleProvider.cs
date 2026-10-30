using System;
using System.Linq;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Addins;

namespace RevitCortex.SampleProvider;

// Example only. Not installed or enabled by Cortex deployment.
public sealed class App : IExternalApplication
{
    private IDisposable? _registration;
    public Result OnStartup(UIControlledApplication app)
    {
        _registration = AddinRegistry.Shared.Register(new Provider());
        return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication app) { _registration?.Dispose(); return Result.Succeeded; }
}

public sealed class Provider : IAddinCommandProvider
{
    private int _busy;
    private readonly string _loadedHash = HashLoadedImage();
    private static string HashLoadedImage()
    {
        var image = File.ReadAllBytes(typeof(Provider).Assembly.Location);
        using var stream = new MemoryStream(image);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != typeof(Provider).Module.ModuleVersionId)
            throw new InvalidOperationException("File was replaced after module load; cannot report its hash as the loaded image.");
        return Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
    }
    private const string Empty = "{\"type\":\"object\",\"additionalProperties\":false}";
    private const string Arguments = """
        {"type":"object","additionalProperties":false,"required":["expectedTestDocumentTitle","value"],
        "properties":{"expectedTestDocumentTitle":{"type":"string","minLength":12},"value":{"type":"string","maxLength":100},
        "injectWarning":{"type":"boolean"}}}
        """;
    private const string WriteResult = """
        {"type":"object","additionalProperties":false,"required":["before","after","requested","restored"],
        "properties":{"before":{"type":"string"},"after":{"type":"string"},"requested":{"type":"string"},"restored":{"type":"boolean"}}}
        """;

    public AddinProviderInfo Describe() => new()
    {
        PluginId = "cortex-test", Name = "Cortex integration test provider", LoadedVersion = typeof(Provider).Assembly.GetName().Version!.ToString(),
        ModuleId = typeof(Provider).Module.ModuleVersionId.ToString("D"),
        LoadedModuleSha256 = _loadedHash,
        Operations = new[]
        {
            new AddinCommandInfo { Name = "test.element_count", ArgumentsSchema = Empty, ResultSchema = "{\"type\":\"integer\",\"minimum\":0}" },
            new AddinCommandInfo { Name = "test.set_project_number", ArgumentsSchema = Arguments, ResultSchema = WriteResult,
                MutatesDocument = true, SupportsCancellation = true, Modes = new[] { "preview", "probeRollback", "commit" } }
        }
    };
    public bool TryEnter(out IDisposable? lease)
    {
        lease = Interlocked.CompareExchange(ref _busy, 1, 0) == 0 ? new Release(() => Volatile.Write(ref _busy, 0)) : null;
        return lease != null;
    }
    private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }

    public AddinCommandResult Execute(AddinExecutionContext context)
    {
        var app = (UIApplication)context.RevitApplication!;
        var doc = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");
        if (context.OperationName == "test.element_count")
        {
            using var elements = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            return new() { Status = "succeeded", TransactionOutcome = "none", ModelChanged = false, ExternalEffects = false,
                ResultJson = JsonConvert.SerializeObject(elements.GetElementCount()) };
        }
        if (context.OperationName != "test.set_project_number") throw new InvalidOperationException("Unknown command.");
        var input = JObject.Parse(context.ArgumentsJson);
        if (doc.IsFamilyDocument || !doc.Title.StartsWith("CORTEX_TEST_", StringComparison.Ordinal)
            || doc.Title != input.Value<string>("expectedTestDocumentTitle"))
            throw new InvalidOperationException("Writes require an explicitly named project whose title starts CORTEX_TEST_. Working models are forbidden.");
        if (doc.IsModifiable || doc.IsReadOnly) throw new InvalidOperationException("Document is not ready for provider-owned transactions.");
        var parameter = doc.ProjectInformation.get_Parameter(BuiltInParameter.PROJECT_NUMBER);
        if (parameter == null || parameter.IsReadOnly) throw new InvalidOperationException("Project Number is not writable.");
        var before = parameter.AsString() ?? "";
        var value = input.Value<string>("value")!;
        var failures = new CaptureFailures();
        AddinCommandResult Receipt(string state, string outcome, bool changed, bool restored) => new()
        {
            Status = state, TransactionOutcome = outcome, ModelChanged = changed, ExternalEffects = false,
            ResultJson = new JObject { ["before"] = before, ["after"] = parameter.AsString() ?? "", ["requested"] = value, ["restored"] = restored }.ToString(Formatting.None),
            DiagnosticsJson = failures.Messages.ToString(Formatting.None)
        };
        if (context.Mode == "preview") return Receipt("succeeded", "none", false, true);
        if (context.Cancellation.IsCancellationRequested) return Receipt("cancelled", "none", false, true);
        context.ReportStage("Checking a bounded Project Number transaction");
        using var group = new TransactionGroup(doc, "Cortex explicit test document");
        if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("TransactionGroup did not start.");
        var final = false;
        try
        {
            using (var tx = new Transaction(doc, "Cortex test Project Number"))
            {
                if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Transaction did not start.");
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                if (!parameter.Set(value)) throw new InvalidOperationException("Set returned false.");
                if (input.Value<bool?>("injectWarning") == true)
                {
                    // Deterministic native warning for T11. No invalid model geometry is constructed.
                    var warning = new FailureMessage(BuiltInFailures.OverlapFailures.DuplicateInstances);
                    warning.SetFailingElement(doc.ProjectInformation.Id);
                    doc.PostFailure(warning);
                }
                var status = tx.Commit();
                if (status == TransactionStatus.Pending) throw new InvalidOperationException("Unexpected pending transaction; result is not successful.");
                if (status != TransactionStatus.Committed)
                {
                    if (group.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("Group rollback failed.");
                    final = true;
                    if ((parameter.AsString() ?? "") != before) throw new InvalidOperationException("Rollback verification failed.");
                    return Receipt("failed", "rolledBack", false, true);
                }
            }
            if ((parameter.AsString() ?? "") != value) throw new InvalidOperationException("Written value does not match.");
            if (context.Mode == "probeRollback" || context.Cancellation.IsCancellationRequested)
            {
                if (group.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("Group rollback failed.");
                final = true;
                if ((parameter.AsString() ?? "") != before) throw new InvalidOperationException("Rollback verification failed.");
                return Receipt(context.Cancellation.IsCancellationRequested ? "cancelled" : "rolledBack", "rolledBack", false, true);
            }
            // Serialize/validate result before finalizing the group, so a malformed response can still roll back.
            var result = Receipt("succeeded", "committed", value != before, false);
            AddinJson.Validate(AddinJson.Parse(result.ResultJson), AddinJson.Parse(WriteResult));
            if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("Group did not commit.");
            final = true;
            return result;
        }
        catch (Exception ex)
        {
            failures.Messages.Add(new JObject { ["code"] = "SampleException", ["message"] = ex.Message });
            if (!final && group.GetStatus() == TransactionStatus.Started && group.RollBack() == TransactionStatus.RolledBack
                && (parameter.AsString() ?? "") == before)
            { final = true; return Receipt("failed", "rolledBack", false, true); }
            throw; // Unknown effect, never fabricate rollback.
        }
        finally
        {
            if (!final && group.GetStatus() == TransactionStatus.Started) group.RollBack();
        }
    }

    private sealed class CaptureFailures : IFailuresPreprocessor
    {
        public JArray Messages { get; } = new();
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var failures = accessor.GetFailureMessages();
            foreach (var failure in failures)
                Messages.Add(new JObject { ["message"] = failure.GetDescriptionText(), ["severity"] = failure.GetSeverity().ToString(),
                    ["failureDefinitionId"] = failure.GetFailureDefinitionId().Guid.ToString("D"),
                    ["elementIds"] = new JArray(failure.GetFailingElementIds().Select(id => id.Value)) });
            return failures.Count == 0 ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
