using System.Collections.Concurrent;
using Ason.Tests.Infrastructure;
using Ason.Transport;
using AsonRunner;

namespace Ason.Tests.Transport;

/// <summary>
/// An agent host configures its transport through options, because a bridge endpoint is deployment
/// configuration - the same reason the execution mode lives there.
/// </summary>
public class AsonClientTransportFactoryTests {

    [Fact]
    public async Task AsonClient_sends_its_script_through_the_transport_from_the_options() {
        var transport = new RecordingTransport();
        var script = "return 42;";
        var client = new AsonClient(
            new StubChatCompletionService(script),
            new RootOperator(new object()),
            TestLibrary(),
            new AsonClientOptions {
                ExecutionMode = ExecutionMode.ExternalProcess,
                TransportFactory = () => transport,
                ForbiddenScriptKeywords = Array.Empty<string>(),
                SkipReceptionAgent = true,
                SkipExplainerAgent = true
            });

        await client.SendAsync("add something");

        var line = Assert.Single(transport.Sent);
        Assert.Contains("\"exec\"", line);
        Assert.Contains("return 42;", line);
    }

    static Ason.CodeGen.OperatorsLibrary TestLibrary() =>
        // The proxy layer has to be non-empty: the client refuses to run without one. Its content does not
        // matter here because the transport intercepts the script before it reaches any interpreter.
        new(Task.FromResult(("// proxy layer for this test", "// signatures for this test", (Ason.Invocation.IOperatorMethodCache)new NullCache())), false, Array.Empty<ModelContextProtocol.Client.IMcpClient>(), Array.Empty<System.Reflection.Assembly>());

    sealed class NullCache : Ason.Invocation.IOperatorMethodCache {
        public bool TryGet(Type declaringType, string name, int argCount, out Ason.Invocation.OperatorMethodEntry entry) { entry = null!; return false; }
        public bool TryGetStatic(string targetTypeName, string name, int argCount, out Ason.Invocation.OperatorMethodEntry entry) { entry = null!; return false; }
        public Ason.Invocation.OperatorMethodEntry GetOrAddClosedGeneric(Ason.Invocation.OperatorMethodEntry openEntry, Type[] typeArguments) => openEntry;
    }

    sealed class RecordingTransport : IRunnerTransport {
        public bool IsStarted { get; private set; }
        public List<string> Sent { get; } = new();
        public event Action<string>? LineReceived;
        public event Action<string>? Closed;

        public Task StartAsync() { IsStarted = true; return Task.CompletedTask; }

        public Task SendAsync(string jsonLine) {
            Sent.Add(jsonLine);
            using var document = System.Text.Json.JsonDocument.Parse(jsonLine);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "exec") {
                var id = root.GetProperty("id").GetString();
                LineReceived?.Invoke($"{{\"id\":\"{id}\",\"type\":\"execResult\",\"result\":1}}");
            }
            return Task.CompletedTask;
        }

        public Task StopAsync() { IsStarted = false; return Task.CompletedTask; }
    }
}
