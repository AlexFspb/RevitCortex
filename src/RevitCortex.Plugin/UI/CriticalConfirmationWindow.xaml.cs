using System;
using System.Diagnostics;
using System.ComponentModel;
using RevitCortex.Core.Session;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Critical confirmation dialog used for send_code_to_revit and other critical actions.
/// When "Allow auto-run" is enabled, the Yes button counts down from 3 seconds and
/// automatically approves the operation at zero. The preference is kept for the current
/// Revit process only and resets when Revit is restarted.
/// </summary>
public partial class CriticalConfirmationWindow : Window
{
    private const int AutoApproveSeconds = 3;
    private readonly DispatcherTimer _timer;
    private readonly ConfirmationDialogLifecycle _lifecycle = new();
    private bool _rendered;
    private int _secondsRemaining = AutoApproveSeconds;

    /// <summary>
    /// Session-scoped preference. It intentionally is not written to settings.json.
    /// Revit restart returns the dialog to manual approval mode.
    /// </summary>
    public static bool AutoApproveEnabled { get; private set; }

    public CriticalConfirmationWindow(string action, int elementCount, string? description)
    {
        InitializeComponent();

        InstructionText.Text = $"About to {action} ({elementCount} element(s))";
        DescriptionText.Text = string.IsNullOrWhiteSpace(description)
            ? "This is a critical operation and can modify the active Revit model."
            : description;

        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += Timer_Tick;

        AutoApproveCheckBox.IsChecked = AutoApproveEnabled;
        UpdateYesButtonText();


    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _lifecycle.MarkLoaded();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_rendered) return;
        _rendered = true;
        if (_lifecycle.IsActive && AutoApproveCheckBox.IsChecked == true) StartCountdown();
    }

    private void AutoApprove_Checked(object sender, RoutedEventArgs e)
    {
        AutoApproveEnabled = true;
        StartCountdown();
    }

    private void AutoApprove_Unchecked(object sender, RoutedEventArgs e)
    {
        AutoApproveEnabled = false;
        StopCountdown(reset: true);
    }

    private void StartCountdown()
    {
        _secondsRemaining = AutoApproveSeconds;
        UpdateYesButtonText();
        if (_lifecycle.IsActive && _rendered && IsVisible && !_timer.IsEnabled)
            _timer.Start();
    }

    private void StopCountdown(bool reset)
    {
        _timer.Stop();
        if (reset)
            _secondsRemaining = AutoApproveSeconds;
        UpdateYesButtonText();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_lifecycle.IsActive || !_rendered || !IsVisible) { _timer.Stop(); return; }
        if (AutoApproveCheckBox.IsChecked != true)
        {
            StopCountdown(reset: true);
            return;
        }

        _secondsRemaining--;
        if (_secondsRemaining <= 0)
        {
            _timer.Stop();
            Finish(true);
            return;
        }

        UpdateYesButtonText();
    }

    private void UpdateYesButtonText()
    {
        YesTitleText.Text = AutoApproveCheckBox.IsChecked == true
            ? $"Yes — auto approve in {_secondsRemaining} s"
            : "Yes";
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Finish(true);
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Finish(false);
    }

    public bool? ShowConfirmation() => _lifecycle.Show(ShowDialog, CleanupConfirmation);

    private void Finish(bool accepted)
    {
        _timer.Stop();
        _lifecycle.Complete(accepted, value => DialogResult = value, Close);
    }

    public void CleanupConfirmation()
    {
        _lifecycle.End();
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        CleanupConfirmation();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        CleanupConfirmation();
        base.OnClosed(e);
    }
}
