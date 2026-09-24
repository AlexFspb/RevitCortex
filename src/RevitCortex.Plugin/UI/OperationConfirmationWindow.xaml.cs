using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using RevitCortex.Core.Session;

namespace RevitCortex.Plugin.UI;

/// <summary>Per-operation timed approval for normal tools, never for custom C#.</summary>
public partial class OperationConfirmationWindow : Window
{
    // Process-local preference; intentionally separate from CriticalConfirmationWindow.
    public static bool AutoRunEnabled { get; private set; } = true;
    private readonly OperationApprovalCountdown _approval;
    private readonly DispatcherTimer _timer;
    private bool _initialized;
    private bool _closed;

    public OperationConfirmationWindow(string action, int elementCount, string? description)
    {
        _approval = new OperationApprovalCountdown(AutoRunEnabled);
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        InitializeComponent();
        InstructionText.Text = $"Операция: {action}. Элементов: {elementCount}.";
        DescriptionText.Text = string.IsNullOrWhiteSpace(description)
            ? "Эта операция может изменить активную модель Revit."
            : description;
        AutoRunHint.Text = $"Операция выполнится через {OperationApprovalCountdown.DelaySeconds} секунды. " +
            "Снимите галочку, чтобы ждать ручного разрешения. Выбор сохраняется до закрытия Revit.";
        AutoRunCheckBox.IsChecked = AutoRunEnabled;
        _initialized = true;
        UpdateButton();
        var owner = Process.GetCurrentProcess().MainWindowHandle;
        if (owner != IntPtr.Zero) new WindowInteropHelper(this).Owner = owner;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _approval.Start();
        if (_approval.AutoRunEnabled) _timer.Start();
    }

    private void AutoRunChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _closed) return;
        _timer.Stop();
        AutoRunEnabled = AutoRunCheckBox.IsChecked == true;
        _approval.SetAutoRun(AutoRunEnabled);
        UpdateButton();
        if (IsLoaded && AutoRunEnabled) _timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_closed) return;
        _approval.Tick();
        UpdateButton();
        if (_approval.Decision == true) FinishApproval();
    }

    private void UpdateButton() => AllowOnceText.Text = _approval.AutoRunEnabled
        ? $"Разрешить однократно — автоматически через {_approval.SecondsRemaining} с"
        : "Разрешить однократно";

    private void AllowOnce_Click(object sender, RoutedEventArgs e)
    {
        if (_closed) return;
        _approval.ApproveOnce();
        FinishApproval();
    }

    private void FinishApproval()
    {
        _timer.Stop();
        // Setting DialogResult closes a modal window. Do not close it twice.
        DialogResult = _approval.Decision == true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _closed = true;
        _timer.Stop();
        _approval.Cancel(); // No effect on an already-approved terminal decision.
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Tick -= Timer_Tick;
        base.OnClosed(e);
    }
}
