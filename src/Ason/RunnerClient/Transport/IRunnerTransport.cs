using Microsoft.Extensions.Logging;
using AsonRunner;

namespace Ason.Transport;

/// <summary>
/// Carries the ASON runner protocol ("exec" down, "invoke"/"log" up) to wherever scripts are evaluated.
///
/// The runtime ships two implementations - SignalR to a remote runner host and stdio to an executor process -
/// but a host can supply its own through <see cref="RunnerClient.UseTransport"/>: a bridge can then reach an
/// application over gRPC or MCP while keeping the rest of the pipeline (proxy layer, operator invocation,
/// result handling) unchanged. The protocol records themselves are public in Ason.Runner.Core.
/// </summary>
public interface IRunnerTransport {
    bool IsStarted { get; }
    event Action<string> LineReceived; // raw JSON line from runner
    event Action<string> Closed; // reason
    Task StartAsync();
    Task SendAsync(string jsonLine);
    Task StopAsync();
}
