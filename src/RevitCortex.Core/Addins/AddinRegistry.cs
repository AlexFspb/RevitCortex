using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Core.Addins;

/// <summary>One stable registry per process. Registration works before or after Cortex startup.</summary>
public sealed class AddinRegistry
{
    public static AddinRegistry Shared { get; } = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    internal sealed class Entry
    {
        public IAddinCommandProvider? Provider;
        public AddinProviderInfo Info = new();
        public long Generation;
        public bool InUse;
        public bool Incompatible;
    }
    public IDisposable Register(IAddinCommandProvider provider)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        var original = provider.Describe() ?? throw new AddinProtocolException("InvalidProvider", "Missing metadata.");
        AddinProviderInfo info;
        try { info = Validate(original); }
        catch (AddinProtocolException ex) when (ex.Code == "Incompatible" && Regex.IsMatch(original.PluginId ?? "", "^[a-z][a-z0-9.-]{0,79}$"))
        {
            lock (_gate)
            {
                var id = original.PluginId!; // The catch filter validated a nonempty ID.
                _entries.TryGetValue(id, out var old);
                if (old?.Provider == null && old?.InUse != true)
                    _entries[id] = new Entry { Incompatible = true, Generation = (old?.Generation ?? 0) + 1,
                        Info = new AddinProviderInfo { PluginId = id, Name = original.Name, ApiVersion = original.ApiVersion,
                            LoadedVersion = original.LoadedVersion, ModuleId = original.ModuleId } };
            }
            throw;
        }
        lock (_gate)
        {
            _entries.TryGetValue(info.PluginId, out var old);
            if (old?.Provider != null || old?.InUse == true) throw new AddinProtocolException("AlreadyRegistered", info.PluginId);
            var entry = new Entry { Provider = provider, Info = info, Generation = (old?.Generation ?? 0) + 1 };
            _entries[info.PluginId] = entry;
            return new Registration(this, entry);
        }
    }
    private sealed class Registration(AddinRegistry owner, Entry registered) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate)
                if (owner._entries.TryGetValue(registered.Info.PluginId, out var current) && ReferenceEquals(current, registered))
                    owner.Unregister(registered.Info.PluginId);
        }
    }
    public void Unregister(string id)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out var entry)) return;
            if (entry.InUse) throw new AddinProtocolException("Busy", "Drain/cancel the operation before unloading this provider.");
            entry.Provider = null;
        }
    }
    internal Entry Find(string id)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(id, out var entry) && entry.Incompatible) throw new AddinProtocolException("Incompatible", "Provider requires an unsupported contract: " + id);
            if (entry == null || entry.Provider == null) throw new AddinProtocolException("Unavailable", "Provider is not registered: " + id);
            return entry;
        }
    }
    internal void Reserve(Entry entry, long generation)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(entry.Info.PluginId, out var current) || !ReferenceEquals(current, entry) || entry.Provider == null || entry.Generation != generation) throw new AddinProtocolException("PluginChanged", "Inspect the currently registered provider again.");
            if (entry.InUse) throw new AddinProtocolException("Busy", "Provider already owns an operation.");
            entry.InUse = true;
        }
    }
    internal void Release(Entry entry) { lock (_gate) entry.InUse = false; }
    internal void Reload(Entry entry)
    {
        // Only the owning operation may call this, in Revit API context.
        var previousId = entry.Info.ModuleId;
        try
        {
            var replacement = ((IReloadableAddinProvider)entry.Provider!).Reload();
            var info = Validate(replacement.Describe());
            if (info.PluginId != entry.Info.PluginId || info.ModuleId == previousId)
                throw new AddinProtocolException("ReloadNotVerified", "Loader did not report a different loaded module for the same provider.");
            lock (_gate) { entry.Provider = replacement; entry.Info = info; entry.Generation++; }
        }
        catch
        {
            // A failed loader may already have unloaded the old module. Never keep calling it.
            lock (_gate) { entry.Provider = null; entry.Generation++; }
            throw;
        }
    }
    public JArray List()
    {
        lock (_gate) return new JArray(_entries.Values.Select(Describe));
    }
    internal static JObject Describe(Entry e) => new()
    {
        ["pluginId"] = e.Info.PluginId, ["name"] = e.Info.Name, ["apiVersion"] = e.Info.ApiVersion,
        ["loadedVersion"] = e.Info.LoadedVersion, ["moduleId"] = e.Info.ModuleId,
        ["buildId"] = "mvid:" + e.Info.ModuleId, ["loadedModuleSha256"] = e.Info.LoadedModuleSha256,
        ["pluginGeneration"] = e.Generation, ["state"] = e.Incompatible ? "incompatible" : e.Provider == null ? "unavailable" : e.InUse ? "busy" : "ready",
        ["supportsReload"] = e.Provider is IReloadableAddinProvider,
        ["operations"] = new JArray(e.Info.Operations.Select(o => new JObject
        {
            ["operationName"] = o.Name, ["schemaVersion"] = o.SchemaVersion,
            ["argumentsSchema"] = AddinJson.Parse(o.ArgumentsSchema), ["resultSchema"] = AddinJson.Parse(o.ResultSchema),
            ["requiresDocument"] = o.RequiresDocument, ["mutatesDocument"] = o.MutatesDocument,
            ["externalEffects"] = o.ExternalEffects, ["modes"] = new JArray(o.Modes),
            ["supportsCancellation"] = o.SupportsCancellation, ["transactionOwner"] = o.TransactionOwner
        }))
    };
    private static AddinProviderInfo Validate(AddinProviderInfo original)
    {
        if (original == null) throw new AddinProtocolException("InvalidProvider", "Missing metadata.");
        var info = JsonConvert.DeserializeObject<AddinProviderInfo>(JsonConvert.SerializeObject(original))!;
        if (info.ApiVersion != 1) throw new AddinProtocolException("Incompatible", "Cortex add-in contract API 1 is required.");
        if (!Regex.IsMatch(info.PluginId ?? "", "^[a-z][a-z0-9.-]{0,79}$") || string.IsNullOrWhiteSpace(info.LoadedVersion) || !Guid.TryParse(info.ModuleId, out _))
            throw new AddinProtocolException("InvalidProvider", "Provider ID, loaded version and actual module MVID are required.");
        if (info.LoadedModuleSha256 != null && !Regex.IsMatch(info.LoadedModuleSha256, "^[a-fA-F0-9]{64}$"))
            throw new AddinProtocolException("InvalidProvider", "loadedModuleSha256 must be a SHA-256 hex value or null.");
        info.ModuleId = Guid.Parse(info.ModuleId).ToString("D");
        info.LoadedModuleSha256 = info.LoadedModuleSha256?.ToLowerInvariant();
        if (info.Operations == null || info.Operations.Length > 128 || info.Operations.Select(o => o.Name).Distinct().Count() != info.Operations.Length)
            throw new AddinProtocolException("InvalidProvider", "Operations must be unique, at most 128.");
        foreach (var op in info.Operations)
        {
            if (!Regex.IsMatch(op.Name ?? "", "^[a-z][a-z0-9._-]{0,79}$") || op.SchemaVersion < 1 || op.TransactionOwner != "provider")
                throw new AddinProtocolException("InvalidProvider", "Named/versioned operations with provider-owned transactions are required in API 1.");
            if (op.Modes == null || op.Modes.Length == 0 || op.Modes.Any(m => m != "preview" && m != "probeRollback" && m != "commit"))
                throw new AddinProtocolException("InvalidProvider", "Invalid operation modes.");
            if (op.MutatesDocument && !op.RequiresDocument) throw new AddinProtocolException("InvalidProvider", "Model writes must require an explicit document.");
            if (op.ExternalEffects && op.Modes.Contains("probeRollback")) throw new AddinProtocolException("InvalidProvider", "External writes are not supported in probeRollback.");
            var arguments = AddinJson.Parse(op.ArgumentsSchema);
            AddinJson.CheckSchema(arguments);
            if ((string?)arguments["type"] != "object") throw new AddinProtocolException("InvalidSchema", "Arguments schema must be an object.");
            AddinJson.CheckSchema(AddinJson.Parse(op.ResultSchema));
        }
        return info;
    }
}
