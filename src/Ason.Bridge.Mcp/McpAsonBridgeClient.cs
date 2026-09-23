using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Ason.Bridge.Mcp;

/// <summary>
/// A typed MCP client for an application's bridge. It exposes the same surface as the gRPC client
/// (<see cref="GetManifestAsync"/>, <see cref="ExecuteScriptAsync"/>, <see cref="InvokeFunctionAsync"/>), so a
/// consumer can switch transports without touching its code.
/// </summary>
public sealed class McpAsonBridgeClient : IAsyncDisposable {

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly McpClient _client;

    McpAsonBridgeClient(McpClient client) => _client = client;

    /// <summary>
    /// Connects to an application's MCP endpoint, for example <c>http://localhost:5222/mcp</c>.
    ///
    /// <paramref name="headers"/> are sent with every request, which is how a caller proves who it is when the
    /// application requires authorization (see <c>AddAsonMcpBridge(endpoint, requireAuthorization: true)</c>).
    /// </summary>
    public static async Task<McpAsonBridgeClient> ConnectAsync(string endpoint, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("An endpoint is required.", nameof(endpoint));
        var options = new HttpClientTransportOptions { Endpoint = new Uri(endpoint) };
        if (headers is { Count: > 0 }) {
            options.AdditionalHeaders = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        }
        var transport = new HttpClientTransport(options);
        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new McpAsonBridgeClient(client);
    }

    /// <summary>Uses an MCP client the caller already owns (its lifetime stays with the caller).</summary>
    public static McpAsonBridgeClient FromClient(McpClient client) {
        if (client is null) throw new ArgumentNullException(nameof(client));
        return new McpAsonBridgeClient(client);
    }

    /// <summary>The underlying MCP client, for tools this wrapper does not model.</summary>
    public McpClient Raw => _client;

    /// <summary>The tools the application published; what is listed depends on its capabilities.</summary>
    public async Task<IReadOnlyList<McpClientTool>> ListToolsAsync(CancellationToken cancellationToken = default) {
        var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return tools.ToList();
    }

    public async Task<AsonBridgeManifest> GetManifestAsync(CancellationToken cancellationToken = default) =>
        Deserialize<AsonBridgeManifest>(await CallAsync(AsonBridgeMcpTools.GetManifest, null, cancellationToken).ConfigureAwait(false));

    public async Task<IReadOnlyList<AsonBridgeInstance>> ListInstancesAsync(CancellationToken cancellationToken = default) =>
        Deserialize<List<AsonBridgeInstance>>(await CallAsync(AsonBridgeMcpTools.ListInstances, null, cancellationToken).ConfigureAwait(false))
        ?? new List<AsonBridgeInstance>();

    public async Task<AsonBridgeScriptApi> GetScriptApiAsync(CancellationToken cancellationToken = default) =>
        Deserialize<AsonBridgeScriptApi>(await CallAsync(AsonBridgeMcpTools.GetScriptApi, null, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// The whole-script interface. <paramref name="includeInstanceDeclarations"/> asks the application to
    /// supply its own proxy layer and current instance declarations - the body-only mode a caller uses when its
    /// manifest snapshot may be stale.
    /// </summary>
    public async Task<AsonBridgeCallResult> ExecuteScriptAsync(string code, bool includeProxyPreamble = true, bool includeInstanceDeclarations = false, CancellationToken cancellationToken = default) {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["code"] = code
        };
        // Optional values are omitted rather than sent as null: that is what the tool schema advertises and
        // what an external MCP client does.
        if (!includeProxyPreamble) arguments["includeProxyPreamble"] = false;
        if (includeInstanceDeclarations) arguments["includeInstanceDeclarations"] = true;
        return Deserialize<AsonBridgeCallResult>(await CallAsync(AsonBridgeMcpTools.ExecuteScript, arguments, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The single-function interface.</summary>
    public async Task<AsonBridgeCallResult> InvokeFunctionAsync(AsonBridgeFunctionCall call, CancellationToken cancellationToken = default) {
        if (call is null) throw new ArgumentNullException(nameof(call));
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["operator"] = call.Operator,
            ["method"] = call.Method
        };
        if (!string.IsNullOrEmpty(call.Handle)) arguments["handle"] = call.Handle;
        if (call.EffectiveArguments.Count > 0) {
            arguments["argumentsJson"] = "[" + string.Join(",", call.EffectiveArguments.Select(a => a.GetRawText())) + "]";
        }
        return Deserialize<AsonBridgeCallResult>(await CallAsync(AsonBridgeMcpTools.InvokeFunction, arguments, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Pass-through to a tool on an MCP server the application itself consumes.</summary>
    public async Task<AsonBridgeCallResult> InvokeMcpToolAsync(string server, string tool, IReadOnlyDictionary<string, JsonElement>? arguments = null, CancellationToken cancellationToken = default) {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["server"] = server,
            ["tool"] = tool
        };
        if (arguments is { Count: > 0 }) payload["argumentsJson"] = JsonSerializer.Serialize(arguments, Json);
        try {
            return Deserialize<AsonBridgeCallResult>(await CallAsync(AsonBridgeMcpTools.InvokeMcpTool, payload, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            // An application with the capability off does not publish the tool at all, so the call fails
            // before it reaches anyone. Which of the two it was is answered by the tool list, not by the
            // message - a tool that ran and failed is a different thing entirely.
            if (!await ExposesToolAsync(AsonBridgeMcpTools.InvokeMcpTool, cancellationToken).ConfigureAwait(false)) {
                return AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.NotSupported,
                    "The application on the other side of this MCP bridge does not expose MCP tool pass-through (its invokeMcpTool capability is off).");
            }
            throw;
        }
    }

    /// <summary>
    /// Executes a script and returns its logs with the result. MCP cannot push here, so the application's
    /// streaming tool answers once with both; a bridge that does not publish that tool reports
    /// <see cref="AsonBridgeErrorCodes.NotSupported"/> rather than pretending the script logged nothing.
    /// </summary>
    public async Task<AsonBridgeStreamedResult> StreamScriptAsync(string code, bool includeProxyPreamble = true, bool includeInstanceDeclarations = false, CancellationToken cancellationToken = default) {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code };
        if (!includeProxyPreamble) arguments["includeProxyPreamble"] = false;
        if (includeInstanceDeclarations) arguments["includeInstanceDeclarations"] = true;

        try {
            var payload = Deserialize<StreamedScriptPayload>(await CallAsync(AsonBridgeMcpTools.StreamScript, arguments, cancellationToken).ConfigureAwait(false));
            return new AsonBridgeStreamedResult(
                new AsonBridgeCallResult(payload.Success, payload.Result, payload.Error, payload.ErrorCode),
                payload.Logs ?? new List<AsonBridgeLogEventArgs>());
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            if (!await ExposesToolAsync(AsonBridgeMcpTools.StreamScript, cancellationToken).ConfigureAwait(false)) {
                return new AsonBridgeStreamedResult(
                    AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.NotSupported,
                        "The application on the other side of this MCP bridge does not expose log streaming (its logStream capability is off)."),
                    Array.Empty<AsonBridgeLogEventArgs>());
            }
            throw;
        }
    }

    async Task<bool> ExposesToolAsync(string name, CancellationToken cancellationToken) {
        try {
            var tools = await ListToolsAsync(cancellationToken).ConfigureAwait(false);
            return tools.Any(t => string.Equals(t.Name, name, StringComparison.Ordinal));
        }
        catch (Exception) {
            return false;
        }
    }

    /// <summary>The wire shape of the streaming tool: a call result plus the logs it produced.</summary>
    sealed record StreamedScriptPayload(bool Success, JsonElement? Result, string? Error, string? ErrorCode, List<AsonBridgeLogEventArgs>? Logs);

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    async Task<string> CallAsync(string tool, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken) {
        var result = await _client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(content => content.Text));
        if (result.IsError == true) {
            throw new InvalidOperationException($"The MCP tool '{tool}' failed: {text}");
        }
        return text;
    }

    static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)
        ?? throw new InvalidOperationException($"The bridge returned a payload that is not a {typeof(T).Name}.");
}
