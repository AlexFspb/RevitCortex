using System;
using System.Windows.Threading;

namespace RevitCortex.Plugin.UI;

/// <summary>Coalesced socket notifications; all display changes stay on the UI dispatcher.</summary>
internal sealed class RibbonActivityIndicator : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher;
    private readonly Action _refresh;
    private readonly Func<long> _now;
    private readonly object _pendingLock = new();
    private string? _pendingRequest;
    private bool _refreshQueued;
    private volatile bool _disposed;
    private string? _executingTool;
    private string? _recentTool;
    private long _recentUntil;
    public string? ToolName => _executingTool ?? _recentTool;
    public bool IsExecuting => _executingTool != null;
    public bool Dimmed { get; private set; }

    public RibbonActivityIndicator(Action refresh, Func<long>? now = null)
    {
        _refresh = refresh;
        _now = now ?? (() => Environment.TickCount64);
        _dispatcher = Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += Tick;
    }

    // Called by socket threads. At most one queued callback, even if Revit is busy.
    public void NotifyRequest(string toolName)
    {
        lock (_pendingLock)
        {
            if (_disposed) return;
            _pendingRequest = toolName;
            if (_refreshQueued) return;
            _refreshQueued = true;
        }
        try { _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ApplyPendingRequest)); }
        catch (Exception ex)
        {
            lock (_pendingLock) { _refreshQueued = false; }
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Queue request indicator: {ex}");
        }
    }

    private void ApplyPendingRequest()
    {
        string? tool;
        lock (_pendingLock)
        {
            _refreshQueued = false;
            tool = _pendingRequest;
            _pendingRequest = null;
        }
        if (!_disposed && tool != null) Touch(tool);
    }

    private void Touch(string toolName)
    {
        _recentTool = toolName;
        // Start the visible hold when the UI can process it, not at socket receipt.
        _recentUntil = _now() + 1200;
        Dimmed = false;
        RefreshSafely();
        _timer.Start();
    }

    public void Begin(string toolName)
    {
        if (_disposed) return;
        _executingTool = toolName;
        Touch(toolName);
    }

    public void End()
    {
        if (_disposed) return;
        var tool = _executingTool;
        _executingTool = null;
        // A synchronous script may have prevented repainting during execution.
        // Retain a recent-activity pulse; do not mislabel it as still executing.
        if (tool != null) Touch(tool);
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (_now() >= _recentUntil) _recentTool = null;
        if (ToolName == null) { _timer.Stop(); Dimmed = false; }
        else Dimmed = !Dimmed;
        RefreshSafely();
    }

    private void RefreshSafely()
    {
        try { _refresh(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[RevitCortex] Ribbon indicator failed: {ex}"); }
    }

    public void Dispose()
    {
        lock (_pendingLock) { _disposed = true; _pendingRequest = null; }
        _timer.Stop();
        _timer.Tick -= Tick;
        _executingTool = _recentTool = null;
        Dimmed = false;
    }
}
