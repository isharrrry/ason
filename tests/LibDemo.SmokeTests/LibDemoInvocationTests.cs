using System.Runtime.CompilerServices;
using Ason;
using Ason.CodeGen;
using AsonRunner;
using LibDemo;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace LibDemoSmokeTests;

/// <summary>
/// These run real scripts through <see cref="AsonClient.ExecuteScriptDirectAsync"/> so the whole
/// in-process path is exercised: generated proxy -> host bridge -> operator invoker -> LibDemo method.
/// The chat service is a stub because no agent is involved in direct script execution.
/// </summary>
public class LibDemoInvocationTests {

    [Fact]
    public async Task Instance_marker_only_operator_is_invocable() {
        var (client, logs) = CreateClient();
        using (client) {
            var result = await ExecuteAsync(client, logs, "libDemoOperator.Echo(\"hello from script\")");

            Assert.Equal("LibDemo received: hello from script", result);
        }
    }

    [Fact]
    public async Task Instance_marker_only_operator_exposes_models() {
        var (client, logs) = CreateClient();
        using (client) {
            var result = await ExecuteAsync(client, logs, "libDemoOperator.GetLibraryTimestamp()");

            Assert.True(DateTime.TryParse(result, out _), $"expected a timestamp, got '{result}'");
        }
    }

    [Fact]
    public async Task Static_operator_module_is_invocable() {
        var (client, logs) = CreateClient();
        using (client) {
            Assert.Equal("6", await ExecuteAsync(client, logs, "LibDemoStaticOperator.Add(2, 4)"));
            Assert.Equal("abab", await ExecuteAsync(client, logs, "LibDemoStaticOperator.Repeat(\"ab\", 2)"));
        }
    }

    [Fact]
    public void Static_operator_module_is_described_to_the_script_agent() {
        var library = new OperatorBuilder().AddAssemblies(typeof(LibDemoStaticOperator).Assembly).Build();
        var (proxies, signatures, _) = library.BuildTask.GetAwaiter().GetResult();

        Assert.Contains("LibDemoStaticOperator", signatures);
        Assert.Contains("LibDemoOperator", signatures);
        // Static modules are emitted as static proxies, marker-only instances get a handle constructor.
        Assert.Contains("public static class LibDemoStaticOperator", proxies);
        Assert.Contains("public LibDemoOperator(string handle)", proxies);
    }

    static async Task<string> ExecuteAsync(AsonClient client, List<string> logs, string script) {
        var result = await client.ExecuteScriptDirectAsync(script);
        Assert.True(result != "Error",
            $"script '{script}' failed. Execution log:\n{string.Join("\n", logs)}");
        return result;
    }

    static (AsonClient Client, List<string> Logs) CreateClient() {
        var library = new OperatorBuilder()
            .AddAssemblies(typeof(LibDemoOperator).Assembly)
            .Build();

        var logs = new List<string>();
        var client = new AsonClient(new StubChatCompletionService(), new RootOperator(new object()), library,
            new AsonClientOptions { ExecutionMode = ExecutionMode.InProcess });
        client.Log += (_, e) => logs.Add($"[{e.Level}] {e.Message}{(string.IsNullOrEmpty(e.Exception) ? string.Empty : Environment.NewLine + e.Exception)}");

        return (client, logs);
    }

    sealed class StubChatCompletionService : IChatCompletionService {
        public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ChatMessageContent>>(Array.Empty<ChatMessageContent>());

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await Task.CompletedTask;
            yield break;
        }
    }
}
