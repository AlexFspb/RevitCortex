using System.Net;
using System.Net.Sockets;
using RevitCortex.Core.Session;
using RevitCortex.Plugin;
using RevitCortex.Plugin.Communication;
using Xunit;

namespace RevitCortex.Tests.Communication;

public class SocketServicePortSelectionTests
{
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
