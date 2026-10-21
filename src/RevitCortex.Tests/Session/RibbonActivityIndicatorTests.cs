using System.Reflection;
using RevitCortex.Plugin.UI;
using Xunit;

namespace RevitCortex.Tests.Session;

public class RibbonActivityIndicatorTests
{
    [Fact]
    public async Task ActivityExcludesProbesEndsExplicitlyAndContainsDisplayFailures()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                // No windows, screenshots, message pump, or live Revit calls.
                var type = typeof(ConfirmationHelper).Assembly.GetType("RevitCortex.Plugin.UI.RibbonActivityIndicator")!;
                var count = 0;
                using var indicator = (IDisposable)Activator.CreateInstance(type, (Action)(() => { count++; throw new InvalidOperationException("display failed"); }))!;
                void Begin(string name) => type.GetMethod("Begin")!.Invoke(indicator, new object[] { name });
                object? State(string name) => type.GetProperty(name)!.GetValue(indicator);
                Begin("get_connection_status"); Assert.Null(State("ToolName")); Assert.Equal(0, count);
                Begin("say_hello"); Assert.Null(State("ToolName"));
                Begin("send_code_to_revit"); Assert.Equal("send_code_to_revit", State("ToolName")); Assert.Equal(false, State("Dimmed"));
                type.GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(indicator, new object?[] { null, EventArgs.Empty });
                Assert.Equal(true, State("Dimmed"));
                type.GetMethod("End")!.Invoke(indicator, null);
                Assert.Null(State("ToolName")); Assert.Equal(false, State("Dimmed"));
                Begin("set_element_parameters"); Assert.Equal("set_element_parameters", State("ToolName"));
                type.GetMethod("End")!.Invoke(indicator, null);
                done.SetResult();
            }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
