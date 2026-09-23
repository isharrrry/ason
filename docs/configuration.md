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
| `AnswerLanguage` | `string?` | `null` | Pins the language of the answers (BCP-47, e.g. `zh-CN`); see [Answering in another language](#answering-in-another-language) |
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

### Reading a preset and adding your own rules

The presets are ordinary strings, so an application can read a preset **before** it builds `AsonClientOptions`, concatenate its own rules and assign the result. This is how the WPF sample makes a non-English Windows answer in the user's own language:

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

| Property | Default when `null` | How an assigned value is used |
| --- | --- | --- |
| `ReceptionInstructions` | `AgentPrompts.ReceptionAgentTemplate` | Plain text, sent as the agent instructions. Prefix or append freely. |
| `ExplainerInstructions` | `AgentPrompts.ExplainerAgentTemplate` | Plain text, sent as the agent instructions. Prefix or append freely. |
| `ScriptInstructions` | the script preset with the operator API formats into `{0}` | Sent **verbatim** - it is not formatted, so `{0}` would stay literal. Build it with `AgentPrompts.BuildScriptInstructions(api)`, and note that this replaces the operator instance declarations ASON appends to the preset API block internally. |

Two practical notes:

- The rule has to reach the agent that writes the visible text: `Reception` for direct answers and `Explainer` for results. Prefixing `ScriptInstructions` alone does not change the language of an answer, because the Script Agent only emits C# (its failure sentences deliberately start with the literal word `Cannot`, which the retry logic string-matches).
- The conversation history can pull a model back into the language of earlier turns. State it explicitly, as in the snippet above ("even when earlier answers in this conversation were written in another one").

### Answering in another language

If the goal is only "answer in the language the user reads", the host does not have to touch the prompts at all. Set `AnswerLanguage` and ASON prepends the rule for you:

<!-- i18n: localize-labels - the code stays identical, only the inline comment is translated -->
```csharp
AsonClientOptions options = new() {
    // e.g. "zh-CN"; keep it null on an English system so the presets are used unchanged.
    AnswerLanguage = CultureInfo.CurrentUICulture.Name,
};
```

| Question | Behaviour |
| --- | --- |
| Which prompts get the rule? | The Reception and Explainer instructions - the two agents that write what the user reads |
| Why not the Script agent? | It only emits C# (the script itself is never shown), and its impossibility sentence must keep starting with the literal word `Cannot`, which the retry logic string-matches |
| Combined with a custom prompt | The rule is prepended even when `ReceptionInstructions` or `ExplainerInstructions` are overridden: the answer language is a host-level behaviour, not part of a preset |
| Precedence with history | The rule tells the model not to copy the language of earlier turns, because conversation history otherwise pulls it back to the previous language |
| Malformed name (`en/US`) | The `AsonClient` constructor throws, so a broken value surfaces at startup |
| Unregistered tag (`zz`) | Cannot be detected: the runtime accepts any well-formed BCP-47 tag and passes it through, so it reaches the model as-is |
| Not set (default) | Nothing changes - `null` leaves every prompt exactly as it is |

`AgentPrompts.BuildLanguageDirective(language)` builds that rule and `AgentPrompts.WithAnswerLanguage(preset, language)` applies it to any prompt, for hosts that want to compose the prompts themselves (for example to add their own rules at the same time).

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

