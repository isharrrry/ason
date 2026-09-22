# ASON (Agent Script Operation)
![ASON Logo](images/ason_logo.jpeg)

[English](README.md) | [中文](README.zh-CN.md) | **Español**

**ASON** es una biblioteca que permite a los modelos de IA generar scripts y ejecutar secuencias de funciones en tu aplicación a partir de solicitudes de los usuarios en lenguaje natural. A diferencia de los mecanismos tradicionales de **MCP** o **tool calling**, ASON genera y ejecuta scripts completos en una sola pasada.

Esta arquitectura permite manejar edición de datos en varios pasos, solicitudes analíticas (por ejemplo, crear gráficos dinámicos a partir de tus datos) o extraer información de grandes fuentes de datos. No se requiere infraestructura compleja: ASON ofrece un punto de entrada simple para una integración potente de IA.

ASON ofrece mayor flexibilidad, rendimiento y eficiencia al incorporar IA en tus aplicaciones. Para obtener más detalles, consulta [Ventajas de ASON frente a tool calling / MCP](#ventajas-de-ason-frente-a-tool-calling--mcp).

> [!Note]
> ASON es una biblioteca nueva y su API pública puede cambiar en futuras versiones. Si quieres que ASON siga creciendo, ¡muestra tu apoyo dándole una estrella!

**Requisitos:** la biblioteca `Ason` está dirigida a **.NET 6.0** y **.NET 9.0**. El paquete de solo marcadores `Ason.Abstractions` está dirigido a **netstandard2.0**, por lo que sus atributos se pueden referenciar desde bibliotecas de clases .NET Framework 4.6.1+, .NET Core y .NET 6+.

## Demo en línea

Puedes probar ASON en una aplicación de ejemplo en vivo aquí (actualmente alojada en un plan gratuito de Azure, por lo que pueden aplicarse límites de uso):  [**ASON Demo**](https://ason-demo-linux-hegxhud6c8cmfkfm.centralus-01.azurewebsites.net/)

El **código fuente** de la demo está disponible en GitHub: [ason-demo](https://github.com/Alexgoon/ason-demo)

**Video de introducción:** [ASON – Actionable AI in .NET Apps (Intro & Demo)](https://youtu.be/dKBkVTl3e_c?si=N0zoIE1EAnC6KoS1)  [![YouTube](https://img.shields.io/badge/Watch_on_YouTube-red?logo=youtube&logoColor=white)](https://youtu.be/dKBkVTl3e_c?si=N0zoIE1EAnC6KoS1)


## Inicio rápido

**1. Instala las plantillas de proyecto de ASON**

```
> dotnet new install Ason.ProjectTemplates
```

**2. Crea un proyecto**
Puedes crear una aplicación Blazor, WPF, WinForms, MAUI o de consola:

```
> dotnet new ason.blaz.srv -n MyAsonProject
```

En lugar de `ason.blaz.srv`, puedes usar `ason.wpf`, `ason.winforms`, `ason.maui` o `ason.console`.

**3. Configura tu proveedor de IA**

Cualquier endpoint compatible con OpenAI funciona. Las plantillas y los ejemplos leen tres variables de entorno:

| Variable | Propósito | Ejemplo |
|---|---|---|
| `MY_OPEN_AI_KEY` | Clave de API (obligatoria) | `sk-...` |
| `MY_OPEN_AI_BASE_URL` | Anulación del endpoint; cuando no se define, se usa el valor predeterminado del conector (`api.openai.com`) | `https://api.deepseek.com` |
| `MY_OPEN_AI_MODEL` | Id del modelo; el valor predeterminado es `gpt-4.1-mini` | `deepseek-flash` |

Uso de **DeepSeek** (API compatible con OpenAI):

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

Las plantillas crean el servicio mediante el helper que se incluye con `Ason`:

```csharp
IChatCompletionService chatService = OpenAiCompatibleChatServiceFactory.FromEnvironment();
```

Consulta **[Proveedores de IA](docs/ai-providers.es.md)** para conocer otros endpoints, nombres de modelos y solución de problemas.

---

## Cómo funciona ASON

A continuación se muestra una descripción general simplificada de la arquitectura de ASON.

![ASON Flow Overview](images/flow-overview.jpg)
1. Tu aplicación define **operadores** que ejecutan métodos en tu código.  
2. El **ASON Client** crea API (firmas de operadores) y las pasa al **script agent** junto con la tarea del usuario.  
3. El **script agent** genera un script y lo envía al **entorno de ejecución** (mismo proceso, proceso externo, contenedor Docker o servidor remoto).  
4. El **entorno de ejecución** llama a proxies generados dinámicamente que invocan métodos reales de los operadores.

> Análisis detallado: [arquitectura, agentes y la comparación con tool calling / MCP](docs/architecture.es.md).

## Operadores

Un **operador** es una clase que contiene métodos expuestos al script agent.  
Para definir un operador, crea una clase que herede de `OperatorBase`.  
Aplica el atributo `[AsonOperator]` a la clase y `[AsonMethod]` a cada método que quieras exponer.

Ejemplo:

```csharp
[AsonOperator]  
public class OrdersViewOperator : OperatorBase<OrdersViewModel> {  
    [AsonMethod]  
    public void DeleteOrder(int orderId) => AttachedObject?.DeleteOrder(orderId);  
}
```

Los operadores se asocian a objetos que contienen la lógica de negocio.  Estos objetos se almacenan en la propiedad `AttachedObject`.

Para asociar un operador a un objeto, llama a `AttachChildOperator`:

```csharp
public partial class OrdersViewModel {  
    public void DeleteOrder(int orderId) => Debug.WriteLine($"Deleted order {orderId}");  
    public OrdersViewModel(RootOperator rootOperator) {  
        rootOperator.AttachChildOperator<OrdersViewOperator>(this);  
    }  
}
```

Los operadores sin estado no necesitan ciclo de vida de vista: una `static class` se convierte en un **módulo** de operador, y una clase de operador con un constructor público sin parámetros se registra automáticamente como singleton.

```csharp
[AsonOperator]
public static class LibDemoStaticOperator {
    [AsonMethod]
    public static int Add(int left, int right) => left + right;   // scripts call LibDemoStaticOperator.Add(2, 4)
}
```

> Análisis detallado: [cómo escribir operadores](docs/operators.es.md) — operador raíz, ciclo de vida de vista, módulos sin estado, modelos y el paquete `Ason.Abstractions`.

## Modelo de dominio

Expón modelos al script agent aplicando `[AsonModel]`; la serialización entre el cliente y el entorno de ejecución se maneja por ti. Ejemplo completo: [modelos de dominio](docs/operators.es.md#modelo-de-dominio).

## Configurar un cliente y enviar una tarea

Para enviar tareas a ASON, crea una instancia de `AsonClient`:

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

- `IChatCompletionService defaultChatCompletion` — Un servicio de chat completion que usan los agentes de ASON. Admite OpenAI, Azure, Gemini, Ollama, Anthropic, **cualquier endpoint compatible con OpenAI, como DeepSeek** (consulta el paso 3 del inicio rápido anterior) y otros proveedores compatibles con Semantic Kernel (consulta: [Chat Completion | Overview](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp-Google%2Cpython-AzureOpenAI%2Cjava-AzureOpenAI&pivots=programming-language-csharp)).  
- `RootOperator rootOperator` — Una instancia de tu operador raíz de la aplicación.  
- `OperatorsLibrary operators` — Una biblioteca de operadores creada por `OperatorBuilder`.  
- `AsonClientOptions options` — Define el entorno de ejecución, permite anular prompts, configuraciones personalizadas de agentes y otros comportamientos.

Una vez creado un `AsonClient`, puedes enviar una tarea del usuario con el método `SendStreamingAsync` o `SendAsync`:

```csharp
await foreach (var token in asonChatClient.SendStreamingAsync(userText)) {  
    ChatResponse += token;  
}  

// or  

ChatResponse = await asonChatClient.SendAsync(userText);
```
> Análisis detallado: [configuración del cliente](docs/configuration.es.md) — la referencia completa de `AsonClientOptions`, el registro de `AddAson`, las anulaciones de prompts y el registro de logs.

## Modos de ejecución / entornos

- **En proceso** – Los scripts se ejecutan en el mismo proceso que tu aplicación.  
  Ideal para desarrollo y pruebas, pero no se recomienda para producción debido al aislamiento limitado.  
  ASON incluye análisis estático para bloquear operaciones no seguras (reflexión, E/S de archivos, redes, etc.).

- **Proceso externo** – Los scripts se ejecutan en un proceso separado, lo que proporciona una capa adicional de protección.  
  Agrega el paquete NuGet `Ason.ExternalExecutor` para habilitar este modo.

- **Docker** – Los scripts se ejecutan dentro de un contenedor totalmente aislado para máxima seguridad.  
  Requiere Docker instalado localmente.  
  Descarga el contenedor requerido antes de ejecutar la aplicación:

  > docker pull ghcr.io/alexgoon/ason:0.8.1

- **Servidor remoto** – Los scripts se ejecutan en un servidor remoto, ya sea en un proceso externo o en un contenedor Docker.  
  Consulta [modos de ejecución y ejecución remota](docs/execution-modes.es.md) para más detalles.
![ASON Execution Environments](images/execution-environments.jpg)

> Análisis detallado: [modos de ejecución y ejecución remota](docs/execution-modes.es.md).

## Integración con MCP

Los scripts de ASON pueden invocar funciones de **servidores MCP** usando el [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).  
Para exponer métodos MCP a los scripts de ASON, llama a `OperatorBuilder.AddMcp` y pasa una instancia de cliente MCP.

Flujo de trabajo de ejemplo:

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

## Documentación

Todas las guías (proveedores, operadores, configuración, modos de ejecución, arquitectura y contribución) están reunidas en **[docs/index.es.md](docs/index.es.md)**, cada una disponible en inglés, chino y español.

## Ventajas de ASON frente a tool calling / MCP

ASON genera **un script por solicitud** en lugar de una ida y vuelta con el LLM por cada llamada de herramienta. Los flujos de trabajo complejos con múltiples entidades siguen siendo expresables en código simple, los conjuntos de resultados completos ya no tienen que viajar de vuelta al modelo y el uso de tokens se mantiene estable a medida que crece el volumen de datos.

El [documento de arquitectura](docs/architecture.es.md#ventajas-de-ason-frente-a-tool-calling--mcp) desarrolla los argumentos de lógica flexible, rendimiento y uso de tokens con ejemplos.

