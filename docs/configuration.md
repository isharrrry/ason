# Client configuration and registration

[English](configuration.md) | [中文](configuration.zh-CN.md) | [Español](configuration.es.md)

> Part of the **ASON** documentation — back to the [README](../README.md).

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

- `IChatCompletionService defaultChatCompletion` — A chat completion service used by ASON agents. Supports OpenAI, Azure, Gemini, Ollama, Anthropic, and other providers supported by Semantic Kernel (see: [Chat Completion | Overview](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp-Google%2Cpython-AzureOpenAI%2Cjava-AzureOpenAI&pivots=programming-language-csharp)).  
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

## AsonClientOptions reference

| Property | Type | Default | Purpose |
|---|---|---|---|
| `Logger` | `ILogger?` | `null` | Logging sink for ASON's own diagnostics |
| `MaxFixAttempts` | `int` | `2` | How many times a failing script is sent back for repair |
| `ScriptInstructions` | `string?` | `null` | Overrides the Script Agent prompt |
| `ReceptionInstructions` | `string?` | `null` | Overrides the Reception Agent prompt |
| `ExplainerInstructions` | `string?` | `null` | Overrides the Explainer Agent prompt |
| `ScriptChatCompletion` | `IChatCompletionService?` | `null` | Dedicated model for the Script Agent |
| `ReceptionChatCompletion` | `IChatCompletionService?` | `null` | Dedicated model for the Reception Agent |
| `ExplainerChatCompletion` | `IChatCompletionService?` | `null` | Dedicated model for the Explainer Agent |
| `SkipReceptionAgent` | `bool` | `false` | Sends the user message straight to the Script Agent |
| `SkipExplainerAgent` | `bool` | `false` | Returns the raw script result without a natural-language explanation |
| `ExecutionMode` | `ExecutionMode` | `InProcess` | `InProcess`, `ExternalProcess` or `Docker` — see [execution modes](execution-modes.md) |
| `AllowTextExtractor` | `bool` | `true` | Exposes the built-in extraction operator to scripts |
| `ForbiddenScriptKeywords` | `string[]` | built-in deny list | Static-analysis deny list (`System.IO`, `Process.Start`, `System.Reflection`, ...) |
| `UseRemoteRunner` | `bool` | `false` | Runs scripts through a remote runner; requires `RemoteRunnerBaseUrl` |
| `RemoteRunnerBaseUrl` | `string?` | `null` | URL of the remote script runner |
| `RemoteRunnerDockerImage` | `string?` | `ghcr.io/alexgoon/ason:<version>` | Image used when the remote runner runs in Docker |
| `StopLocalRunnerWhenEnablingRemote` | `bool` | `true` | Stops the local runner once the remote one is active |
| `AdditionalMethodFilter` | `Func<MethodInfo, bool>?` | `null` | Extra filter applied on top of the generated operator snapshot |
| `RunnerExecutablePath` | `string?` | `null` | Explicit path to `Ason.ExternalExecutor` (dll or exe) |

These three instruction properties default to `null`, which means ASON applies the built-in prompt. Those presets are publicly readable: `Ason.AgentPrompts` exposes `ScriptAgentTemplate`, `ReceptionAgentTemplate`, `ExplainerAgentTemplate` and `TextToDataAgentTemplate`, and `AgentPrompts.BuildScriptInstructions(apiSignatures)` fills the generated operator API into the script prompt.

## Service registration (ASP.NET Core / Blazor)

You can register `AsonClient` as a scoped dependency in your service container using `AddAson`:

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


## Logging

ASON provides centralized logging via the `AsonClient.Log` event, which reports activity from all execution levels.

```csharp
asonChatClient.Log += (o, e) => Debug.WriteLine($"{e.Source}: {e.Message}");
```

This helps track generated scripts, capture errors, and monitor runtime events from both client and execution environments.

`AsonLogEventArgs` includes the following properties:

- `Level`  
- `Message`  
- `Exception`  
- `Source`

