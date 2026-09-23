# Separación aplicación / agente con el puente de ASON

[English](app-agent-separation.md) | [中文](app-agent-separation.zh-CN.md) | **Español**

> Parte de la documentación de **ASON** — volver al [README](../README.es.md).

ASON asumía originalmente que un solo proceso hacía todo: la aplicación, sus operadores, las llamadas al
modelo y la orquestación de scripts. El **puente** (bridge) lo divide en dos manteniendo idéntico el runtime
en ambos lados.

- La **aplicación** es el único lugar que contiene operadores marcados con `[Ason*]`, datos de negocio y
  estado de UI. No incluye ningún cliente de modelo.
- El **agente** contiene el modelo y la orquestación. No posee operadores: pregunta a la aplicación qué puede
  llamar y le pide que ejecute cosas.
- El **puente** es la costura entre ambos: describe la API de operadores, reenvía la ejecución y es el único
  componente que conoce un transporte (gRPC, MCP, ...). Las cuatro formas de despliegue — un agente .NET, MCP por
  HTTP, el relé por stdio y un cliente HTTP genérico — están dibujadas como diagramas de flujo en
  [arquitectura](architecture.es.md), en la sección *Separación aplicación / agente (el puente)*.

## El manifiesto es el contrato

`AsonBridgeRuntime.GetManifestAsync()` devuelve una única carga que todos los adaptadores publican sin cambios:

| Campo | Significado |
|---|---|
| `protocolVersion` | Revisión del contrato; fallar rápido si no coincide |
| `appName` | Qué aplicación respondió |
| `execution` | Dónde se evalúan los scripts: `in-process`, `external-process`, `docker`, `remote-runner` |
| `capabilities` | Qué interfaces están habilitadas |
| `api` | La API de operadores, métodos, parámetros y tipos `[AsonModel]` |
| `markdown` | La misma lista como documento |
| `proxies` / `signatures` | La capa de proxies generada que necesita un script |
| `instances` | Las instancias vivas y el handle que direcciona cada una |

La lista proviene de `OperatorApiCatalog`, que aplica las mismas reglas de reflexión con las que se construye
el prompt del script: una lista no puede divergir de lo que los scripts pueden llamar realmente.

## Dos interfaces de ejecución independientes

| Capacidad | Interfaz | Cuándo usarla |
|---|---|---|
| `listApis` | manifiesto | El agente necesita saber qué puede llamar |
| `executeScript` | `ExecuteScriptAsync(script)` | El agente compone varias llamadas, ramas o bucles |
| `invokeFunction` | `InvokeFunctionAsync(operator, method, args)` | Una llamada precisa, sin generar texto de script |
| `invokeMcpTool` | paso directo a los clientes MCP de la aplicación | La aplicación consume otros servidores MCP |
| `logStream` | `StreamExecutionAsync(script)` | El llamador quiere seguir la ejecución |

Son interruptores independientes y se combinan entre sí. Un módulo de operador estático no necesita handle; un
operador de instancia se resuelve por el directorio de instancias vivas cuando solo existe una, y en caso
contrario requiere el `handle` del manifiesto. Los fallos llegan como códigos estables (`operator-not-found`,
`handle-required`, `handle-ambiguous`, `handle-not-found`, `method-not-found`, `script-rejected`,
`not-supported`, `execution-failed`).

## Transportes

| Adaptador | Paquete | Contenido |
|---|---|---|
| gRPC | `Ason.Bridge.Grpc` | `GrpcAsonBridgeService`, `GrpcAsonBridgeClient`, `GrpcAsonBridgeTransport`, `GrpcAsonBridgeEndpoint` |
| MCP (Streamable HTTP) | `Ason.Bridge.Mcp` | `AsonBridgeMcpTools`, `McpAsonBridgeClient`, `McpAsonBridgeTransport`, `McpAsonBridgeEndpoint` |
| MCP (stdio, relé) | `Ason.Bridge.McpHost` | Proceso que republica el puente gRPC como MCP por stdin/stdout |
| HTTP + OpenAPI (Swagger) | `Ason.Bridge.OpenApi` | Endpoints HTTP más un documento generado desde el manifiesto, para clientes HTTP genéricos y Swagger UI |
| Otro | tu propio proyecto | La misma proyección sobre `IAsonBridgeEndpoint` |

Las bibliotecas maduras quedan fuera de `Ason` a propósito: `Ason` no referencia gRPC, ni un servidor MCP, ni
ASP.NET. Añadir un transporte es añadir un proyecto que referencia `Ason.Bridge` y proyecta
`IAsonBridgeEndpoint` sobre él. Nada en la aplicación debe cambiar.

Una capacidad deshabilitada se responde como `StatusCode.Unimplemented` en gRPC y simplemente no se registra
como herramienta en MCP.

## Ubicación de la ejecución

| Valor | Los scripts se evalúan | Las llamadas a operadores se resuelven |
|---|---|---|
| `InProcess` | En el proceso de la aplicación | La aplicación |
| `ExternalProcess` | En un proceso hijo `Ason.ExternalExecutor` | La aplicación (por stdio) |
| `Docker` | En un contenedor iniciado por la aplicación | La aplicación |
| `RemoteRunner` | En un host remoto de `Ason.RemoteBridge` | La aplicación |

En todos los casos los operadores, los datos y las credenciales permanecen en la aplicación. Las llamadas se
serializan a través del `SynchronizationContext` capturado al construir el runtime; en WPF eso es el hilo del
dispatcher.

## Delegar la orquestación al agente

El agente puede conservar la orquestación de ASON sin poseer operadores: construye su biblioteca de operadores
a partir del manifiesto y apunta su runner a la aplicación con
`AsonClientOptions.TransportFactory` (por debajo, `RunnerClient.UseTransport`), por ejemplo
`TransportFactory = () => new GrpcAsonBridgeTransport(client)`. La aplicación resuelve las llamadas a
operadores en su propio proceso, de modo que el transporte nunca ve un mensaje `invoke`; si llegara uno, se
responde con un error en lugar de dejar al llamador esperando.

## Usar el puente sin un agente

Nada del puente es específico de los agentes. Los mismos endpoints son una superficie RPC para programas
corrientes: un arnés de pruebas que conduce la aplicación real, un paso de CI que siembra o verifica estado, un
script de automatización o mantenimiento, otro servicio, o un desarrollador con `curl`. Llaman a la aplicación
como cualquier cliente — normalmente con la interfaz de función única, porque un programa que ya sabe qué llamar
no necesita un modelo que escriba la llamada:

| Llamador | Canal | Llamada típica |
|---|---|---|
| Programa .NET | `GrpcAsonBridgeClient` | `InvokeFunctionAsync(call)` — un solo viaje, JSON de entrada y de salida |
| Programa .NET | `McpAsonBridgeClient` | la misma llamada por MCP, cuando el llamador ya habla MCP |
| Cualquier cliente HTTP | `Ason.Bridge.OpenApi` | `POST /ason/functions/{operator}/{method}`, o `POST /ason/script` |
| Arnés de pruebas / job de CI | cualquiera de ellos | una secuencia de llamadas cuyo JSON devuelto se verifica |

```bash
# aquí no hay ningún modelo en juego
curl -s http://localhost:5223/ason/manifest                       # descubrir qué se puede llamar
curl -s -X POST http://localhost:5223/ason/functions/EmployeesOperator/Rename \
     -H "Content-Type: application/json" -d '{"arguments":[1,"Ada"]}'
curl -s -X POST http://localhost:5223/ason/script \
     -H "Content-Type: application/json" -d '{"code":"return employeesOperator.GetEmployees().Count;"}'

# las mismas llamadas desde un cliente .NET (el ejemplo de consola es exactamente este caso)
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --func EmployeesOperator.GetEmployees
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --script "return employeesOperator.GetDiagnostics().OnUiThread;" --stream
```

Lo que obtiene ese llamador: el manifiesto (descubrimiento), la interfaz de función única, la interfaz de script
completo si quiere componer varias llamadas, y los logs en streaming por gRPC. Lo que no obtiene es el modelo ni
la orquestación: la secuencia de llamadas es suya, nadie explica el resultado en palabras y no hay reintentos
más allá de los suyos. Para la automatización eso es precisamente lo deseable: la llamada determinista es la
ventaja.

Dos consecuencias que conviene conocer:

- **Las llamadas a función omiten por completo el canal de scripts.** `ForbiddenScriptKeywords` protege el
  código generado; una llamada `invokeFunction` no es código. Lo que protege ahí a la aplicación es la API
  expuesta (los operadores marcados y `AdditionalMethodFilter` cuando el host lo restringe), las capacidades
  habilitadas y los controles de red de abajo. Además, la invocación a nivel de función nunca implica un host de
  scripts: sin proceso ejecutor, sin compilación, un solo viaje — a diferencia de un script, que puede costar un
  viaje por cada llamada a operador que haga.
- **Un endpoint del puente es una superficie de administración.** Quien pueda alcanzarlo podrá llamar a todos
  los operadores del manifiesto, así que enlázalo a loopback y pon delante una clave o autenticación real
  cuando se exponga más lejos. El adaptador HTTP tiene una clave compartida opcional; se espera que gRPC y MCP
  queden detrás de una pasarela o de la autorización de ASP.NET.

El escenario con agente y este comparten todo: el mismo host, el mismo runtime, los mismos adaptadores y
endpoints. Lo único que cambia es quién compone las llamadas — un modelo, o el propio programa.

## Seguridad

- Enlazar a loopback por defecto; un puente expuesto en red necesita autenticación delante.
- Definir `ForbiddenScriptKeywords` con la misma lista que se daría a un `AsonClient` local; es un filtro de
  palabras clave, no un sandbox.
- Habilitar solo las capacidades necesarias; el manifiesto dice la verdad sobre cuáles están activas.
- `invokeMcpTool` está desactivado por defecto.

## Ejemplos y cómo ejecutarlos

Qué ejemplo (o combinación de ejemplos) muestra cada forma — desde la disposición en un solo proceso hasta cada
manera de separarla. Las filas con 🔑 necesitan una clave de modelo: `MY_OPEN_AI_KEY` (y opcionalmente
`MY_OPEN_AI_BASE_URL`, `MY_OPEN_AI_MODEL`); `ConsoleMcpSample` además necesita `MY_CONTEXT7_API_KEY`.

| Forma | Lado aplicación | Lado llamador / agente | Qué se ve |
|---|---|---|---|
| **Sin separar** — aplicación de escritorio con el agente dentro | `samples/WptDemoApp` | el mismo proceso | el panel de chat conduce la UI de WPF mediante operadores en proceso |
| Sin separar — Blazor Server | `samples/BlazorAdvancedApp` (http://localhost:5240) | el mismo proceso | el panel de chat conduce componentes del servidor |
| Sin separar — consola con el agente extractor | `samples/ConsoleExtractorSample` | el mismo proceso | extracción de texto y llamadas a operadores en una consola |
| Sin separar — consola cuya API viene de un servidor MCP | `samples/ConsoleMcpSample` | el mismo proceso | el script llama a las herramientas MCP de Context7 como si fueran operadores |
| Sin separar — una aplicación nueva | `samples/templates` | el mismo proceso | `dotnet new ason.wpf` / `ason.winforms` / `ason.console` / `ason.blaz.srv` / `ason.maui` generan una app de chat funcional |
| Sin separar, pero con el **host de scripts** en remoto | `samples/WptDemoApp` + `samples/RemoteRunnerService` (http://localhost:5236) | el mismo proceso | solo se mueve la ejecución; app, agente, operadores y datos siguen juntos |
| **Separado** — agente .NET con su propia orquestación | `samples/WpfAppOnlyDemo` o `samples/ConsoleGrpcBridgeHost` | `samples/WpfAgentDemo`, o cualquier `AsonClient` con `TransportFactory` | el agente lee la API de operadores de la aplicación y la conduce; en el lado del agente no existe ningún operador |
| Separado — agente que habla MCP por HTTP | cualquiera de los lados aplicación | cualquier cliente MCP (Claude Desktop, un IDE) apuntando a `/mcp` | la aplicación aparece como cinco herramientas MCP |
| Separado — agente que solo puede arrancar un servidor MCP por stdio | cualquiera de los lados aplicación | `src/Ason.Bridge.McpHost` (`--transport grpc` o `--transport mcp`) | las mismas herramientas por el stdin/stdout del agente |
| Separado — **sin agente alguno** | cualquiera de los lados aplicación | `samples/ConsoleGrpcBridgeDemo`, `curl`, Swagger UI/Postman | un programa o un shell conduce la aplicación: una llamada a función o un script |

```bash
# --- sin separar: aplicación y agente en un solo proceso (🔑 requiere la clave del modelo) ---
dotnet run --project samples/WptDemoApp/WpfSampleApp.csproj -f net9.0-windows
dotnet run --project samples/BlazorAdvancedApp                       # http://localhost:5240
dotnet run --project samples/ConsoleExtractorSample
dotnet run --project samples/ConsoleMcpSample                        # también requiere MY_CONTEXT7_API_KEY
dotnet new install samples/templates && dotnet new ason.console   # añade --force para refrescar una instalación previa

# sin separar pero con el host de scripts remoto: arranca el runner y deja que la app lo use
dotnet run --project samples/RemoteRunnerService/RunnerServiceSample.csproj    # http://localhost:5236
#   en samples/WptDemoApp/ViewModels/ChatViewModel.cs descomenta:
#     UseRemoteRunner = true, RemoteRunnerBaseUrl = "http://localhost:5236"

# --- separado: el lado aplicación (sin modelo, sin clave) ---
dotnet run --project samples/WpfAppOnlyDemo -- --bridge-only --port 5222
#   gRPC   http://localhost:5222
#   MCP    http://localhost:5223/mcp
#   HTTP   http://localhost:5223/ason/openapi.json
dotnet run --project samples/ConsoleGrpcBridgeHost -- --port 5222    # los mismos endpoints, operadores de LibDemo

# --- separado: un lado agente ---
dotnet run --project samples/WpfAgentDemo                            # ventana de chat; la clave solo hace falta para el chat
dotnet run --project samples/WpfAgentDemo -- --verify http://localhost:5222           # autocomprobación gRPC, sin clave
dotnet run --project samples/WpfAgentDemo -- --verify http://localhost:5223/mcp --mcp # autocomprobación MCP, sin clave
Ason.Bridge.McpHost --url http://localhost:5222                      # relé MCP por stdio para Claude Desktop/Code

# --- separado: sin agente, solo un programa ---
#   contra el host de consola de arriba (sus operadores vienen de LibDemo)
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --func LibDemoOperator.GetProducts
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --script "return LibDemoStaticOperator.Add(40, 2);" --stream
curl -s http://localhost:5223/ason/openapi.json
curl -s -X POST http://localhost:5223/ason/functions/LibDemoStaticOperator/Add \
     -H "Content-Type: application/json" -d '{"arguments":[40,2]}'

#   contra la aplicación WPF de arriba (sus propios operadores)
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --func EmployeesOperator.GetDiagnostics
curl -s -X POST http://localhost:5223/ason/functions/EmployeesOperator/GetDiagnostics \
     -H "Content-Type: application/json" -d '{}'
```

La prueba automatizada de las filas separadas es la suite del puente: arranca la aplicación WPF real sin ventana,
la conduce por gRPC **y** MCP, arranca el ejemplo de agente real en modo `--verify`, arranca el relé como servidor
MCP stdio real y verifica los valores devueltos — sin ninguna clave de modelo:

```bash
dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj --configuration Release
```

La demo de un solo proceso conserva su propia prueba a nivel de interfaz, que necesita un escritorio Windows
interactivo:

```bash
dotnet test tests/WpfDemoApp.UiTests/WpfDemoApp.UiTests.csproj --configuration Release   # ajusta WPF_DEMO_TFM si hace falta
```

### Por qué existe el relé

El relé existe por lo que el MCP stdio *es*: el contrato dice que el cliente arranca el servidor y habla por su
stdin/stdout. Una aplicación de escritorio en ejecución no puede ser ese hijo, así que algo debe poseer la
tubería - y ese algo necesita un canal hacia la aplicación, de ahí los dos saltos en esta única forma de
despliegue. No es un requisito del diseño: un agente que habla MCP por HTTP se conecta directamente a la
aplicación, y una aplicación cuya vida *es* la sesión del agente puede servir stdio MCP ella misma con
`AddAsonMcpStdioBridge` (que es justo lo que usa el relé). El canal hacia la aplicación también es elegible:
`--transport grpc` (por defecto) o `--transport mcp`.

## Límites conocidos

- `manifest.proxies` es una instantánea; la interfaz de función única resuelve el handle vivo en cada llamada y
  es la ruta robusta para operadores de instancia.
- Invocar un operador cuya vista no está cargada dispara la recarga normal del runtime, que puede abrir o
  navegar la vista.
- El espacio de nombres `Ason.Bridge.Grpc` oculta el espacio raíz `Grpc` dentro de los ficheros que lo
  importan: usar `using Grpc.Net.Client;` y luego `GrpcChannel.ForAddress(...)`.
- El contrato gRPC no incluye el paso directo a MCP (`invokeMcpTool`); un relé lo informa como `not-supported`.
