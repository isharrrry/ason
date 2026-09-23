namespace Ason.Bridge;

/// <summary>
/// Everything a client needs in order to use an application's operator API, in one payload: what can be
/// called, where scripts run, which interfaces are enabled, the script prompt layer, and which operator
/// instances are alive right now.
///
/// It is deliberately transport-neutral and JSON-round-trippable, because it is the one contract shared by
/// every adapter (gRPC, MCP, OpenAPI) and by the agent that consumes them. The API listing is derived from
/// <c>OperatorApiCatalog</c>, which is the same reflection walk the script prompt uses, so a listing cannot
/// drift from what scripts are actually able to call.
/// </summary>
public sealed record AsonBridgeManifest(
    /// <summary><see cref="AsonBridgeProtocol.Version"/> this payload was produced with.</summary>
    string ProtocolVersion,
    /// <summary>Name of the application, for display and for log correlation.</summary>
    string AppName,
    /// <summary>Where scripts run; matches <see cref="IAsonExecutor.Name"/>.</summary>
    string Execution,
    /// <summary>Which interfaces are actually enabled, so a client can adapt instead of probing.</summary>
    AsonBridgeCapabilities Capabilities,
    /// <summary>The machine-readable operator API.</summary>
    AsonBridgeApi Api,
    /// <summary>The same API as a Markdown document, ready to show a user or hand to a model.</summary>
    string Markdown,
    /// <summary>Generated proxy layer a script needs, including declarations for the live instances.</summary>
    string Proxies,
    /// <summary>Signature-only rendering of the same API, for prompt contexts that prefer it.</summary>
    string Signatures,
    /// <summary>Live operator instances, addressable by handle over the single-function interface.</summary>
    IReadOnlyList<AsonBridgeInstance> Instances,
    /// <summary>
    /// Changes whenever the instance set changes, so a caller that kept an older manifest can tell whether the
    /// instance declarations inside <see cref="Proxies"/> are still current. It is a digest, not a timestamp:
    /// two manifests taken while nothing changed compare equal.
    /// </summary>
    string InstancesRevision);
