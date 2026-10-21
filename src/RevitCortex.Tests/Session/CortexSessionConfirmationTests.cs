using RevitCortex.Core.Session;
using RevitCortex.Core.Discovery;
using Xunit;

namespace RevitCortex.Tests.Session;

public class CortexSessionConfirmationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingOrNullCallbackDenies(bool critical)
    {
        var session = new CortexSession(new SessionStore());
        Assert.False(session.RequestConfirmation("change", 1, critical: critical));
        session.ConfirmAction = session.CriticalConfirmAction = (_, _, _) => null;
        Assert.False(session.RequestConfirmation("change", 1, critical: critical));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HostPolicyControlsBothKindsWithoutWindowWhenEnabled(bool critical)
    {
        var policy = new AutomaticExecutionPolicy();
        var session = new CortexSession(new SessionStore());
        var shown = 0;
        session.ConfirmAction = session.CriticalConfirmAction = (_, _, _) => policy.Decide(() => { shown++; return false; });
        Assert.True(session.RequestConfirmation("change", 1, critical: critical));
        Assert.Equal(0, shown);
        policy.SetEnabled(false);
        Assert.False(session.RequestConfirmation("change", 1, critical: critical));
        Assert.Equal(1, shown);
        session.Reinitialize(new DocumentCapabilities(), "en");
        Assert.False(policy.Enabled); // A document switch never re-enables automatic execution.
        Assert.True(new AutomaticExecutionPolicy().Enabled); // A fresh process/host starts enabled.
    }
    [Fact]
    public void HostPoliciesAreIndependent()
    {
        var first = new AutomaticExecutionPolicy(); var second = new AutomaticExecutionPolicy();
        first.SetEnabled(false);
        Assert.True(second.Enabled);
    }
    [Fact]
    public void CriticalDoesNotUseOrdinaryCallback()
    {
        var session = new CortexSession(new SessionStore()) { ConfirmAction = (_, _, _) => true };
        Assert.False(session.RequestConfirmation("script", 1, critical: true));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ApprovalStillChecksContextAndExpiration(bool critical)
    {
        var session = new CortexSession(new SessionStore()) { ConfirmAction = (_, _, _) => true, CriticalConfirmAction = (_, _, _) => true };
        var request = new ToolRequestLifetime(); request.TryStart();
        using var scope = request.Enter();
        request.ContextIsValid = () => false;
        Assert.Throws<DocumentContextChangedException>(() => session.RequestConfirmation("change", 1, critical: critical));
        request.ContextIsValid = () => true;
        request.Expire("change", 1);
        Assert.Throws<ConfirmationExpiredException>(() => session.RequestConfirmation("change", 1, critical: critical));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExpirationDuringApprovalDoesNotAuthorizeExecution(bool critical)
    {
        var request = new ToolRequestLifetime(); request.TryStart();
        using var scope = request.Enter();
        var session = new CortexSession(new SessionStore());
        session.ConfirmAction = session.CriticalConfirmAction = (_, _, _) => { request.Expire("change", 1); return true; };
        Assert.Throws<ConfirmationExpiredException>(() => session.RequestConfirmation("change", 1, critical: critical));
    }
}
