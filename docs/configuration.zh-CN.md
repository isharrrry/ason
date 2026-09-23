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
| `AnswerLanguage` | `string?` | `null` | 指定回答所用语言（BCP-47，如 `zh-CN`）；见[让回答使用指定语言](#让回答使用指定语言) |
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
| `TransportFactory` | `Func<IRunnerTransport>?` | `null` | 由宿主提供的传输，取代由执行模式与远程运行器开关构建的那一个。Agent 借此把运行器指向[桥](app-agent-separation.zh-CN.md)背后的应用，同时保留代理生成、重试与结果处理 |

这三个提示词属性默认为 `null`，表示 ASON 会使用内置提示词。这些预设是**公开可读**的：`Ason.AgentPrompts` 暴露了 `ScriptAgentTemplate`、`ReceptionAgentTemplate`、`ExplainerAgentTemplate` 与 `TextToDataAgentTemplate`，而 `AgentPrompts.BuildScriptInstructions(apiSignatures)` 会把生成的 operator API 填入脚本提示词。

### 读取预设并追加自己的规则

预设就是普通字符串，因此应用可以在构造 `AsonClientOptions` **之前**先读取预设，拼接自己的规则再赋值。WPF 示例正是用这种方式让非英文的 Windows 用用户自己的语言作答：

```csharp
var culture = CultureInfo.CurrentUICulture;
var directive = $"""
    Language rule: always answer the user in {culture.EnglishName} ({culture.Name}).
    Write every user-facing sentence in that language, even when earlier answers in this
    conversation were written in another one.

    """;

AsonClientOptions options = new() {
    ReceptionInstructions = directive + AgentPrompts.ReceptionAgentTemplate,
    ExplainerInstructions = directive + AgentPrompts.ExplainerAgentTemplate,
};
```

| 属性 | `null` 时的默认值 | 赋值后如何使用 |
| --- | --- | --- |
| `ReceptionInstructions` | `AgentPrompts.ReceptionAgentTemplate` | 纯文本，作为 agent 指令发送。可自由前置或追加。 |
| `ExplainerInstructions` | `AgentPrompts.ExplainerAgentTemplate` | 纯文本，作为 agent 指令发送。可自由前置或追加。 |
| `ScriptInstructions` | 把 operator API 填入 `{0}` 后的脚本预设 | **原样**发送，不会做格式化，所以 `{0}` 会保持字面量。请用 `AgentPrompts.BuildScriptInstructions(api)` 构造，并注意这样会覆盖 ASON 内部追加到预设 API 块之后的 operator 实例声明。 |

两点实践经验：

- 规则必须作用到真正输出可见文本的 agent：直接回答来自 `Reception`，结果说明来自 `Explainer`。只给 `ScriptInstructions` 加前缀不会改变回答语言，因为 Script Agent 只输出 C#（它表示无法完成时会刻意以字面单词 `Cannot` 开头，重试逻辑会对该词做字符串匹配）。
- 对话历史会把模型拉回之前几轮的语言。请在指令里显式说明，如上面片段中的 “even when earlier answers in this conversation were written in another one”。

### 让回答使用指定语言

如果目标只是"用用户能读的语言回答"，宿主完全不必碰提示词：设置 `AnswerLanguage`，ASON 会替你前置规则：

<!-- i18n: localize-labels - 代码完全一致，只有行内注释本地化 -->
```csharp
AsonClientOptions options = new() {
    // 例如 "zh-CN"；英文系统保持 null，这样预设原样使用。
    AnswerLanguage = CultureInfo.CurrentUICulture.Name,
};
```

| 问题 | 行为 |
| --- | --- |
| 规则作用到哪些提示词？ | Reception 与 Explainer —— 只有这两个 agent 写用户看得到的文字 |
| 为什么不作用于 Script？ | 它只输出 C#（脚本本身从不展示），而且它表示"无法完成"的句子必须以字面单词 `Cannot` 开头，重试逻辑正是靠对该前缀做字符串匹配来短路 |
| 与自定义提示词同时设置 | 即使覆盖了 `ReceptionInstructions` / `ExplainerInstructions`，规则仍会前置：回答语言是宿主级行为，不属于某个预设 |
| 与对话历史的关系 | 规则会明确要求模型不要沿用前几轮的语言，否则历史会把回答拉回上一种语言 |
| 名字格式非法（`en/US`） | `AsonClient` 构造函数抛异常，启动时即可发现 |
| 未注册的标签（`zz`） | 无法识别：运行时接受任何格式合法的 BCP-47 标签并原样传递，因此它会直接进入模型提示词 |
| 不设置（默认） | 没有任何变化：`null` 时每个提示词完全保持原样 |

`AgentPrompts.BuildLanguageDirective(language)` 负责构造这条规则，`AgentPrompts.WithAnswerLanguage(preset, language)` 可把它套用到任意提示词上，供想自己拼提示词的宿主使用（例如同时追加自己的规则）。

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
        opt.ExecutionMode = ExecutionMode.Docker;  
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

