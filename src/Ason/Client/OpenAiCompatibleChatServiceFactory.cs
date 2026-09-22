using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Ason;

/// <summary>
/// Builds an OpenAI-compatible <see cref="IChatCompletionService"/> from environment variables so
/// the same build can talk to OpenAI, DeepSeek, Azure OpenAI, Ollama, vLLM or any other endpoint
/// that speaks the OpenAI chat-completions protocol.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>MY_OPEN_AI_KEY</term><description>API key (falls back to OPENAI_API_KEY). Required.</description></item>
/// <item><term>MY_OPEN_AI_BASE_URL</term><description>Endpoint override, e.g. https://api.deepseek.com (falls back to OPENAI_BASE_URL). When unset the connector default (api.openai.com) is used.</description></item>
/// <item><term>MY_OPEN_AI_MODEL</term><description>Model id, e.g. deepseek-flash (falls back to OPENAI_MODEL, then to <see cref="DefaultModel"/>).</description></item>
/// </list>
/// </remarks>
public static class OpenAiCompatibleChatServiceFactory {

    public const string ApiKeyVariable = "MY_OPEN_AI_KEY";
    public const string BaseUrlVariable = "MY_OPEN_AI_BASE_URL";
    public const string ModelVariable = "MY_OPEN_AI_MODEL";

    /// <summary>Model id used when no model is supplied and no model variable is set.</summary>
    public const string DefaultModel = "gpt-4.1-mini";

    public static string ResolveApiKey()
        => FirstNonEmpty(Environment.GetEnvironmentVariable(ApiKeyVariable), Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

    public static string ResolveBaseUrl()
        => FirstNonEmpty(Environment.GetEnvironmentVariable(BaseUrlVariable), Environment.GetEnvironmentVariable("OPENAI_BASE_URL"));

    public static string ResolveModel(string? defaultModel = null)
        => FirstNonEmpty(
            Environment.GetEnvironmentVariable(ModelVariable),
            Environment.GetEnvironmentVariable("OPENAI_MODEL"),
            defaultModel,
            DefaultModel);

    /// <summary>
    /// Creates the chat service from <c>MY_OPEN_AI_KEY</c> / <c>MY_OPEN_AI_BASE_URL</c> /
    /// <c>MY_OPEN_AI_MODEL</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The API key or base URL is missing or malformed.</exception>
    public static IChatCompletionService FromEnvironment(string? defaultModel = null)
        => Create(ResolveApiKey(), ResolveModel(defaultModel), ResolveBaseUrl());

    /// <summary>
    /// Creates the chat service from explicit values. When <paramref name="baseUrl"/> is null or
    /// empty the connector default endpoint is used, which keeps existing OpenAI-only setups working.
    /// </summary>
    public static IChatCompletionService Create(string? apiKey, string? model = null, string? baseUrl = null) {
        if (string.IsNullOrWhiteSpace(apiKey)) {
            throw new InvalidOperationException(
                $"No API key configured. Set the {ApiKeyVariable} environment variable (or OPENAI_API_KEY) before starting the application.");
        }

        var modelId = FirstNonEmpty(model, DefaultModel);

        if (string.IsNullOrWhiteSpace(baseUrl)) {
            return new OpenAIChatCompletionService(modelId, apiKey!);
        }

        if (!Uri.TryCreate(baseUrl!.Trim(), UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)) {
            throw new InvalidOperationException(
                $"{BaseUrlVariable} must be an absolute http(s) URL such as https://api.deepseek.com, but was '{baseUrl}'.");
        }

        // DeepSeek and other OpenAI-compatible providers expect <base>/chat/completions, which is
        // exactly how the connector composes the request from this endpoint. The endpoint
        // constructor is marked [Experimental("SKEXP0010")]; suppressing it here is the documented
        // way to talk to a non-OpenAI endpoint with the OpenAI connector.
#pragma warning disable SKEXP0010
        return new OpenAIChatCompletionService(modelId, endpoint, apiKey!);
#pragma warning restore SKEXP0010
    }

    /// <summary>Human-readable description of the active configuration, without leaking the key.</summary>
    public static string DescribeConfiguration(string? defaultModel = null) {
        var baseUrl = ResolveBaseUrl();
        var model = ResolveModel(defaultModel);
        var key = ResolveApiKey();
        var keyState = string.IsNullOrWhiteSpace(key) ? "missing" : $"set ({key!.Length} chars)";
        var endpoint = string.IsNullOrWhiteSpace(baseUrl) ? "connector default (api.openai.com)" : baseUrl;
        return $"model={model}; baseUrl={endpoint}; {ApiKeyVariable}={keyState}";
    }

    static string FirstNonEmpty(params string?[] candidates) {
        foreach (var candidate in candidates) {
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate!.Trim();
        }
        return string.Empty;
    }
}
