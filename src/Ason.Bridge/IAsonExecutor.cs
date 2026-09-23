using System.Text.Json;

namespace Ason.Bridge;

/// <summary>
/// Executes bridge requests. The application ships with <see cref="RunnerClientAsonExecutor"/>, which covers
/// the in-process and out-of-process (and Docker / remote runner) locations through the ASON runtime; a host
/// can replace it entirely - for example to forward to an ASON server of its own.
/// </summary>
public interface IAsonExecutor : IAsyncDisposable {

    /// <summary>Short stable name of where execution happens; travels in the manifest.</summary>
    string Name { get; }

    /// <summary>Log lines produced by execution, for transports that stream them.</summary>
    event EventHandler<AsonBridgeLogEventArgs>? Log;

    /// <summary>Prepares the executor (loads generated code, starts a child process as needed).</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Evaluates a complete script - proxies included - and returns its result.</summary>
    Task<AsonBridgeCallResult> ExecuteScriptAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Calls one operator method directly, without compiling a script.</summary>
    Task<AsonBridgeCallResult> InvokeFunctionAsync(AsonBridgeFunctionCall call, CancellationToken cancellationToken = default);

    /// <summary>Calls a tool on an MCP server the application itself consumes.</summary>
    Task<AsonBridgeCallResult> InvokeMcpToolAsync(string server, string tool, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Names of the MCP servers this executor can pass through to; empty - the default, and what a host that
    /// does not use MCP reports - means the application configured none, and pass-through is then answered
    /// with <see cref="AsonBridgeErrorCodes.NotSupported"/> instead of being forwarded.
    /// </summary>
    IReadOnlyCollection<string> McpServers => Array.Empty<string>();
}
