using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// A deterministic <see cref="IChatCompletionService"/>: replies are handed out in the order the agents ask
/// for them, so a test can script the whole orchestration without a model. The last reply is reused for any
/// further request.
/// </summary>
internal sealed class ScriptedChatCompletionService : IChatCompletionService {

    readonly ConcurrentQueue<string> _replies = new();
    readonly string _fallback;

    public ScriptedChatCompletionService(params string[] replies) {
        _fallback = replies.Length > 0 ? replies[^1] : string.Empty;
        foreach (var reply in replies) _replies.Enqueue(reply);
    }

    /// <summary>Everything the "model" was asked, so a test can assert what the prompts contained.</summary>
    public List<string> Received { get; } = new();

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default) {
        Received.Add(string.Join("\n", chatHistory.Select(m => m.Content)));
        IReadOnlyList<ChatMessageContent> reply = new List<ChatMessageContent> { new(AuthorRole.Assistant, Next()) };
        return Task.FromResult(reply);
    }

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(string prompt, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default) {
        Received.Add(prompt);
        IReadOnlyList<ChatMessageContent> reply = new List<ChatMessageContent> { new(AuthorRole.Assistant, Next()) };
        return Task.FromResult(reply);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
        Received.Add(string.Join("\n", chatHistory.Select(m => m.Content)));
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, Next());
        await Task.CompletedTask;
    }

    string Next() => _replies.TryDequeue(out var reply) ? reply : _fallback;
}
