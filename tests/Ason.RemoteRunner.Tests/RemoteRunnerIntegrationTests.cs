using System;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Ason;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Ason.CodeGen;
using AsonRunner;

namespace Ason.RemoteRunner.Tests;

public class RemoteRunnerIntegrationTests {
    private static OperatorsLibrary Snapshot = new OperatorBuilder()
        .AddAssemblies(typeof(RootOperator).Assembly)
        .SetBaseFilter(mi => mi.GetCustomAttribute<AsonMethodAttribute>() != null)
        .Build();

    private sealed class DummyChat : IChatCompletionService {
        public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();
        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default) {
            IReadOnlyList<ChatMessageContent> list = new List<ChatMessageContent>{ new ChatMessageContent(AuthorRole.Assistant, "script")};
            return Task.FromResult(list);
        }
        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, "script");
            await Task.CompletedTask;
        }
    }

    [RemoteRunnerFact]
    public async Task EnableRemoteRunner_SetsFlags_And_StartsTransport() {
        // A remote runner server must be reachable; its URL comes from ASON_REMOTE_RUNNER_URL.
        var remoteUrl = Environment.GetEnvironmentVariable("ASON_REMOTE_RUNNER_URL")!;
        var chat = new DummyChat();
        var root = new RootOperator(new object());
        var client = new AsonClient(chat, root, Snapshot, new AsonClientOptions { SkipExplainerAgent = true, ExecutionMode = ExecutionMode.ExternalProcess });
        var runnerField = typeof(AsonClient).GetField("_runner", BindingFlags.NonPublic|BindingFlags.Instance);
        var runner = runnerField!.GetValue(client)!;
        var useRemoteProp = runner.GetType().GetProperty("UseRemote");
        Assert.False((bool)useRemoteProp!.GetValue(runner)!);
        await client.EnableRemoteRunnerAsync(remoteUrl, stopLocalIfRunning:true);
        Assert.True((bool)useRemoteProp.GetValue(runner)!);
        var remoteUrlProp = runner.GetType().GetProperty("RemoteUrl");
        Assert.Equal(remoteUrl.TrimEnd('/'), (string)remoteUrlProp!.GetValue(runner)!);
    }
}

/// <summary>
/// Integration test that needs a reachable remote runner (see Ason.RemoteBridge and the MAUI template).
/// Set ASON_REMOTE_RUNNER_URL (for example http://localhost:5222) to enable it; otherwise it is skipped
/// instead of failing on a refused connection.
/// </summary>
public sealed class RemoteRunnerFactAttribute : FactAttribute {
    public RemoteRunnerFactAttribute() {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASON_REMOTE_RUNNER_URL"))) {
            Skip = "Needs a running remote runner. Start one and set ASON_REMOTE_RUNNER_URL to enable this test.";
        }
    }
}
