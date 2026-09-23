# ASON (Agent Script Operation)
![ASON Logo](images/ason_logo.jpeg)

**English** | [中文](README.zh-CN.md) | [Español](README.es.md)

**ASON** is a library that enables AI models to generate scripts and execute sequences of functions in your application based on natural language user requests. Unlike traditional **MCP** or **tool-calling** mechanisms, ASON generates and runs complete scripts in a single pass.

This architecture makes it possible to handle multi-step data editing, analytical requests (for example, building dynamic charts from your data), or extracting information from large data sources. No complex infrastructure is required — ASON provides a simple entry point for powerful AI integration.

ASON offers greater flexibility, performance, and efficiency when bringing AI into your applications. For more details, see [Benefits of ASON over Tool Calling / MCP](#benefits-of-ason-over-tool-calling--mcp).

> [!Note]
> ASON is a new library, and its public API may change in future versions. If you’d like to see ASON continue to grow, please show your support by **giving it a star!**

**Requirements:** the `Ason` library targets **.NET 6.0** and **.NET 9.0**. The marker-only package `Ason.Abstractions` targets **netstandard2.0**, so its attributes can be referenced from .NET Framework 4.6.1+, .NET Core and .NET 6+ class libraries.

## Online Demo

You can try out ASON in a live sample app here (currently hosted on a free Azure plan, so usage limits may apply):  [**ASON Demo**](https://ason-demo-linux-hegxhud6c8cmfkfm.centralus-01.azurewebsites.net/)

The **source code** for the demo is available on GitHub: [ason-demo](https://github.com/Alexgoon/ason-demo)

**Intro Video:** [ASON – Actionable AI in .NET Apps (Intro & Demo)](https://youtu.be/dKBkVTl3e_c?si=N0zoIE1EAnC6KoS1)  [![YouTube](https://img.shields.io/badge/Watch_on_YouTube-red?logo=youtube&logoColor=white)](https://youtu.be/dKBkVTl3e_c?si=N0zoIE1EAnC6KoS1)


## Quick start

**1. Install ASON project templates**

```
> dotnet new install Ason.ProjectTemplates
```

**2. Create a project**
You can create a Blazor, WPF, WinForms, MAUI, or Console application:

```
> dotnet new ason.blaz.srv -n MyAsonProject
```

Instead of `ason.blaz.srv`, you can use `ason.wpf`, `ason.winforms`, `ason.maui`, or `ason.console`.

**3. Configure your AI provider**

Any OpenAI-compatible endpoint works. The templates and samples read three environment variables:

| Variable | Purpose | Example |
|---|---|---|
| `MY_OPEN_AI_KEY` | API key (required) | `sk-...` |
| `MY_OPEN_AI_BASE_URL` | Endpoint override; when unset the connector default (`api.openai.com`) is used | `https://api.deepseek.com` |
| `MY_OPEN_AI_MODEL` | Model id; defaults to `gpt-4.1-mini` | `deepseek-flash` |

Using **DeepSeek** (OpenAI-compatible API):

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

The templates create the service through the helper that ships with `Ason`:

```csharp
IChatCompletionService chatService = OpenAiCompatibleChatServiceFactory.FromEnvironment();
```

See **[AI providers](docs/ai-providers.md)** for other endpoints, model names and troubleshooting.

---

## How ASON works

Below is a simplified overview of the ASON architecture.

![ASON Flow Overview](images/flow-overview.jpg)
1. Your application defines **operators** that execute methods in your code.  
2. The **ASON Client** creates APIs (operator signatures) and passes them to the **Script Agent** along with the user’s task.  
3. The **Script Agent** generates a script and sends it to the **execution environment** (same process, external process, Docker container, or remote server).  
4. The **execution environment** calls dynamically generated proxies that invoke real operator methods.

> Deep dive: [architecture, agents and the comparison with tool calling / MCP](docs/architecture.md).

## Operators

An **operator** is a class that contains methods exposed to the Script Agent.  
To define an operator, create a class that inherits from `OperatorBase`.  
Apply the `[AsonOperator]` attribute to the class and `[AsonMethod]` to each method you want to expose.

Example:

```csharp
[AsonOperator]  
public class OrdersViewOperator : OperatorBase<OrdersViewModel> {  
    [AsonMethod]  
    public void DeleteOrder(int orderId) => AttachedObject?.DeleteOrder(orderId);  
}
```

Operators are attached to objects that contain business logic.  These objects are stored in the `AttachedObject` property.

To attach an operator to an object, call `AttachChildOperator`:

```csharp
public partial class OrdersViewModel {  
    public void DeleteOrder(int orderId) => Debug.WriteLine($"Deleted order {orderId}");  
    public OrdersViewModel(RootOperator rootOperator) {  
        rootOperator.AttachChildOperator<OrdersViewOperator>(this);  
    }  
}
```

Stateless operators need no view lifecycle: a `static class` becomes an operator **module**, and an operator class with a public parameterless constructor is registered automatically as a singleton.

```csharp
[AsonOperator]
public static class LibDemoStaticOperator {
    [AsonMethod]
    public static int Add(int left, int right) => left + right;   // scripts call LibDemoStaticOperator.Add(2, 4)
}
```

> Deep dive: [writing operators](docs/operators.md) — root operator, view lifecycle, stateless modules, models and the `Ason.Abstractions` package.

## Domain model

Expose models to the Script Agent by applying `[AsonModel]`; serialization between the client and the execution environment is handled for you. Full example: [domain models](docs/operators.md#domain-model).

## Configure a client and send a task

To send tasks to ASON, create an instance of `AsonClient`:

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

**Constructor parameters:**

- `IChatCompletionService defaultChatCompletion` — A chat completion service used by ASON agents. Supports OpenAI, Azure, Gemini, Ollama, Anthropic, **any OpenAI-compatible endpoint such as DeepSeek** (see step 3 of the Quick start above), and other providers supported by Semantic Kernel (see: [Chat Completion | Overview](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp-Google%2Cpython-AzureOpenAI%2Cjava-AzureOpenAI&pivots=programming-language-csharp)).  
- `RootOperator rootOperator` — An instance of your root application operator.  
- `OperatorsLibrary operators` — A library of operators created by `OperatorBuilder`.  
- `AsonClientOptions options` — Defines the execution environment, allows prompt overrides, custom agent configurations, and other behaviors.

Once an `AsonClient` is created, you can send a user task using the `SendStreamingAsync` or `SendAsync` method:

```csharp
await foreach (var token in asonChatClient.SendStreamingAsync(userText)) {  
    ChatResponse += token;  
}  

// or  

ChatResponse = await asonChatClient.SendAsync(userText);
```
> Deep dive: [client configuration](docs/configuration.md) — the full `AsonClientOptions` reference, `AddAson` registration, prompt overrides and logging.

## Execution modes / environments

- **In-process** – Scripts run in the same process as your application.  
  Ideal for development and testing, but not recommended for production due to limited isolation.  
  ASON includes static analysis to block unsafe operations (reflection, file I/O, networking, etc.).

- **External process** – Scripts run in a separate process, providing an extra layer of protection.  
  Add the `Ason.ExternalExecutor` NuGet package to enable this mode.

- **Docker** – Scripts execute inside a fully isolated container for maximum security.  
  Requires Docker installed locally.  
  Pull the required container before running the app:

  > docker pull ghcr.io/alexgoon/ason:0.8.1

- **Remote server** – Scripts execute on a remote server, either in an external process or Docker container.  
  See [execution modes and remote execution](docs/execution-modes.md) for the details.
![ASON Execution Environments](images/execution-environments.jpg)

> Deep dive: [execution modes and remote execution](docs/execution-modes.md).

## MCP integration

ASON scripts can invoke functions from **MCP servers** using the [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).  
To expose MCP methods to ASON scripts, call `OperatorBuilder.AddMcp` and pass an MCP client instance.

Example workflow:

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

## Documentation

All guides — providers, operators, configuration, execution modes, architecture and contributing — are collected in **[docs/index.md](docs/index.md)**, each available in English, Chinese and Spanish. The **bridge** — publishing an application's operators over gRPC, MCP or HTTP/OpenAPI so a separate agent can drive them — is documented in **[application / agent separation](docs/app-agent-separation.md)**, with a WPF sample for each side.

## Benefits of ASON over Tool Calling / MCP

ASON generates **one script per request** instead of one LLM round trip per tool call. Complex multi-entity workflows stay expressible in plain code, whole result sets no longer have to travel back to the model, and token usage stays flat while data volume grows.

The [architecture document](docs/architecture.md#benefits-of-ason-over-tool-calling--mcp) works through the flexible-logic, performance and token-usage arguments with examples.

