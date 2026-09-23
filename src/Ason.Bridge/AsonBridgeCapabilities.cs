namespace Ason.Bridge;

/// <summary>
/// Which interfaces the bridge offers to a transport. They are independent: a host may publish only the API
/// list, only scripts, only single functions, or any combination - a transport maps exactly the enabled ones,
/// so the two execution interfaces never conflict with each other.
/// </summary>
public sealed record AsonBridgeCapabilities {
    /// <summary>Reading the manifest (operator API, models, live instances).</summary>
    public bool ListApis { get; init; } = true;

    /// <summary>The whole-script interface: send generated code, get its result.</summary>
    public bool ExecuteScript { get; init; } = true;

    /// <summary>The single-function interface: call one operator method precisely, with JSON arguments.</summary>
    public bool InvokeFunction { get; init; } = true;

    /// <summary>Pass-through to the MCP tools the application itself consumes.</summary>
    public bool InvokeMcpTool { get; init; }

    /// <summary>Stream execution logs to the caller.</summary>
    public bool LogStream { get; init; } = true;
}
