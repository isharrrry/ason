# 客户端配置与注册

[English](configuration.md) | **中文** | [Español](configuration.es.md)

> 本文档是 **ASON** 文档的一部分 —— 返回 [README](../README.zh-CN.md)。

## 配置客户端并发送任务

要向 ASON 发送任务，请创建一个 `AsonClient` 实例：

```csharp
IChatCompletionService chatService = new OpenAIChatCompletionService(modelId: "gpt-4.1-mini", apiKey: apiKey);  
OperatorsLibrary operatorLibrary = new OperatorBuilder()  
    .AddAssemblies(typeof(MainAppOperator).Assembly)  
    .Build();  

mainAppOperator = new MainAppOperator(this);  

AsonClientOptions options = new() {  
    ExecutionMode = ExecutionMode.ExternalProcess  
};  

asonChatClient = new AsonClient(chatService, mainAppOperator, operatorLibrary, options);
```

**构造函数参数：**

- `IChatCompletionService defaultChatCompletion` —— ASON agent 使用的聊天补全服务。支持 OpenAI、Azure、Gemini、Ollama、Anthropic 以及 Semantic Kernel 支持的其他提供程序（参见：[Chat Completion | Overview](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp-Google%2Cpython-AzureOpenAI%2Cjava-AzureOpenAI&pivots=programming-language-csharp)）。  
- `RootOperator rootOperator` —— 你的 root 应用 operator 的实例。  
- `OperatorsLibrary operators` —— 由 `OperatorBuilder` 创建的 operator 库。  
- `AsonClientOptions options` —— 定义执行环境，允许覆盖提示词、自定义 agent 配置以及其他行为。

创建 `AsonClient` 之后，你可以使用 `SendStreamingAsync` 或 `SendAsync` 方法发送用户任务：

```csharp
await foreach (var token in asonChatClient.SendStreamingAsync(userText)) {  
    ChatResponse += token;  
}  

// or  

ChatResponse = await asonChatClient.SendAsync(userText);
```

## AsonClientOptions 参考

| 属性 | 类型 | 默认值 | 用途 |
|---|---|---|---|
| `Logger` | `ILogger?` | `null` | ASON 自身诊断信息的日志输出目标 |
| `MaxFixAttempts` | `int` | `2` | 失败的脚本被送回修复的次数 |
| `ScriptInstructions` | `string?` | `null` | 覆盖 Script Agent 的提示词 |
| `ReceptionInstructions` | `string?` | `null` | 覆盖 Reception Agent 的提示词 |
| `ExplainerInstructions` | `string?` | `null` | 覆盖 Explainer Agent 的提示词 |
| `ScriptChatCompletion` | `IChatCompletionService?` | `null` | 专用于 Script Agent 的模型 |
| `ReceptionChatCompletion` | `IChatCompletionService?` | `null` | 专用于 Reception Agent 的模型 |
| `ExplainerChatCompletion` | `IChatCompletionService?` | `null` | 专用于 Explainer Agent 的模型 |
| `SkipReceptionAgent` | `bool` | `false` | 将用户消息直接发送给 Script Agent |
| `SkipExplainerAgent` | `bool` | `false` | 返回原始脚本结果，不附带自然语言解释 |
| `ExecutionMode` | `ExecutionMode` | `InProcess` | `InProcess`、`ExternalProcess` 或 `Docker` —— 参见[执行模式](execution-modes.zh-CN.md) |
| `AllowTextExtractor` | `bool` | `true` | 向脚本公开内置的提取 operator |
| `ForbiddenScriptKeywords` | `string[]` | 内置拒绝列表 | 静态分析拒绝列表（`System.IO`、`Process.Start`、`System.Reflection` 等） |
| `UseRemoteRunner` | `bool` | `false` | 通过远程运行器运行脚本；需要 `RemoteRunnerBaseUrl` |
| `RemoteRunnerBaseUrl` | `string?` | `null` | 远程脚本运行器的 URL |
| `RemoteRunnerDockerImage` | `string?` | `ghcr.io/alexgoon/ason:<version>` | 远程运行器运行在 Docker 中时使用的镜像 |
| `StopLocalRunnerWhenEnablingRemote` | `bool` | `true` | 远程运行器启用后停止本地运行器 |
| `AdditionalMethodFilter` | `Func<MethodInfo, bool>?` | `null` | 在生成的 operator 快照之上额外应用的过滤器 |
| `RunnerExecutablePath` | `string?` | `null` | `Ason.ExternalExecutor`（dll 或 exe）的显式路径 |

## 服务注册（ASP.NET Core / Blazor）

你可以使用 `AddAson` 将 `AsonClient` 注册为服务容器中的 scoped 依赖项：

```csharp
builder.Services.AddAson(  
    defaultChatCompletionFactory: sp => new OpenAIChatCompletionService("gpt-4.1-mini", Environment.GetEnvironmentVariable("MY_OPEN_AI_KEY") ?? string.Empty),  
    rootOperatorFactory: sp => sp.GetRequiredService().MainAppOperator,  
    operators: new OperatorBuilder()  
        .AddAssemblies(typeof(BlazorMainAppOperator).Assembly)  
        .AddExtractor()  
        .Build(),  
    configureOptions: opt => {  
        opt.RunnerMode = ExecutionMode.Docker;  
    }  
);
```


## 日志

ASON 通过 `AsonClient.Log` 事件提供集中式日志，该事件会报告来自所有执行层级的活动。

```csharp
asonChatClient.Log += (o, e) => Debug.WriteLine($"{e.Source}: {e.Message}");
```

这有助于跟踪生成的脚本、捕获错误，并监控来自客户端与执行环境的运行时事件。

`AsonLogEventArgs` 包含以下属性：

- `Level`  
- `Message`  
- `Exception`  
- `Source`

