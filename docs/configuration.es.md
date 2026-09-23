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
| `AnswerLanguage` | `string?` | `null` | Fija el idioma de las respuestas (BCP-47, p. ej. `zh-CN`); ver [Responder en otro idioma](#responder-en-otro-idioma) |
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
| `TransportFactory` | `Func<IRunnerTransport>?` | `null` | Transporte aportado por el host, que sustituye al que construyen el modo y los interruptores de runner remoto. Así apunta un agente su runner a una aplicación tras un [puente](app-agent-separation.es.md) sin perder la generación de proxies, los reintentos ni el manejo del resultado |

Estas tres propiedades tienen `null` como valor predeterminado, lo que significa que ASON aplica el prompt integrado. Esos presets son de lectura pública: `Ason.AgentPrompts` expone `ScriptAgentTemplate`, `ReceptionAgentTemplate`, `ExplainerAgentTemplate` y `TextToDataAgentTemplate`, y `AgentPrompts.BuildScriptInstructions(apiSignatures)` rellena el prompt del script con la API de operadores generada.

### Leer un preset y añadir tus propias reglas

Los presets son cadenas normales, así que una aplicación puede leer un preset **antes** de construir `AsonClientOptions`, concatenar sus propias reglas y asignar el resultado. Así consigue el ejemplo de WPF que un Windows no inglés responda en el idioma del usuario:

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

| Propiedad | Valor por defecto cuando es `null` | Como se usa el valor asignado |
| --- | --- | --- |
| `ReceptionInstructions` | `AgentPrompts.ReceptionAgentTemplate` | Texto plano que se envía como instrucciones del agente. Se puede prefijar o ampliar libremente. |
| `ExplainerInstructions` | `AgentPrompts.ExplainerAgentTemplate` | Texto plano que se envía como instrucciones del agente. Se puede prefijar o ampliar libremente. |
| `ScriptInstructions` | el preset del script con la API de operadores en `{0}` | Se envía **tal cual**: no se formatea, así que `{0}` quedaría literal. Constrúyelo con `AgentPrompts.BuildScriptInstructions(api)` y ten en cuenta que sustituye las declaraciones de instancias de operadores que ASON anexa internamente al bloque de API del preset. |

Dos notas prácticas:

- La regla debe llegar al agente que escribe el texto visible: `Reception` para respuestas directas y `Explainer` para resultados. Prefijar solo `ScriptInstructions` no cambia el idioma de la respuesta, porque el Script Agent solo emite C# (sus frases de fallo empiezan a propósito con la palabra literal `Cannot`, que la lógica de reintentos compara como cadena).
- El historial de la conversación puede devolver al modelo al idioma de turnos anteriores. Indícalo de forma explícita, como en el fragmento anterior ("even when earlier answers in this conversation were written in another one").

### Responder en otro idioma

Si el único objetivo es "responder en el idioma que lee el usuario", el host no necesita tocar los prompts: basta con `AnswerLanguage` y ASON antepone la regla:

<!-- i18n: localize-labels - el código es idéntico, solo se traduce el comentario -->
```csharp
AsonClientOptions options = new() {
    // p. ej. "zh-CN"; en un sistema en inglés déjalo en null para usar los presets tal cual.
    AnswerLanguage = CultureInfo.CurrentUICulture.Name,
};
```

| Pregunta | Comportamiento |
| --- | --- |
| ¿A qué prompts se aplica la regla? | A las instrucciones de Reception y Explainer: son los dos agentes que escriben lo que lee el usuario |
| ¿Por qué no al Script agent? | Solo emite C# (el script nunca se muestra) y su frase de imposibilidad debe empezar por la palabra literal `Cannot`, que la lógica de reintentos compara como cadena |
| Combinado con un prompt propio | La regla se antepone aunque se sobrescriban `ReceptionInstructions` o `ExplainerInstructions`: el idioma de respuesta es un comportamiento del host, no parte de un preset |
| Relación con el historial | La regla indica al modelo que no copie el idioma de turnos anteriores, porque el historial lo devuelve al idioma previo |
| Nombre mal formado (`en/US`) | El constructor de `AsonClient` lanza una excepción, así que el error aparece al arrancar |
| Etiqueta no registrada (`zz`) | No se puede detectar: el runtime acepta cualquier etiqueta BCP-47 bien formada y la pasa tal cual, de modo que llega al modelo sin cambios |
| Sin configurar (por defecto) | Nada cambia: con `null` cada prompt queda exactamente igual |

`AgentPrompts.BuildLanguageDirective(language)` construye esa regla y `AgentPrompts.WithAnswerLanguage(preset, language)` la aplica a cualquier prompt, para los hosts que prefieren componer los prompts ellos mismos (por ejemplo para añadir sus propias reglas a la vez).

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
        opt.ExecutionMode = ExecutionMode.Docker;  
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

