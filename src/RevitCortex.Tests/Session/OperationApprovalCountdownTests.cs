using RevitCortex.Core.Session;
using Xunit;

namespace RevitCortex.Tests.Session;

public class OperationApprovalCountdownTests
{
    [Fact]
    public void DefaultIsEnabled_ButCannotApproveBeforeWindowLoads()
    {
        var state = new OperationApprovalCountdown();
        Assert.True(state.AutoRunEnabled);
        for (var i = 0; i < 10; i++) state.Tick();
        Assert.Null(state.Decision);
        Assert.Equal(3, state.SecondsRemaining);
    }

    [Fact]
    public void AutoApproval_RequiresThreeTicksAfterStart()
    {
        var state = new OperationApprovalCountdown();
        state.Start();
        state.Tick();
        Assert.Equal(2, state.SecondsRemaining);
        Assert.Null(state.Decision);
        state.Tick();
        Assert.Equal(1, state.SecondsRemaining);
        Assert.Null(state.Decision);
        state.Tick();
        Assert.True(state.Decision);
        state.Tick();
        Assert.Equal(0, state.SecondsRemaining);
    }

    [Fact]
    public void UncheckBeforeExpiry_CancelsCountdownUntilReenabled()
    {
        var state = new OperationApprovalCountdown();
        state.Start();
        state.Tick();
        state.Tick();
        state.SetAutoRun(false);
        for (var i = 0; i < 10; i++) state.Tick();
        Assert.Null(state.Decision);
        state.SetAutoRun(true);
        state.Tick();
        state.Tick();
        Assert.Null(state.Decision);
        state.Tick();
        Assert.True(state.Decision);
    }

    [Fact]
    public void ManualPreference_WaitsForAllowOnce()
    {
        var state = new OperationApprovalCountdown(autoRunEnabled: false);
        state.Start();
        for (var i = 0; i < 10; i++) state.Tick();
        Assert.Null(state.Decision);
        state.ApproveOnce();
        Assert.True(state.Decision);
    }

    [Fact]
    public void CancelIsFinal_EvenIfTimerOrClickArrivesAfterClose()
    {
        var state = new OperationApprovalCountdown();
        state.Start();
        state.Tick();
        state.Tick();
        state.Cancel();
        state.Tick();
        state.ApproveOnce();
        state.SetAutoRun(true);
        state.Tick();
        Assert.False(state.Decision);
    }

    [Fact]
    public void ApprovedDecisionSurvivesWindowCleanup()
    {
        var state = new OperationApprovalCountdown();
        state.ApproveOnce();
        state.Cancel();
        Assert.True(state.Decision);
    }

    [Fact]
    public void ApprovalDoesNotCarryIntoNextOperation()
    {
        var first = new OperationApprovalCountdown();
        first.ApproveOnce();
        var second = new OperationApprovalCountdown();
        Assert.Null(second.Decision);
        Assert.Equal(3, second.SecondsRemaining);
    }
}
