using System;
using System.Threading;
using RevitCortex.Core.Caching;
using RevitCortex.Core.Discovery;

namespace RevitCortex.Core.Session;

/// <summary>
/// Facade passed to every tool. Provides access to shared state,
/// document capabilities, and detected locale. Does NOT hold a
/// direct Revit Document reference — that lives in the Plugin layer.
/// Core has no Revit dependency.
/// </summary>
public class CortexSession
{
    public ISessionStore Store { get; }
    public DocumentCapabilities Capabilities { get; private set; }
    public string DetectedLocale { get; private set; }
    /// <summary>Actual plugin listener port; independent of the document store.</summary>
    public int? BridgePort { get; set; }

    /// <summary>
    /// Tool-result cache. Always non-null. Plugin wires invalidation to Revit
    /// document events; in tests a default cache is created automatically.
    /// </summary>
    public IToolResultCache Cache { get; }

    /// <summary>
    /// Monotonic counter, bumped on each Revit DocumentChanged. Read by the
    /// router when consulting <see cref="Cache"/>; bumped by the Plugin's
    /// DocumentChangeWatcher. Tests can bump it directly via <see cref="BumpDocumentVersion"/>.
    /// </summary>
    public long DocumentVersion => Interlocked.Read(ref _documentVersion);
    private long _documentVersion;
    private readonly object _documentContextLock = new();
    private long _documentContextGeneration;
    private string? _documentTitle;

    // Set on the Revit UI thread; background audit code reads only this string.
    public void UpdateDocumentTitle(string? title)
    {
        lock (_documentContextLock) _documentTitle = title;
    }

    public readonly struct DocumentContext
    {
        public long Generation { get; }
        public object? Document { get; }
        public string? Title { get; }
        public DocumentContext(long generation, object? document, string? title = null)
        {
            Generation = generation;
            Document = document;
            Title = title;
        }
    }

    public DocumentContext CaptureDocumentContext()
    {
        lock (_documentContextLock)
            return new(_documentContextGeneration, Store.Get<object>("activeDocument"), _documentTitle);
    }

    public bool IsCurrentDocumentContext(DocumentContext context)
    {
        lock (_documentContextLock)
            // Lifecycle comparisons happen on the Revit UI thread. Worker threads
            // compare only the generation and never invoke Document.Equals.
            return context.Generation == _documentContextGeneration;
    }

    /// <summary>
    /// Atomically increment <see cref="DocumentVersion"/>. Returns the new value.
    /// </summary>
    public long BumpDocumentVersion() => Interlocked.Increment(ref _documentVersion);

    /// <summary>
    /// Confirmation callback for normal destructive operations.
    /// Parameters: (actionVerb, elementCount, description) -> bool?
    /// null means "Yes to All" and arms the 120-second ApproveAll window.
    /// If the callback itself is null, the normal operation proceeds without confirmation.
    /// </summary>
    public Func<string, int, string?, bool?>? ConfirmAction { get; set; }

    /// <summary>
    /// Confirmation callback for critical operations such as custom C# execution.
    /// Critical requests never consume the generic ApproveAll or AutoMode flags and
    /// fail closed when no callback exists. The Plugin callback may provide its own
    /// explicit UI policy; the Revit 2026 fork uses a visible session-only 3-second
    /// auto-run countdown inside that critical confirmation window.
    /// </summary>
    public Func<string, int, string?, bool?>? CriticalConfirmAction { get; set; }

    /// <summary>
    /// When true, normal subsequent confirmations are auto-approved until timeout.
    /// Set by "Yes to All" in the normal confirmation dialog. Expires after 120 seconds.
    /// The flag+timestamp pair must be read/written as a unit.
    /// </summary>
    public bool ApproveAll
    {
        get
        {
            lock (_approveAllLock)
            {
                return _approveAll && (DateTime.UtcNow - _approveAllTimestamp).TotalSeconds < 120;
            }
        }
        set
        {
            lock (_approveAllLock)
            {
                _approveAll = value;
                if (value) _approveAllTimestamp = DateTime.UtcNow;
            }
        }
    }
    private readonly object _approveAllLock = new();
    private bool _approveAll;
    private DateTime _approveAllTimestamp;

    /// <summary>
    /// Generic Auto mode for normal destructive confirmations. This is separate
    /// from the critical C# confirmation window's session-only "Allow auto-run"
    /// option. Critical requests intentionally do not read this flag.
    /// </summary>
    public bool AutoMode
    {
        get { lock (_approveAllLock) { return _autoMode; } }
        set { lock (_approveAllLock) { _autoMode = value; } }
    }
    private bool _autoMode;

    /// <summary>
    /// Raised every time a normal destructive operation is auto-approved because
    /// generic AutoMode is on. Core stays Revit-agnostic so UI layers can update status.
    /// </summary>
    public event Action? AutoModeActivity;

    public CortexSession(ISessionStore store)
        : this(store, new ToolResultCache())
    {
    }

    public CortexSession(ISessionStore store, IToolResultCache cache)
    {
        Store = store;
        Cache = cache;
        Capabilities = new DocumentCapabilities();
        DetectedLocale = "en";
    }

    public void Reinitialize(DocumentCapabilities capabilities, string locale, object? document = null)
    {
        lock (_documentContextLock)
        {
            _documentContextGeneration++;
            _documentTitle = null;
            Store.Clear();
            Capabilities = capabilities;
            DetectedLocale = locale;
            Cache.InvalidateAll();
            BumpDocumentVersion();
            AutoMode = false;
            ApproveAll = false;
            if (document != null) Store.Set("activeDocument", document);
        }
    }

    /// <summary>
    /// Ask for confirmation before a destructive operation.
    /// Normal requests may use the 120-second ApproveAll window or generic AutoMode.
    /// Critical requests bypass both generic modes, require CriticalConfirmAction,
    /// and return exactly the decision produced by that callback.
    /// </summary>
    /// <param name="action">Action verb: "delete", "rename", "replace compound structure", etc.</param>
    /// <param name="elementCount">Number of elements affected.</param>
    /// <param name="description">Optional detailed description of what will happen.</param>
    /// <param name="critical">If true, bypasses generic ApproveAll/AutoMode and fails closed if no critical callback is available.</param>
    public bool RequestConfirmation(
        string action,
        int elementCount,
        string? description = null,
        bool critical = false)
    {
        var request = ToolRequestLifetime.Current;
        request?.BeginConfirmation();
        var approved = RequestConfirmationCore(action, elementCount, description, critical);
        request?.FinishConfirmation();
        return approved;
    }

    private bool RequestConfirmationCore(string action, int elementCount, string? description, bool critical)
    {
        if (elementCount <= 0) return true;

        if (!critical && AutoMode)
        {
            AutoModeActivity?.Invoke();
            return true;
        }

        if (!critical && ApproveAll) return true;

        if (critical)
        {
            if (CriticalConfirmAction == null) return false;
            var criticalResult = CriticalConfirmAction.Invoke(action, elementCount, description);
            return criticalResult == true;
        }

        var result = ConfirmAction?.Invoke(action, elementCount, description);
        if (result == null)
        {
            ApproveAll = true;
            return true;
        }

        if (result == false) return false;

        // ConfirmationHelper may set generic AutoMode directly when its Auto command is clicked.
        if (AutoMode) return true;

        return result.Value;
    }

    /// <summary>
    /// Resets generic normal-operation approval modes. Critical confirmation state
    /// belongs to the Plugin UI and is intentionally not represented by these flags.
    /// </summary>
    public void ResetApproveAll()
    {
        ApproveAll = false;
        AutoMode = false;
    }
}
