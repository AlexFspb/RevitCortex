using System.Windows;
using System.Windows.Controls;
using RevitCortex.Plugin.UI;
using Xunit;

namespace RevitCortex.Tests.Session;

public class ConfirmationWindowConstructionTests
{
    [Fact]
    public async Task OrdinaryWindow_HasOneAction_DefaultAuto_AndIndependentCriticalPreference()
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            OperationConfirmationWindow? ordinary = null;
            CriticalConfirmationWindow? critical = null;
            Exception? failure = null;
            try
            {
                // Construct only. Never Show/ShowDialog: no screen or Revit interaction.
                ordinary = new OperationConfirmationWindow("delete", 2, "Test description");
                critical = new CriticalConfirmationWindow("execute C#", 1, "Test description");
                Assert.False(ordinary.IsVisible);
                Assert.False(critical.IsVisible);
                var normalAuto = (CheckBox)ordinary.FindName("AutoRunCheckBox");
                var criticalAuto = (CheckBox)critical.FindName("AutoApproveCheckBox");
                Assert.True(normalAuto.IsChecked);
                Assert.False(criticalAuto.IsChecked);
                Assert.Single(Descendants(ordinary).OfType<Button>());
                Assert.Equal(2, Descendants(critical).OfType<Button>().Count());

                normalAuto.IsChecked = false;
                Assert.False(OperationConfirmationWindow.AutoRunEnabled);
                Assert.False(CriticalConfirmationWindow.AutoApproveEnabled);
                Assert.Equal("Разрешить однократно", ((TextBlock)ordinary.FindName("AllowOnceText")).Text);
                normalAuto.IsChecked = true;
                Assert.True(OperationConfirmationWindow.AutoRunEnabled);
                Assert.False(CriticalConfirmationWindow.AutoApproveEnabled);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                try
                {
                    if (ordinary != null)
                    {
                        ((CheckBox)ordinary.FindName("AutoRunCheckBox")).IsChecked = true;
                        ordinary.Close();
                    }
                    critical?.Close();
                }
                catch (Exception ex) { failure ??= ex; }
            }
            if (failure != null) completed.SetException(failure);
            else completed.SetResult(true);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
