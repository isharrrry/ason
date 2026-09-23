using System.Collections.Concurrent;
using System.Text.Json;
using Ason.Transport;
using AsonRunner;

namespace Ason.Tests.Transport;

/// <summary>
/// The bridge reaches an application through a transport of its own (gRPC, MCP), so the runner must accept a
/// host-supplied transport instead of only the two it builds itself. These tests pin that seam: an injected
/// transport is used, it wins over the execution mode, and it is created lazily and recreated after a restart.
/// </summary>
public class CustomTransportTests {

    [Fact]
    public async Task RunnerClient_sends_exec_over_a_host_supplied_transport() {
        var transport = new RecordingTransport();
        var client = CreateClient(ExecutionMode.ExternalProcess);
        client.UseTransport(() => transport);

        var result = await client.ExecuteAsync("return 42;");

        Assert.True(transport.IsStarted);
        var line = Assert.Single(transport.Sent);
        Assert.Contains("\"exec\"", line);
        Assert.Equal(42, result!.Value.GetInt32());
    }

    [Fact]
    public async Task A_host_supplied_transport_wins_over_the_execution_mode() {
        var transport = new RecordingTransport();
        var client = CreateClient(ExecutionMode.InProcess);
        client.UseTransport(() => transport);

        // Deliberately not valid C#: if the runner evaluated this locally it would throw instead of returning
        // the transport's reply, which is how this asserts the script never reached this process.
        var result = await client.ExecuteAsync("this is not a script");

        Assert.Equal(42, result!.Value.GetInt32());
        Assert.Single(transport.Sent);
    }

    [Fact]
    public async Task The_transport_is_created_lazily_and_recreated_after_a_restart() {
        var created = 0;
        var client = CreateClient(ExecutionMode.ExternalProcess);
        client.UseTransport(() => { created++; return new RecordingTransport(); });

        Assert.Equal(0, created);
        await client.ExecuteAsync("return 1;");
        Assert.Equal(1, created);

        await client.StopAsync();
        await client.ExecuteAsync("return 2;");
        Assert.Equal(2, created);
    }

    static RunnerClient CreateClient(ExecutionMode mode) =>
        new(new ConcurrentDictionary<string, OperatorBase>(StringComparer.Ordinal), null) { Mode = mode };

    /// <summary>
    /// A transport that answers every <c>exec</c> with the same result, so a test can tell "the runner went
    /// somewhere else" from "the runner evaluated it here".
    /// </summary>
    sealed class RecordingTransport : IRunnerTransport {

        public bool IsStarted { get; private set; }
        public List<string> Sent { get; } = new();

        public event Action<string>? LineReceived;
        public event Action<string>? Closed;

        public Task StartAsync() {
            IsStarted = true;
            return Task.CompletedTask;
        }

        public Task SendAsync(string jsonLine) {
            Sent.Add(jsonLine);
            using var document = JsonDocument.Parse(jsonLine);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "exec") {
                var id = root.GetProperty("id").GetString();
                LineReceived?.Invoke($"{{\"id\":\"{id}\",\"type\":\"execResult\",\"result\":42}}");
            }
            return Task.CompletedTask;
        }

        public Task StopAsync() {
            IsStarted = false;
            return Task.CompletedTask;
        }
    }
}
