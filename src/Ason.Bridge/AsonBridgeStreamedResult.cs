namespace Ason.Bridge;

/// <summary>
/// One execution's outcome together with the log lines it produced. It exists for transports that cannot
/// stream: MCP tools answer a call once, so "the logs while it runs" has to arrive as part of the answer.
/// A transport that can stream (gRPC, HTTP server-sent events) sends the same information incrementally.
/// </summary>
public sealed record AsonBridgeStreamedResult(AsonBridgeCallResult Result, IReadOnlyList<AsonBridgeLogEventArgs> Logs);
