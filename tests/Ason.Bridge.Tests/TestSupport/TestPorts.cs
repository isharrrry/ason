using System.Net;
using System.Net.Sockets;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// The samples bind two ports next to each other (gRPC and MCP, or gRPC and HTTP), so a test has to reserve a
/// pair - not one port and hope the neighbour is free. This is the one place that logic lives.
/// </summary>
internal static class TestPorts {

    /// <summary>
    /// Below the ephemeral ranges of both Windows (49152 and up) and Linux (32768 and up by default), and away
    /// from the ports the samples use by default (5222/5223), so a candidate is unlikely to be taken by a
    /// client connection or by a sample started outside the tests.
    /// </summary>
    const int FirstCandidate = 20000;
    const int LastCandidate = 30000;

    /// <summary>
    /// Finds two consecutive free ports by holding both at once before handing them over.
    ///
    /// The candidates are chosen here and both are bound simultaneously. Asking the OS instead - bind two
    /// sockets to port 0 and keep the numbers only if they happen to be neighbours - does not work: Windows
    /// hands out the next free number, so two allocations tend to be adjacent, while Linux spreads them
    /// pseudo-randomly over the ephemeral range, so two consecutive allocations are almost never neighbours.
    /// That is what "Could not find two consecutive free ports" on the first Linux CI run meant for every
    /// process-level test, while the same suite was green on Windows.
    /// </summary>
    public static (int First, int Second) Pair() {
        for (var attempt = 0; attempt < 200; attempt++) {
            var first = Random.Shared.Next(FirstCandidate, LastCandidate);
            TcpListener? low = null;
            TcpListener? high = null;
            try {
                low = new TcpListener(IPAddress.Loopback, first);
                low.Start();
                high = new TcpListener(IPAddress.Loopback, first + 1);
                high.Start();
                return (first, first + 1);
            }
            catch (SocketException) {
                // One of the two is taken (or a parallel test picked the same base). Try another base; anything
                // else - no permission to bind, no loopback interface - is a real failure and is left to surface.
            }
            finally {
                high?.Stop();
                low?.Stop();
            }
        }
        throw new InvalidOperationException("Could not find two consecutive free ports for a sample process.");
    }

    /// <summary>True when a failure is a lost port race rather than a real startup failure.</summary>
    public static bool IsPortRace(Exception exception) =>
        exception.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase);
}
