# Configuración y registro del cliente

[English](configuration.md) | [中文](configuration.zh-CN.md) | **Español**

> Parte de la documentación de **ASON** — volver al [README](../README.es.md).

## Configurar un cliente y enviar una tarea

Para enviar tareas a ASON, cree una instancia de `AsonClient`:

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

**Parámetros del constructor:**

- `IChatCompletionService defaultChatCompletion` — Un servicio de chat completion que usan los agentes de ASON. Admite OpenAI, Azure, Gemini, Ollama, Anthropic y otros proveedores compatibles con Semantic Kernel (consulte: [Chat Completion | Overview](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp-Google%2Cpython-AzureOpenAI%2Cjava-AzureOpenAI&pivots=programming-language-csharp)).  
- `RootOperator rootOperator` — Una instancia del root operator de su aplicación.  
- `OperatorsLibrary operators` — Una biblioteca de operators creada por `OperatorBuilder`.  
- `AsonClientOptions options` — Define el entorno de ejecución y permite sobrescribir prompts, configurar agentes personalizados y ajustar otros comportamientos.

Una vez creado un `AsonClient`, puede enviar una tarea de usuario con el método `SendStreamingAsync` o `SendAsync`:

```csharp
await foreach (var token in asonChatClient.SendStreamingAsync(userText)) {  
    ChatResponse += token;  
}  

// or  

ChatResponse = await asonChatClient.SendAsync(userText);
```

## Referencia de AsonClientOptions

| Propiedad | Tipo | Valor predeterminado | Propósito |
|---|---|---|---|
| `Logger` | `ILogger?` | `null` | Receptor de logs para los diagnósticos propios de ASON |
| `MaxFixAttempts` | `int` | `2` | Cuántas veces se devuelve un script que falla para repararlo |
| `ScriptInstructions` | `string?` | `null` | Sobrescribe el prompt del script agent |
| `ReceptionInstructions` | `string?` | `null` | Sobrescribe el prompt del reception agent |
| `ExplainerInstructions` | `string?` | `null` | Sobrescribe el prompt del explainer agent |
| `ScriptChatCompletion` | `IChatCompletionService?` | `null` | Modelo dedicado para el script agent |
| `ReceptionChatCompletion` | `IChatCompletionService?` | `null` | Modelo dedicado para el reception agent |
| `ExplainerChatCompletion` | `IChatCompletionService?` | `null` | Modelo dedicado para el explainer agent |
| `SkipReceptionAgent` | `bool` | `false` | Envía el mensaje del usuario directamente al script agent |
| `SkipExplainerAgent` | `bool` | `false` | Devuelve el resultado sin procesar del script, sin una explicación en lenguaje natural |
| `ExecutionMode` | `ExecutionMode` | `InProcess` | `InProcess`, `ExternalProcess` o `Docker` — consulte [modos de ejecución](execution-modes.es.md) |
| `AllowTextExtractor` | `bool` | `true` | Expone el operator de extracción integrado a los scripts |
| `ForbiddenScriptKeywords` | `string[]` | lista de denegación integrada | Lista de denegación del análisis estático (`System.IO`, `Process.Start`, `System.Reflection`, ...) |
| `UseRemoteRunner` | `bool` | `false` | Ejecuta los scripts a través de un ejecutor remoto; requiere `RemoteRunnerBaseUrl` |
| `RemoteRunnerBaseUrl` | `string?` | `null` | URL del ejecutor de scripts remoto |
| `RemoteRunnerDockerImage` | `string?` | `ghcr.io/alexgoon/ason:<version>` | Imagen que se usa cuando el ejecutor remoto se ejecuta en Docker |
| `StopLocalRunnerWhenEnablingRemote` | `bool` | `true` | Detiene el ejecutor local una vez que el remoto está activo |
| `AdditionalMethodFilter` | `Func<MethodInfo, bool>?` | `null` | Filtro adicional que se aplica sobre la instantánea de operators generada |
| `RunnerExecutablePath` | `string?` | `null` | Ruta explícita a `Ason.ExternalExecutor` (dll o exe) |

## Registro de servicios (ASP.NET Core / Blazor)

Puede registrar `AsonClient` como una dependencia scoped en su contenedor de servicios usando `AddAson`:

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


## Registro de logs

ASON proporciona un registro centralizado mediante el evento `AsonClient.Log`, que informa la actividad de todos los niveles de ejecución.

```csharp
asonChatClient.Log += (o, e) => Debug.WriteLine($"{e.Source}: {e.Message}");
```

Esto ayuda a rastrear los scripts generados, capturar errores y supervisar los eventos de tiempo de ejecución tanto del cliente como de los entornos de ejecución.

`AsonLogEventArgs` incluye las siguientes propiedades:

- `Level`  
- `Message`  
- `Exception`  
- `Source`

