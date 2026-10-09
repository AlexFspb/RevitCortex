using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using Xunit;

namespace RevitCortex.Tests.Session;

public class ToolRequestLifetimeTests
{
    [Fact]
    public void ExpiredQueuedEventCannotStartLater()
    {
        var request = new ToolRequestLifetime();
        var error = request.Expire("test", 10).Error!;
        Assert.False(request.TryStart());
        Assert.Equal("queued", error.Context!["requestPhase"]);
        Assert.Equal(false, error.Context["executionMayStillBeRunning"]);
        Assert.False(request.Completion.IsCompleted); // Slot still belongs to the pending event.
    }

    [Fact]
    public void ExpirationDuringDialogRejectsEvenATrueCallback()
    {
        var request = new ToolRequestLifetime();
        request.TryStart();
        using var scope = request.Enter();
        var session = new CortexSession(new SessionStore());
        session.CriticalConfirmAction = (_, _, _) => { request.Expire("script", 1); return true; };
        Assert.Throws<ConfirmationExpiredException>(() => session.RequestConfirmation("script", 1, critical: true));
        Assert.Equal(false, request.TimeoutResult("script", 1).Error!.Context!["executionMayStillBeRunning"]);
    }

    [Fact]
    public void ApprovalBeforeTimeoutReportsPossiblyRunningCode()
    {
        var request = new ToolRequestLifetime();
        request.TryStart(); request.BeginConfirmation(); request.FinishConfirmation();
        var error = request.Expire("script", 1).Error!;
        Assert.Equal("executing", error.Context!["requestPhase"]);
        Assert.Equal(true, error.Context["executionMayStillBeRunning"]);
    }

    [Fact]
    public async Task ExpirationNotifiesCleanupButDoesNotPretendTheEventFinished()
    {
        var request = new ToolRequestLifetime();
        request.TryStart(); request.BeginConfirmation();
        int calls = 0;
        using var registration = request.Expiration.Register(() => calls++);
        request.Expire("test", 1); request.Expire("test", 1);
        Assert.Equal(1, calls);
        Assert.False(request.Completion.IsCompleted);
        request.Complete(CortexResult<object>.Fail(CortexErrorCode.Cancelled, "expired"));
        Assert.Equal("completed", request.Phase);
        Assert.Equal(CortexErrorCode.Cancelled, (await request.Completion).Error!.Code);
    }

    [Fact]
    public void CompletedRequestRetainsItsOwnResultAcrossNextRequest()
    {
        var first = new ToolRequestLifetime(); var second = new ToolRequestLifetime();
        var result = CortexResult<object>.Ok("first");
        first.Complete(result); second.Complete(CortexResult<object>.Ok("second"));
        Assert.Same(result, first.Expire("first", 1));
        Assert.False(first.IsExpired);
    }

    [Fact]
    public async Task ConcurrentApprovalAndExpiryNeverApproveAfterExpiryWins()
    {
        for (int i = 0; i < 100; i++)
        {
            var request = new ToolRequestLifetime(); request.TryStart(); request.BeginConfirmation();
            bool approved = false;
            var approve = Task.Run(() => { try { request.FinishConfirmation(); approved = true; } catch (ConfirmationExpiredException) { } });
            var timeout = Task.Run(() => request.Expire("script", 1));
            await Task.WhenAll(approve, timeout);
            var phase = (await timeout).Error!.Context!["requestPhase"];
            Assert.Equal(approved ? "executing" : "awaiting_confirmation", phase);
        }
    }

    [Fact]
    public void AmbientRequestRestoresAfterFailure()
    {
        var request = new ToolRequestLifetime();
        using (request.Enter()) Assert.Same(request, ToolRequestLifetime.Current);
        Assert.Null(ToolRequestLifetime.Current);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1080, 450, 350, 735, 365)]
    [InlineData(-1920, -200, 1920, 1080, 450, 350, -1185, 165)]
    [InlineData(1920, 0, 2560, 1440, 900, 700, 2750, 370)]
    public void CenterUsesSelectedMonitorPixels(int x, int y, int w, int h, int ww, int wh, int expectedX, int expectedY)
    {
        Assert.Equal((expectedX, expectedY), ConfirmationPlacement.Center(x, y, w, h, ww, wh));
    }
}
