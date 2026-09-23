using System.Text.Json;
using System.Text.Json.Serialization;
using Ason.Transport;

namespace Ason.Bridge.Mcp;

/// <summary>
/// The runner protocol carried over MCP. Functionally the twin of the gRPC transport: an agent's
/// <c>RunnerClient</c> sends an <c>exec</c> line, the application evaluates it and the result comes back, so
/// the agent keeps the whole ASON pipeline while the script runs inside the application.
///
/// Use gRPC when both ends are .NET (it is the cheaper channel); use MCP when the agent only speaks MCP.
/// </summary>
public sealed class McpAsonBridgeTransport : IRunnerTransport {

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    readonly McpAsonBridgeClient _client;

    public McpAsonBridgeTransport(McpAsonBridgeClient client) => _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>
    /// The proxy layer the caller prepends to every script - normally <c>manifest.Proxies</c> as it was when
    /// the caller read the manifest. When set, it is stripped back off before sending and the application is
    /// asked for its own current proxy layer and instance declarations, which is what keeps a script that
    /// references a live view working after that view was closed and reopened. Unset means the code travels
    /// exactly as the caller composed it.
    /// </summary>
    public string? Proxies { get; init; }

    public bool IsStarted { get; private set; }

    public event Action<string>? LineReceived;
    public event Action<string>? Closed;

    public Task StartAsync() {
        IsStarted = true;
        return Task.CompletedTask;
    }

    public Task StopAsync() {
        IsStarted = false;
        return Task.CompletedTask;
    }

    public async Task SendAsync(string jsonLine) {
        using var document = JsonDocument.Parse(jsonLine);
        var root = document.RootElement;
        var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? NewId() : NewId();

        switch (type) {
            case "exec": {
                var code = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() ?? string.Empty : string.Empty;
                var body = StripSnapshotLayer(code, out var freshInstances);
                var result = await _client.ExecuteScriptAsync(body, includeProxyPreamble: false, includeInstanceDeclarations: freshInstances).ConfigureAwait(false);
                LineReceived?.Invoke(Serialize(new { id, type = "execResult", result = result.Result, error = result.Success ? null : result.Error }));
                break;
            }

            // Operators are resolved inside the application, so an invoke must never travel this way; answering
            // with an error keeps a misconfigured deployment from hanging.
            case "invoke":
            case "invokeMcp":
                LineReceived?.Invoke(Serialize(new { id, type = "invokeResult", result = (object?)null, error = InvokeNotExpected() }));
                break;
        }
    }

    static string InvokeNotExpected() =>
        "The application answered an 'invoke' request, but operators are resolved inside the application process. " +
        "This means the bridge is pointed at an executor that is not the application itself.";

    /// <summary>
    /// Removes the caller's snapshot proxy layer from the composed script so the application rebuilds it with
    /// today's instance declarations. The comparison is against the exact text the caller prepended; anything
    /// else travels untouched.
    /// </summary>
    string StripSnapshotLayer(string code, out bool freshInstances) {
        freshInstances = false;
        if (Proxies is not { Length: > 0 } proxies || !code.StartsWith(proxies, StringComparison.Ordinal)) return code;
        freshInstances = true;
        return code[proxies.Length..];
    }

    static string Serialize(object payload) => JsonSerializer.Serialize(payload, Json);

    static string NewId() => Guid.NewGuid().ToString("N");
}
