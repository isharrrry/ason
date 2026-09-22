# AI 提供商

[English](ai-providers.md) | **中文** | [Español](ai-providers.es.md)

> 本文档是 **ASON** 文档的一部分 —— 返回 [README](../README.zh-CN.md)。

ASON 从不直接与模型通信：你把任意的 Semantic Kernel `IChatCompletionService` 交给 `AsonClient`（或 `AddAson`）。模板和示例通过三个环境变量来构建该服务，这对于 OpenAI 以及任何暴露 OpenAI 兼容 API 的提供商来说已经足够。

## 环境变量

| 变量 | 用途 | 默认值 |
|---|---|---|
| `MY_OPEN_AI_KEY` | API key。回退到 `OPENAI_API_KEY`。 | *必填* |
| `MY_OPEN_AI_BASE_URL` | 端点覆盖项。回退到 `OPENAI_BASE_URL`。 | 连接器默认值（`api.openai.com`） |
| `MY_OPEN_AI_MODEL` | 模型 id。回退到 `OPENAI_MODEL`。 | `gpt-4.1-mini` |

`Ason.OpenAiCompatibleChatServiceFactory` 会读取它们：

```csharp
IChatCompletionService chatService = OpenAiCompatibleChatServiceFactory.FromEnvironment();
```

如果未配置 base URL，它会使用普通的 `OpenAIChatCompletionService(modelId, apiKey)` 构造函数，因此仅使用 OpenAI 的配置可以保持不变地继续工作。自定义端点的构造函数被 Semantic Kernel 标记为实验性（`SKEXP0010`）；该工厂会在内部抑制该诊断信息，因此使用方不必自行处理。

## DeepSeek

DeepSeek 暴露了 OpenAI 兼容的 API —— 参见其 [API 文档](https://api-docs.deepseek.com/)。

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

请求会发送到 `<MY_OPEN_AI_BASE_URL>/chat/completions`，这与 DeepSeek 文档中给出的路径一致。模型名称会随时间变化 —— 在撰写本页时，`deepseek-flash` 和 `deepseek-v4-pro` 是当时可用的名称；在固定某个名称之前，请先查阅他们的文档。

## 其他 OpenAI 兼容端点

| 提供商 | Base URL | 模型 |
|---|---|---|
| OpenAI | *（留空）* | `gpt-4.1-mini` |
| DeepSeek | `https://api.deepseek.com` | `deepseek-flash` |
| Ollama（本地） | `http://localhost:11434/v1` | 你已拉取的任意模型 |
| vLLM、LM Studio、其他本地服务器 | `http://<host>:<port>/v1` | 你对外提供服务时使用的名称 |

## 不兼容 OpenAI 的提供商

对于 Azure OpenAI、Gemini、Anthropic 以及其他提供商，请自行构造 Semantic Kernel 服务，并将其传给 `AsonClient` 或 `AddAson`：

```csharp
IChatCompletionService chatService = new AzureOpenAIChatCompletionService(deploymentName, endpoint, apiKey);
```

可用的连接器已列在 [Chat Completion | Overview](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp-Google%2Cpython-AzureOpenAI%2Cjava-AzureOpenAI&pivots=programming-language-csharp) 中。

## 故障排查

| 现象 | 可能原因 |
|---|---|
| 启动时出现 `ArgumentException: The value cannot be an empty string (Parameter 'apiKey')` | 未配置 key。桌面应用在创建聊天视图时会读取该变量，因此请在启动**之前**设置它。 |
| 回复中出现 `401` 或 `invalid_api_key` | 该 key 不属于 `MY_OPEN_AI_BASE_URL` 所指向的提供商。 |
| `/chat/completions` 上出现 `404` | base URL 包含了该提供商未使用的路径段。DeepSeek 期望的是裸的 `https://api.deepseek.com`；OpenAI 风格的本地服务器通常需要以 `/v1` 结尾。 |
| 回复忽略了你配置的模型 | 未设置 `MY_OPEN_AI_MODEL`，因此使用了默认的 `gpt-4.1-mini`。 |
