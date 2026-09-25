using System.Net;
using System.Net.Sockets;
using RevitCortex.Core.Session;
using RevitCortex.Plugin;
using RevitCortex.Plugin.Communication;
using Xunit;

namespace RevitCortex.Tests.Communication;

public class SocketServicePortSelectionTests
{
    private sealed class FailingService(bool threadFailure) : SocketService(
        new CortexRouter(new CortexSession(new SessionStore()), new Router.FakeAnalyzer()))
    {
        public bool Fail { get; set; } = true;
        protected override void StartListener(TcpListener listener)
        {
            base.StartListener(listener);
            if (Fail && !threadFailure) throw new IOException("Injected failure after binding");
        }
        protected override void StartListenerThread(Thread thread)
        {
            if (Fail && threadFailure) throw new InvalidOperationException("Injected thread failure");
            base.StartListenerThread(thread);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnexpectedStartupFailure_ReleasesPortAndAllowsRetry(bool threadFailure)
    {
        var port = ReserveThenReleaseTwoPorts()[0];
        var service = new FailingService(threadFailure);
        try
        {
            Assert.ThrowsAny<Exception>(() => service.StartOnFirstAvailablePort(port));
            Assert.False(service.IsRunning);
            Assert.False(service.HasBoundPort);
            using (var probe = new TcpListener(IPAddress.Loopback, port))
            {
                probe.ExclusiveAddressUse = true;
                probe.Start(); // Proves failed startup released the actual socket.
            }
            service.Fail = false;
            Assert.Equal(port, service.StartOnFirstAvailablePort(port));
            Assert.True(service.IsRunning);
            service.Stop();
            service.Fail = true;
            Assert.ThrowsAny<Exception>(() => service.StartOnFirstAvailablePort(1));
            Assert.True(service.HasBoundPort); // Failed restart preserves its assignment.
            Assert.False(service.IsRunning);
            service.Fail = false;
            Assert.Equal(port, service.StartOnFirstAvailablePort(1));
        }
        finally { service.Stop(); }
    }

    private static SocketService CreateService() => new(
        new CortexRouter(new CortexSession(new SessionStore()), new Router.FakeAnalyzer()));

    private static int[] ReserveThenReleaseTwoPorts()
    {
        using var a = new TcpListener(IPAddress.Loopback, 0);
        using var b = new TcpListener(IPAddress.Loopback, 0);
        a.Start();
        b.Start();
        return new[] { ((IPEndPoint)a.LocalEndpoint).Port, ((IPEndPoint)b.LocalEndpoint).Port };
    }

    [Fact]
    public void FirstUsesPrimary_SecondUsesFallback_ThirdFails_AndRestartKeepsAssignment()
    {
        var ports = ReserveThenReleaseTwoPorts();
        var first = CreateService();
        var second = CreateService();
        var third = CreateService();
        try
        {
            Assert.Equal(ports[0], first.StartOnFirstAvailablePort(ports));
            Assert.Equal(ports[1], second.StartOnFirstAvailablePort(ports));
            Assert.Throws<InvalidOperationException>(() => third.StartOnFirstAvailablePort(ports));
            Assert.False(third.IsRunning);
            Assert.False(third.HasBoundPort);
            first.Stop();
            second.Stop();
            // Primary is now free, but B must not move there on a reconnect.
            Assert.Equal(ports[1], second.StartOnFirstAvailablePort(ports));
            Assert.Equal(ports[0], first.StartOnFirstAvailablePort(ports));
        }
        finally
        {
            first.Stop();
            second.Stop();
            third.Stop();
        }
    }

    [Fact]
    public async Task SimultaneousStarts_ClaimDifferentPorts()
    {
        var ports = ReserveThenReleaseTwoPorts();
        var first = CreateService();
        var second = CreateService();
        try
        {
            var assigned = await Task.WhenAll(
                Task.Run(() => first.StartOnFirstAvailablePort(ports)),
                Task.Run(() => second.StartOnFirstAvailablePort(ports)));
            Assert.Equal(ports.OrderBy(p => p), assigned.OrderBy(p => p));
        }
        finally
        {
            first.Stop();
            second.Stop();
        }
    }

    [Fact]
    public void AssignedPortTakenDuringStop_DoesNotSilentlyMoveToOtherFreePort()
    {
        var ports = ReserveThenReleaseTwoPorts();
        var service = CreateService();
        using var competingListener = new TcpListener(IPAddress.Loopback, ports[0]);
        competingListener.ExclusiveAddressUse = true;
        try
        {
            Assert.Equal(ports[0], service.StartOnFirstAvailablePort(ports));
            service.Stop();
            competingListener.Start();
            Assert.Throws<InvalidOperationException>(() => service.StartOnFirstAvailablePort(ports));
            Assert.False(service.IsRunning);
            competingListener.Stop();
            Assert.Equal(ports[0], service.StartOnFirstAvailablePort(ports));
        }
        finally { service.Stop(); }
    }
}
