using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using RevitCortex.Core.Session;

namespace RevitCortex.Plugin.UI;

/// <summary>Manual, per-request confirmation. Automation is controlled only by the host policy.</summary>
public partial class CriticalConfirmationWindow : Window
{
    private readonly ConfirmationDialogLifecycle _lifecycle = new();
    public CriticalConfirmationWindow(string action, int elementCount, string? description)
    {
        InitializeComponent();
        InstructionText.Text = $"Операция: {action}. Элементов: {elementCount}.";
        DescriptionText.Text = string.IsNullOrWhiteSpace(description)
            ? "Эта операция может изменить активную модель Revit." : description;
    }
    private void Window_Loaded(object sender, RoutedEventArgs e) => _lifecycle.MarkLoaded();
    private void Yes_Click(object sender, RoutedEventArgs e) => Finish(true);
    private void No_Click(object sender, RoutedEventArgs e) => Finish(false);
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
    public bool? ShowConfirmation() => _lifecycle.Show(ShowDialog, CleanupConfirmation);
    private void Finish(bool accepted) => _lifecycle.Complete(accepted, value => DialogResult = value, Close);
    public void CleanupConfirmation() => _lifecycle.End();
    protected override void OnClosing(CancelEventArgs e) { CleanupConfirmation(); base.OnClosing(e); }
    protected override void OnClosed(EventArgs e) { CleanupConfirmation(); base.OnClosed(e); }
}
