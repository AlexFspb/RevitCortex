using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RevitCortex.Plugin.UI;

/// <summary>A one-time, non-modal startup warning with no acknowledgement button.</summary>
public sealed class PortWarningWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(12) };
    public PortWarningWindow(string message, IntPtr owner)
    {
        Title = "RevitCortex — Port configuration";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18) };
        if (owner != IntPtr.Zero) new WindowInteropHelper(this).Owner = owner;
        _timer.Tick += OnTick;
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => { _timer.Stop(); _timer.Tick -= OnTick; };
    }
    private void OnTick(object? sender, EventArgs e) => Close();
}
