using System.Net;
using System.Net.Sockets;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// The samples bind two ports next to each other (gRPC and MCP, or gRPC and HTTP), so a test has to reserve a
/// pair - not one port and hope the neighbour is free. This is the one place that logic lives.
/// </summary>
internal static class TestPorts {

    /// <summary>Finds two consecutive free ports by holding both at once before handing them over.</summary>
    public static (int First, int Second) Pair() {
        for (var attempt = 0; attempt < 100; attempt++) {
            var first = new TcpListener(IPAddress.Loopback, 0);
            first.Start();
            var second = new TcpListener(IPAddress.Loopback, 0);
            second.Start();
            var firstPort = ((IPEndPoint)first.LocalEndpoint).Port;
            var secondPort = ((IPEndPoint)second.LocalEndpoint).Port;
            first.Stop();
            second.Stop();
            if (secondPort == firstPort + 1) return (firstPort, secondPort);
        }
        throw new InvalidOperationException("Could not find two consecutive free ports for a sample process.");
    }

    /// <summary>True when a failure is a lost port race rather than a real startup failure.</summary>
    public static bool IsPortRace(Exception exception) =>
        exception.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase);
}
