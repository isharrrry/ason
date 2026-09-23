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

    /// <summary>
    /// Whether the runtime relays the logs of an execution. It describes what the runtime supports end to end,
    /// not what a particular transport chose to publish: gRPC always has the <c>StreamExecution</c> rpc, the MCP
    /// tool surface offers <c>ason_stream_script</c>, HTTP offers <c>POST {base}/script/stream</c>, and turning
    /// this off removes all of them. A client therefore asks the transport it is using what it can get, instead
    /// of reading one flag and assuming every adapter behaves the same.
    /// </summary>
    public bool LogStream { get; init; } = true;
}
