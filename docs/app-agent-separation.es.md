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
  componente que conoce un transporte (gRPC, MCP, ...).

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

## Seguridad

- Enlazar a loopback por defecto; un puente expuesto en red necesita autenticación delante.
- Definir `ForbiddenScriptKeywords` con la misma lista que se daría a un `AsonClient` local; es un filtro de
  palabras clave, no un sandbox.
- Habilitar solo las capacidades necesarias; el manifiesto dice la verdad sobre cuáles están activas.
- `invokeMcpTool` está desactivado por defecto.

## Ejemplos

| Ejemplo | Rol |
|---|---|
| `samples/WpfAppOnlyDemo` | **El lado aplicación como aplicación de escritorio real**: una ventana WPF con operadores `[Ason*]` que publica gRPC, MCP y HTTP/OpenAPI. Sin modelo ni chat. `--bridge-only --port 5222` la ejecuta sin ventana |
| `samples/WpfAgentDemo` | **El lado agente como aplicación de escritorio real**: ventana de chat, selección de endpoint y transporte (gRPC/MCP), la lista de API leída de la aplicación y un registro de llamadas. No declara ningún `[AsonOperator]`. `--verify <endpoint> [--mcp]` ejecuta una autocomprobación sin interfaz |
| `samples/ConsoleGrpcBridgeHost` | Lado aplicación en su forma mínima: operadores `[Ason*]` de `LibDemo`, gRPC + MCP + OpenAPI, sin agente |
| `samples/ConsoleGrpcBridgeDemo` | Lado solicitante externo: manifiesto, instancias, llamadas a funciones, scripts, logs |
| `src/Ason.Bridge.McpHost` | Relé stdio para agentes que solo hablan MCP |

El relé existe por lo que el MCP stdio *es*: el contrato dice que el cliente arranca el servidor y habla por su
stdin/stdout. Una aplicación de escritorio en ejecución no puede ser ese hijo, así que algo debe poseer la
tubería - y ese algo necesita un canal hacia la aplicación, de ahí los dos saltos en esta única forma de
despliegue. No es un requisito del diseño: un agente que habla MCP por HTTP se conecta directamente a la
aplicación, y una aplicación cuya vida *es* la sesión del agente puede servir stdio MCP ella misma con
`AddAsonMcpStdioBridge` (que es justo lo que usa el relé). El canal hacia la aplicación también es elegible:
`--transport grpc` (por defecto) o `--transport mcp`.

```bash
dotnet run --project samples/ConsoleGrpcBridgeHost -- --port 5222
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --func LibDemoStaticOperator.Add --args "[2,3]"
```

## Límites conocidos

- `manifest.proxies` es una instantánea; la interfaz de función única resuelve el handle vivo en cada llamada y
  es la ruta robusta para operadores de instancia.
- Invocar un operador cuya vista no está cargada dispara la recarga normal del runtime, que puede abrir o
  navegar la vista.
- El espacio de nombres `Ason.Bridge.Grpc` oculta el espacio raíz `Grpc` dentro de los ficheros que lo
  importan: usar `using Grpc.Net.Client;` y luego `GrpcChannel.ForAddress(...)`.
- El contrato gRPC no incluye el paso directo a MCP (`invokeMcpTool`); un relé lo informa como `not-supported`.
