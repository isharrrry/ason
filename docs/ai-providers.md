# AI providers

[English](ai-providers.md) | [中文](ai-providers.zh-CN.md) | [Español](ai-providers.es.md)

> Part of the **ASON** documentation — back to the [README](../README.md).

ASON never talks to a model directly: you hand `AsonClient` (or `AddAson`) any Semantic Kernel `IChatCompletionService`. The templates and samples build that service from three environment variables, which is enough for OpenAI and for any provider that exposes an OpenAI-compatible API.

## Environment variables

| Variable | Purpose | Default |
|---|---|---|
| `MY_OPEN_AI_KEY` | API key. Falls back to `OPENAI_API_KEY`. | *required* |
| `MY_OPEN_AI_BASE_URL` | Endpoint override. Falls back to `OPENAI_BASE_URL`. | connector default (`api.openai.com`) |
| `MY_OPEN_AI_MODEL` | Model id. Falls back to `OPENAI_MODEL`. | `gpt-4.1-mini` |

`Ason.OpenAiCompatibleChatServiceFactory` reads them:

```csharp
IChatCompletionService chatService = OpenAiCompatibleChatServiceFactory.FromEnvironment();
```

With no base URL configured it uses the plain `OpenAIChatCompletionService(modelId, apiKey)` constructor, so OpenAI-only setups keep working unchanged. The custom-endpoint constructor is marked experimental by Semantic Kernel (`SKEXP0010`); the factory suppresses that diagnostic internally, so consumers do not have to.

## DeepSeek

DeepSeek exposes an OpenAI-compatible API — see their [API docs](https://api-docs.deepseek.com/).

```powershell
$env:MY_OPEN_AI_KEY      = "sk-..."
$env:MY_OPEN_AI_BASE_URL = "https://api.deepseek.com"
$env:MY_OPEN_AI_MODEL    = "deepseek-flash"
```

```bash
export MY_OPEN_AI_KEY=sk-...
export MY_OPEN_AI_BASE_URL=https://api.deepseek.com
export MY_OPEN_AI_MODEL=deepseek-flash
```

Requests are sent to `<MY_OPEN_AI_BASE_URL>/chat/completions`, which matches the path DeepSeek documents. Model names change over time — `deepseek-flash` and `deepseek-v4-pro` were current when this page was written; check their documentation before pinning a name.

## Other OpenAI-compatible endpoints

| Provider | Base URL | Model |
|---|---|---|
| OpenAI | *(leave unset)* | `gpt-4.1-mini` |
| DeepSeek | `https://api.deepseek.com` | `deepseek-flash` |
| Ollama (local) | `http://localhost:11434/v1` | any model you have pulled |
| vLLM, LM Studio, other local servers | `http://<host>:<port>/v1` | the name you served |

## Providers that are not OpenAI-compatible

For Azure OpenAI, Gemini, Anthropic and the rest, construct the Semantic Kernel service yourself and pass it to `AsonClient` or `AddAson`:

```csharp
IChatCompletionService chatService = new AzureOpenAIChatCompletionService(deploymentName, endpoint, apiKey);
```

The available connectors are listed in [Chat Completion | Overview](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp-Google%2Cpython-AzureOpenAI%2Cjava-AzureOpenAI&pivots=programming-language-csharp).

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `ArgumentException: The value cannot be an empty string (Parameter 'apiKey')` at startup | No key configured. Desktop apps read the variable while the chat view is created, so set it **before** launching. |
| `401` or `invalid_api_key` in the reply | The key does not belong to the provider that `MY_OPEN_AI_BASE_URL` points at. |
| `404` on `/chat/completions` | The base URL includes a path segment the provider does not use. DeepSeek expects the bare `https://api.deepseek.com`; OpenAI-style local servers usually need a trailing `/v1`. |
| The reply ignores the model you configured | `MY_OPEN_AI_MODEL` was not set, so the default `gpt-4.1-mini` was used. |
