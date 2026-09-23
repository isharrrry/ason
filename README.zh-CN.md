# ASON (Agent Script Operation)
![ASON Logo](images/ason_logo.jpeg)

[English](README.md) | **中文** | [Español](README.es.md)

**ASON** 是一个库，它让 AI 模型能够根据自然语言的用户请求，在你的应用程序中生成脚本并执行函数序列。与传统的 **MCP** 或 **tool calling** 机制不同，ASON 一次性生成并运行完整脚本。

这种架构使得处理多步骤数据编辑、分析类请求（例如，基于你的数据构建动态图表），或从大型数据源中提取信息成为可能。不需要复杂的基础设施 —— ASON 为强大的 AI 集成提供了简单的入口。

在将 AI 引入你的应用程序时，ASON 提供了更高的灵活性、性能和效率。更多细节，请参阅 [ASON 相对于 Tool Calling / MCP 的优势](#ason-相对于-tool-calling--mcp-的优势)。

> [!Note]
> ASON 是一个新库，其公共 API 在未来版本中可能会发生变化。如果你希望看到 ASON 持续成长，请通过**给它点一个 star** 来支持它！

**要求：** `Ason` 库面向 **.NET 6.0** 和 **.NET 9.0**。仅含标记的包 `Ason.Abstractions` 面向 **netstandard2.0**，因此它的特性（attribute）可以从 .NET Framework 4.6.1+、.NET Core 和 .NET 6+ 类库中引用。

## Online Demo

你可以在实时示例应用中体验 ASON（目前托管在免费的 Azure 计划上，因此可能存在使用限制）：  [**ASON Demo**](https://ason-demo-linux-hegxhud6c8cmfkfm.centralus-01.azurewebsites.net/)

该演示的**源代码**可在 GitHub 上获取：[ason-demo](https://github.com/Alexgoon/ason-demo)

**介绍视频：** [ASON – Actionable AI in .NET Apps (Intro & Demo)](https://youtu.be/dKBkVTl3e_c?si=N0zoIE1EAnC6KoS1)  [![YouTube](https://img.shields.io/badge/Watch_on_YouTube-red?logo=youtube&logoColor=white)](https://youtu.be/dKBkVTl3e_c?si=N0zoIE1EAnC6KoS1)


## Quick start

**1. 安装 ASON 项目模板**

```
> dotnet new install Ason.ProjectTemplates
```

**2. 创建项目**
你可以创建 Blazor、WPF、WinForms、MAUI 或 Console 应用程序：

```
> dotnet new ason.blaz.srv -n MyAsonProject
```

除了 `ason.blaz.srv`，你还可以使用 `ason.wpf`、`ason.winforms`、`ason.maui` 或 `ason.console`。

**3. 配置你的 AI 提供方**

任何与 OpenAI 兼容的端点都可以使用。模板和示例会读取三个环境变量：

| 变量 | 用途 | 示例 |
|---|---|---|
| `MY_OPEN_AI_KEY` | API 密钥（必填） | `sk-...` |
| `MY_OPEN_AI_BASE_URL` | 端点覆盖；未设置时使用连接器的默认值（`api.openai.com`） | `https://api.deepseek.com` |
| `MY_OPEN_AI_MODEL` | 模型 id；默认为 `gpt-4.1-mini` | `deepseek-flash` |

使用 **DeepSeek**（OpenAI 兼容 API）：

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

模板通过 `Ason` 自带的辅助工具创建服务：

```csharp
IChatCompletionService chatService = OpenAiCompatibleChatServiceFactory.FromEnvironment();
```

关于其他端点、模型名称和故障排查，请参阅 **[AI providers](docs/ai-providers.zh-CN.md)**。

---

## ASON 如何工作

以下是 ASON 架构的简化概览。

![ASON Flow Overview](images/flow-overview.jpg)
1. 你的应用程序定义 **operator**，它们执行你代码中的方法。  
2. **ASON Client** 创建 API（operator 签名），并将它们连同用户的任务一起传递给 **Script Agent**。  
3. **Script Agent** 生成脚本，并将其发送到**执行环境**（同一进程、外部进程、Docker 容器或远程服务器）。  
4. **执行环境**调用动态生成的代理，由这些代理调用真正的 operator 方法。

> 深入阅读：[架构、代理以及与 tool calling / MCP 的比较](docs/architecture.zh-CN.md)。

## Operators

**operator** 是一个类，其中包含暴露给 Script Agent 的方法。  
要定义一个 operator，请创建一个继承自 `OperatorBase` 的类。  
将 `[AsonOperator]` 特性应用于该类，并将 `[AsonMethod]` 应用于每个你想要暴露的方法。

示例：

```csharp
[AsonOperator]  
public class OrdersViewOperator : OperatorBase<OrdersViewModel> {  
    [AsonMethod]  
    public void DeleteOrder(int orderId) => AttachedObject?.DeleteOrder(orderId);  
}
```

operator 会附加到包含业务逻辑的对象上。这些对象存储在 `AttachedObject` 属性中。

要将 operator 附加到一个对象上，请调用 `AttachChildOperator`：

```csharp
public partial class OrdersViewModel {  
    public void DeleteOrder(int orderId) => Debug.WriteLine($"Deleted order {orderId}");  
    public OrdersViewModel(RootOperator rootOperator) {  
        rootOperator.AttachChildOperator<OrdersViewOperator>(this);  
    }  
}
```

无状态 operator 不需要视图生命周期：`static class` 会成为 operator **模块**，而带有公共无参构造函数的 operator 类会自动注册为单例。

```csharp
[AsonOperator]
public static class LibDemoStaticOperator {
    [AsonMethod]
    public static int Add(int left, int right) => left + right;   // scripts call LibDemoStaticOperator.Add(2, 4)
}
```

> 深入阅读：[编写 operator](docs/operators.zh-CN.md) —— root operator、视图生命周期、无状态模块、模型以及 `Ason.Abstractions` 包。

## 领域模型

通过应用 `[AsonModel]` 将模型暴露给 Script Agent；客户端与执行环境之间的序列化会为你处理。完整示例：[领域模型](docs/operators.zh-CN.md#领域模型)。

## 配置客户端并发送任务

要将任务发送给 ASON，请创建 `AsonClient` 的实例：

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

- `IChatCompletionService defaultChatCompletion` —— ASON 代理使用的聊天补全服务。支持 OpenAI、Azure、Gemini、Ollama、Anthropic、**任何与 OpenAI 兼容的端点，例如 DeepSeek**（参见上文 Quick start 的第 3 步），以及 Semantic Kernel 支持的其他提供方（参见：[Chat Completion | Overview](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp-Google%2Cpython-AzureOpenAI%2Cjava-AzureOpenAI&pivots=programming-language-csharp)）。  
- `RootOperator rootOperator` —— 你的根应用 operator 的实例。  
- `OperatorsLibrary operators` —— 由 `OperatorBuilder` 创建的 operator 库。  
- `AsonClientOptions options` —— 定义执行环境，允许覆盖提示词、自定义代理配置以及其他行为。

创建 `AsonClient` 后，你可以使用 `SendStreamingAsync` 或 `SendAsync` 方法发送用户任务：

```csharp
await foreach (var token in asonChatClient.SendStreamingAsync(userText)) {  
    ChatResponse += token;  
}  

// or  

ChatResponse = await asonChatClient.SendAsync(userText);
```
> 深入阅读：[客户端配置](docs/configuration.zh-CN.md) —— 完整的 `AsonClientOptions` 参考、`AddAson` 注册、提示词覆盖和日志记录。

## 执行模式 / 环境

- **进程内（In-process）** – 脚本与你的应用程序在同一进程中运行。  
  非常适合开发和测试，但由于隔离性有限，不推荐用于生产环境。  
  ASON 包含静态分析，用于阻止不安全的操作（反射、文件 I/O、网络等）。

- **外部进程（External process）** – 脚本在单独的进程中运行，提供额外一层保护。  
  添加 `Ason.ExternalExecutor` NuGet 包即可启用此模式。

- **Docker** – 脚本在完全隔离的容器内执行，以获得最高安全性。  
  需要在本地安装 Docker。  
  在运行应用之前拉取所需的容器：

  > docker pull ghcr.io/alexgoon/ason:0.8.1

- **远程服务器（Remote server）** – 脚本在远程服务器上执行，可以是在外部进程中，也可以是在 Docker 容器中。  
  详情请参阅[执行模式与远程执行](docs/execution-modes.zh-CN.md)。
![ASON Execution Environments](images/execution-environments.jpg)

> 深入阅读：[执行模式与远程执行](docs/execution-modes.zh-CN.md)。

## MCP 集成

ASON 脚本可以使用 [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) 调用来自 **MCP server** 的函数。  
要将 MCP 方法暴露给 ASON 脚本，请调用 `OperatorBuilder.AddMcp` 并传入一个 MCP 客户端实例。

示例工作流：

```csharp
async Task<McpClient> CreateContext7ClientAsync() {
    var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Add("CONTEXT7_API_KEY", Environment.GetEnvironmentVariable("CONTEXT7_API_KEY"));

    return await McpClient.CreateAsync(
        new HttpClientTransport(new HttpClientTransportOptions {
            Endpoint = new Uri("https://mcp.context7.com/mcp")
        }, httpClient));
}

var context7Client = await CreateContext7ClientAsync();  
var operatorLibrary = new OperatorBuilder()  
    .AddAssemblies(typeof(MainAppOperator).Assembly)  
    .AddMcp(context7Client)  
    .Build();
```

## 文档

所有指南（服务商接入、operator 编写、客户端配置、执行模式、架构与贡献指南）都集中在 **[docs/index.zh-CN.md](docs/index.zh-CN.md)**，每篇均提供英文、中文与西班牙语版本。关于**桥**（把应用的 operator 通过 gRPC、MCP 或 HTTP/OpenAPI 发布出去，让独立的 Agent 驱动它），见 **[应用 / Agent 分离](docs/app-agent-separation.zh-CN.md)**，两侧各有一个 WPF 示例。

## ASON 相对于 Tool Calling / MCP 的优势

ASON **每个请求只生成一个脚本**，而不是每次 tool call 都进行一次 LLM 往返。复杂的多实体工作流依然可以用普通代码表达，整个结果集不再需要回传给模型，并且在数据量增长时 token 用量保持平稳。

[架构文档](docs/architecture.zh-CN.md#ason-相对于-tool-calling--mcp-的优势)通过示例详细阐述了灵活逻辑、性能和 token 用量方面的论点。

