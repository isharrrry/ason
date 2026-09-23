namespace Ason.Bridge;

/// <summary>
/// The contract version an application and an agent agree on. It travels in the manifest so a client that
/// speaks a different revision fails fast instead of mis-decoding a payload.
///
/// 1.1 is a purely additive revision: the MCP pass-through call (gRPC <c>InvokeMcpTool</c>, the MCP tool
/// <c>ason_invoke_mcp_tool</c>) and the fresh-instance script mode (<c>includeInstanceDeclarations</c>,
/// <c>instancesRevision</c>). A 1.0 client keeps working against a 1.1 bridge - every message it sends is
/// still understood and every field it reads is still there - so the version only needs checking when a
/// client relies on one of the additions.
/// </summary>
public static class AsonBridgeProtocol {
    public const string Version = "1.1";
}

/// <summary>
/// Stable error codes a transport maps onto its own failure shape (a gRPC status, an MCP tool error, an
/// HTTP status). Clients branch on these instead of parsing messages.
/// </summary>
public static class AsonBridgeErrorCodes {
    /// <summary>The capability was switched off on the bridge.</summary>
    public const string NotSupported = "not-supported";
    /// <summary>The request was malformed (missing operator, method or arguments).</summary>
    public const string InvalidArguments = "invalid-arguments";
    /// <summary>The script was rejected by the keyword validator before execution.</summary>
    public const string ScriptRejected = "script-rejected";
    /// <summary>No operator with that name is registered in the manifest.</summary>
    public const string OperatorNotFound = "operator-not-found";
    /// <summary>The operator is an instance operator and no live instance exists.</summary>
    public const string HandleRequired = "handle-required";
    /// <summary>Several live instances match the operator, so a handle must be named.</summary>
    public const string HandleAmbiguous = "handle-ambiguous";
    /// <summary>The handle is no longer live (the owning view was closed).</summary>
    public const string HandleNotFound = "handle-not-found";
    /// <summary>The operator exists but has no such method for the supplied argument count.</summary>
    public const string MethodNotFound = "method-not-found";
    /// <summary>Execution ran and failed.</summary>
    public const string ExecutionFailed = "execution-failed";
}
