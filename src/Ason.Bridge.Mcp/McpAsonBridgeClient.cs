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

    /// <summary>Connects to an application's MCP endpoint, for example <c>http://localhost:5222/mcp</c>.</summary>
    public static async Task<McpAsonBridgeClient> ConnectAsync(string endpoint, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("An endpoint is required.", nameof(endpoint));
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(endpoint) });
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

    /// <summary>The whole-script interface.</summary>
    public async Task<AsonBridgeCallResult> ExecuteScriptAsync(string code, bool includeProxyPreamble = true, CancellationToken cancellationToken = default) {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["code"] = code
        };
        // Optional values are omitted rather than sent as null: that is what the tool schema advertises and
        // what an external MCP client does.
        if (!includeProxyPreamble) arguments["includeProxyPreamble"] = false;
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
