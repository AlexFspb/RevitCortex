using System;
using System.Windows.Threading;

namespace RevitCortex.Plugin.UI;

/// <summary>UI-thread-only indicator. No nested message pump, window, activation or Revit API from a worker.</summary>
internal sealed class RibbonActivityIndicator : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Action _refresh;
    public string? ToolName { get; private set; }
    public bool Dimmed { get; private set; }

    public RibbonActivityIndicator(Action refresh)
    {
        _refresh = refresh;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
        _timer.Tick += Tick;
    }

    public void Begin(string toolName)
    {
        if (toolName is "get_connection_status" or "say_hello") return;
        ToolName = toolName;
        Dimmed = false;
        RefreshSafely();
        _timer.Start();
    }

    public void End()
    {
        _timer.Stop();
        ToolName = null;
        Dimmed = false;
        RefreshSafely();
    }

    private void Tick(object? sender, EventArgs e)
    {
        Dimmed = !Dimmed;
        RefreshSafely();
    }

    private void RefreshSafely()
    {
        try { _refresh(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[RevitCortex] Ribbon indicator failed: {ex}"); }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Tick;
    }
}
