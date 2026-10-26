using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using Xunit;

namespace RevitCortex.Tests.Session;

public class ConfirmationDialogLifecycleTests
{
    [Fact]
    public void ConstructorAndPrematureLoadedCannotEnableApproval()
    {
        var state = new ConfirmationDialogLifecycle();
        Assert.False(state.MarkLoaded());
        state.Complete(true, _ => throw new Exception("Must not set DialogResult"), () => { });
        Assert.False(state.IsActive);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActiveDialogCompletesExactlyOnce(bool approved)
    {
        var state = new ConfirmationDialogLifecycle();
        var calls = 0;
        var cleaned = false;
        bool? decision = null;
        var result = state.Show(() =>
        {
            Assert.True(state.MarkLoaded());
            state.Complete(approved, value => { calls++; decision = value; state.End(); }, () => { });
            state.Complete(!approved, _ => calls++, () => { });
            return decision;
        }, () => cleaned = true);
        Assert.Equal(approved, result);
        Assert.Equal(1, calls);
        Assert.True(cleaned);
        Assert.False(state.IsActive);
    }

    [Fact]
    public void ShowFailureCleansUpAndLateTickCannotSetDialogResult()
    {
        var state = new ConfirmationDialogLifecycle();
        var cleaned = false;
        var cause = new InvalidOperationException("Dispatcher processing has been suspended.");
        var error = Assert.Throws<ConfirmationFailedException>(() => state.Show(() => throw cause, () => cleaned = true));
        Assert.Same(cause, error.InnerException);
        Assert.True(cleaned);
        state.Complete(true, _ => throw new Exception("Late callback"), () => { });
        Assert.Equal(CortexErrorCode.ConfirmationFailed, error.ToResult().Error!.Code);
        Assert.DoesNotContain("cancelled by user", error.ToResult().Error!.Message);
        error.DiagnosticReportPath = "diagnostic.txt";
        Assert.Equal("diagnostic.txt", error.ToResult().Error!.Context!["diagnosticReportPath"]);
        Assert.Equal(false, error.ToResult().Error!.Context!["operationApproved"]);
    }

    [Fact]
    public void InvalidDialogResultNeverEscapesTimerCallbackOrApprovesOperation()
    {
        var state = new ConfirmationDialogLifecycle();
        var closed = false;
        var cleaned = false;
        Assert.Throws<ConfirmationFailedException>(() => state.Show(() =>
        {
            state.MarkLoaded();
            state.Complete(true, _ => throw new InvalidOperationException("DialogResult can be set only after Window is created and shown as dialog."), () => closed = true);
            Assert.True(closed); // Complete returned normally to DispatcherTimer.
            return true; // Even a bogus successful Show result cannot mask the failure.
        }, () => cleaned = true));
        Assert.True(cleaned);
    }

    [Fact]
    public void ClosingBeforeTickPreventsApproval()
    {
        var state = new ConfirmationDialogLifecycle();
        Assert.False(state.Show(() =>
        {
            state.MarkLoaded();
            state.End();
            state.Complete(true, _ => throw new Exception("Cannot approve closed dialog"), () => { });
            return null;
        }, () => { }));
    }

    [Fact]
    public void FailureDuringFallbackCloseIsCapturedAndDialogCannotBeReused()
    {
        var state = new ConfirmationDialogLifecycle();
        var error = Assert.Throws<ConfirmationFailedException>(() => state.Show(() =>
        {
            state.MarkLoaded();
            state.Complete(true, _ => throw new InvalidOperationException("setter"), () => throw new InvalidOperationException("close"));
            return false;
        }, () => { }));
        Assert.IsType<AggregateException>(error.InnerException);
        Assert.Throws<ConfirmationFailedException>(() => state.Show(() => true, () => { }));
    }
}
