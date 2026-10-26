using System.Reflection;
using RevitCortex.Plugin.UI;
using Xunit;
namespace RevitCortex.Tests.Session;
public class RibbonActivityIndicatorTests
{
    [Theory]
    [InlineData("get_connection_status")]
    [InlineData("say_hello")]
    [InlineData("unknown_tool")]
    public async Task TrafficPulseSurvivesShortRequestsAndDoesNotMaskExecution(string probe)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                // Private STA, no windows, live Revit API or nested message pump.
                var type = typeof(ConfirmationHelper).Assembly.GetType("RevitCortex.Plugin.UI.RibbonActivityIndicator")!;
                long now = 0; int count = 0;
                using var indicator = (IDisposable)Activator.CreateInstance(type,
                    (Action)(() => { count++; throw new InvalidOperationException("display failed"); }),
                    (Func<long>)(() => now))!;
                void Call(string name, params object?[] args) => type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(indicator, args);
                object? State(string name) => type.GetProperty(name)!.GetValue(indicator);
                var worker = new Thread(() => { for (int i = 0; i < 10000; i++) Call("NotifyRequest", probe); });
                worker.Start(); Assert.True(worker.Join(5000));
                Assert.Null(State("ToolName")); Assert.Equal(0, count);
                now = 10000; Call("ApplyPendingRequest"); // hold begins after UI becomes available
                Assert.Equal(probe, State("ToolName")); Assert.Equal(1, count);
                Assert.Equal(false, State("IsExecuting"));
                now += 300; Call("Tick", null, EventArgs.Empty); Assert.Equal(true, State("Dimmed"));
                now += 899; Call("Tick", null, EventArgs.Empty); Assert.Equal(probe, State("ToolName"));
                now++; Call("Tick", null, EventArgs.Empty); Assert.Null(State("ToolName"));
                Call("Begin", "send_code_to_revit");
                Call("NotifyRequest", probe); Call("ApplyPendingRequest");
                now += 100000; Call("Tick", null, EventArgs.Empty);
                Assert.Equal("send_code_to_revit", State("ToolName")); Assert.Equal(true, State("IsExecuting"));
                Call("End"); Assert.Equal(false, State("IsExecuting"));
                Assert.Equal("send_code_to_revit", State("ToolName"));
                now += 1200; Call("Tick", null, EventArgs.Empty); Assert.Null(State("ToolName"));
                Call("NotifyRequest", probe); indicator.Dispose(); int before = count;
                Call("ApplyPendingRequest"); Call("NotifyRequest", probe); Call("Begin", probe); Call("Tick", null, EventArgs.Empty);
                Assert.Null(State("ToolName")); Assert.Equal(before, count);
                done.SetResult();
            }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
