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
| `protocolVersion` | Revisión del contrato; fallar rápido si no coincide. `1.1` es aditiva: un cliente `1.0` sigue funcionando |
| `appName` | Qué aplicación respondió |
| `execution` | Dónde se evalúan los scripts: `in-process`, `external-process`, `docker`, `remote-runner` |
| `capabilities` | Qué interfaces están habilitadas |
| `api` | La API de operadores, métodos, parámetros y tipos `[AsonModel]` |
| `markdown` | La misma lista como documento |
| `proxies` / `signatures` | La capa de proxies generada que necesita un script |
| `instances` | Las instancias vivas y el handle que direcciona cada una |
| `instancesRevision` | Resumen de `instances`; compáralo con un manifiesto guardado para saber si esa instantánea quedó obsoleta |

La lista proviene de `OperatorApiCatalog`, que aplica las mismas reglas de reflexión con las que se construye
el prompt del script: una lista no puede divergir de lo que los scripts pueden llamar realmente.

## Dos interfaces de ejecución independientes

| Capacidad | Interfaz | Cuándo usarla |
|---|---|---|
| `listApis` | manifiesto | El agente necesita saber qué puede llamar |
| `executeScript` | `ExecuteScriptAsync(script)` | El agente compone varias llamadas, ramas o bucles |
| `invokeFunction` | `InvokeFunctionAsync(operator, method, args)` | Una llamada precisa, sin generar texto de script |
| `invokeMcpTool` | paso directo a los clientes MCP de la aplicación: RPC gRPC `InvokeMcpTool`, herramienta MCP `ason_invoke_mcp_tool` | La aplicación consume otros servidores MCP |
| `logStream` | gRPC `StreamExecution`, MCP `ason_stream_script`, HTTP `POST /ason/script/stream` | El llamador quiere seguir la ejecución |

Son interruptores independientes y se combinan entre sí: gRPC responde `StatusCode.Unimplemented` a una
capacidad deshabilitada y MCP simplemente no registra la herramienta.

`invokeMcpTool` tiene además un segundo rechazo, con un significado distinto:

- **Capacidad desactivada**: la llamada no existe (`Unimplemented`, o no hay tal herramienta).
- **Capacidad activada pero sin servidor MCP registrado**: la llamada existe y responde `not-supported`,
  porque la aplicación habilitó el paso directo pero nunca registró un cliente con
  `RunnerClient.RegisterMcpClient`. El puente consulta `IAsonExecutor.McpServers`, así que un host con su
  propio ejecutor informa de los servidores que realmente tiene.

Un módulo de operador estático no necesita handle; un
operador de instancia se resuelve por el directorio de instancias vivas cuando solo existe una, y en caso
contrario requiere el `handle` del manifiesto. Los fallos llegan como códigos estables (`operator-not-found`,
`handle-required`, `handle-ambiguous`, `handle-not-found`, `method-not-found`, `script-rejected`,
`not-supported`, `execution-failed`).

## Instancias vivas y frescura del manifiesto

`manifest.proxies` termina con una declaración por cada instancia viva
(`EmployeesOperator employeesOperator = new("EmployeesOperator");`), que es lo que permite escribir
`employeesOperator.GetEmployees()`. Esas declaraciones solo son válidas en el momento de leer el manifiesto:
una vista abierta o cerrada después falta en ellas (o declara un handle que ya no existe).

El puente ofrece tres caminos, de menor a mayor robustez:

1. **Comparar `instancesRevision`**, un resumen de la lista de instancias, para detectar que la instantánea
   guardada quedó obsoleta y volver a leer el manifiesto (`ason_get_manifest`).
2. **Enviar solo el cuerpo.** `includeInstanceDeclarations: true` (gRPC `include_instance_declarations`, MCP
   `includeInstanceDeclarations`, HTTP `includeInstanceDeclarations`) hace que la aplicación aporte la capa de
   proxies *y* las declaraciones de hoy, de modo que el llamador solo manda las sentencias. Las declaraciones
   deben vivir dentro de la capa generada, así que solo la aplicación puede reconstruirlas.
3. **Nombrar tu capa de instantánea.** `GrpcAsonBridgeTransport` y `McpAsonBridgeTransport` aceptan
   `Proxies = manifest.Proxies`: cuando está definido, el transporte recorta esa capa exacta del script
   compuesto y pide a la aplicación la suya actual en cada llamada, sin ninguna ida y vuelta extra. Es el
   interruptor que usa el protocolo del runner en el lado del agente.

La interfaz de función única no necesita nada de esto: resuelve el handle vivo en cada llamada.

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

`Protos/ason_bridge.proto` es el contrato: `GetManifest`, `ListInstances`, `ExecuteScript`, `InvokeFunction`,
`InvokeMcpTool` y el streaming `StreamExecution`. `ExecuteScriptRequest` lleva `include_proxy_preamble` e
`include_instance_declarations`, `ManifestReply` lleva `instances_revision`, e `InvokeMcpToolRequest` lleva los
argumentos de la herramienta como objeto JSON. Todo lo añadido en el protocolo `1.1` es aditivo: un cliente
`1.0` sigue funcionando contra un puente `1.1`.

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

## Ver cómo se ejecuta un script

Los logs son una capacidad del *runtime* (`Capabilities.LogStream`) y cada adaptador publica la forma que su
protocolo puede entregar de verdad: el manifiesto dice que el runtime soporta logs, la superficie de cada
adaptador dice cómo obtenerlos.

| Adaptador | Superficie | Qué llega |
|---|---|---|
| gRPC | `StreamExecution` | Un evento `log` por línea mientras corre el script y luego un único `result`/`error` |
| HTTP | `POST {base}/script/stream` | Lo mismo como server-sent events (`event: log` … `event: result`) |
| MCP | herramienta `ason_stream_script` | Los logs *y* el resultado en la única respuesta, porque una llamada MCP no puede empujar |

Desactivar `LogStream` elimina las tres (la rpc responde `Unimplemented`, la ruta no se sirve, la herramienta no
se registra) y deja intactas las interfaces no streaming. Un transporte que hace de relé -el host MCP por
stdio sobre todo- recoge el stream de la propia aplicación en lugar de suscribirse a un evento de log que nunca
recibiría, así que los logs de un relé son los de la aplicación.

```bash
# server-sent events, directamente con curl
curl -N -X POST http://localhost:5223/ason/script/stream \
  -H 'Content-Type: application/json' \
  -d '{"code":"return LibDemoOperator.Add(40, 2);"}'
# event: log
# data: {"level":"Information","message":"...","source":"RunnerClient"}
#
# event: result
# data: {"success":true,"result":42}
```

`AsonBridgeRuntime` publica esos eventos en `AsonBridgeRuntime.Log` (`IAsonBridgeEndpoint.Log` en un adaptador),
que es también la vía por la que un host puede reflejarlos en su propio logging.

## Llamar al puente sin .NET

El contrato gRPC *es* la interfaz: un llamador en Python, Go, Java, Rust o `grpcurl` solo necesita el fichero
`.proto`. Dos caminos para obtener la misma copia que sirve la aplicación:

```xml
<!-- 1. Desde el paquete: el fichero viaja dentro de Ason.Bridge.Grpc -->
<PackageReference Include="Ason.Bridge.Grpc" Version="0.9.0" GeneratePathProperty="true" />
<!-- el contrato queda en $(PkgAson_Bridge_Grpc)\protos\ason_bridge.proto -->
```

```bash
# 2. Desde el repositorio, o desde el paquete descomprimido
unzip -o Ason.Bridge.Grpc.0.9.0.nupkg 'protos/*' -d ./ason-contract
# src/Ason.Bridge.Grpc/Protos/ason_bridge.proto
```

El servicio es `ason.bridge.v1.AsonBridge`, con `GetManifest`, `ListInstances`, `ExecuteScript`,
`InvokeFunction`, `InvokeMcpTool` y el streaming de servidor `StreamExecution` (un evento `log` por línea y
exactamente un `result` o `error` al final).

```bash
# Python: generar los stubs y llamar; samples/python/ason_bridge_client.py ya lo encapsula
python -m pip install -r samples/python/requirements.txt      # añade -i <mirror>/simple si PyPI va lento
python -m grpc_tools.protoc -I./ason-contract --python_out=. --grpc_python_out=. ason_bridge.proto

# Go / Java / cualquier lenguaje que soporte protoc
protoc -I./ason-contract --go_out=. --go-grpc_out=. ason_bridge.proto
protoc -I./ason-contract --java_out=. ason_bridge.proto

# grpcurl, con el contrato en disco...
grpcurl -plaintext -proto src/Ason.Bridge.Grpc/Protos/ason_bridge.proto \
  -d '{"code":"return 1;"}' localhost:5222 ason.bridge.v1.AsonBridge/ExecuteScript

# ...o sin él, si la aplicación publicó reflexión (--reflection, desactivado por defecto)
grpcurl -plaintext -d '{"operator":"LibDemoOperator","method":"Add","arguments_json":"[40,2]"}' \
  localhost:5222 ason.bridge.v1.AsonBridge/InvokeFunction
```

`samples/python/` es la versión ejecutable del primer ejemplo: compila el contrato en el primer uso y expone
`manifest`, `instances`, `call`, `script [--stream]` y `mcp <server> <tool>`.

La reflexión es opt-in y está desactivada por defecto (`AddAsonGrpcBridge(runtime, enableReflection: true)`, o
`--reflection` en el ejemplo): publicarla difunde la superficie llamable a cualquiera que alcance el puerto, lo
cual está bien en loopback y debe combinarse con la autorización de la sección [Seguridad](#seguridad) en
cualquier otro caso. En cualquier caso, **lo que un llamador puede invocar lo sigue diciendo el manifiesto**: la
reflexión solo te ahorra mantener el `.proto` sincronizado.

## Delegar la orquestación al agente

El agente puede conservar la orquestación de ASON sin poseer operadores: `manifest.ToOperatorsLibrary()` convierte
el manifiesto en la biblioteca de operadores con la que trabaja el cliente, y `AsonClientOptions.TransportFactory`
(por debajo, `RunnerClient.UseTransport`) apunta su runner a la aplicación, por ejemplo
`TransportFactory = () => new GrpcAsonBridgeTransport(client) { Proxies = manifest.Proxies }` (definir
`Proxies` hace que la aplicación reconstruya las declaraciones de instancia en cada ejecución). La aplicación
resuelve las llamadas a operadores en su propio proceso, de modo que el transporte nunca ve un mensaje
`invoke`; si llegara uno, se responde con un error en lugar de dejar al llamador esperando.

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

# todas las rutas, agrupadas y listas para enviar de una en una: samples/bridge-examples.http
# (VS Code REST Client, Rider, o copia un bloque a curl)

# las mismas llamadas desde un cliente .NET (el ejemplo de consola es exactamente este caso)
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --func EmployeesOperator.GetEmployees
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --script "return employeesOperator.GetDiagnostics().OnUiThread;" --stream
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

## Qué necesita saber y configurar un llamador

El puente es deliberadamente ignorante en materia de descubrimiento: responde preguntas, no se anuncia. Así que
la respuesta honesta a «¿qué hace falta para conectarse?» depende de la forma:

| Forma | Qué prepara el llamador | Qué configura la aplicación | ¿Cero configuración? |
|---|---|---|---|
| HTTP + OpenAPI | Una URL (y la clave, si la aplicación la definió) — `curl`, Swagger UI, Postman o cualquier cliente HTTP | `AddAsonOpenApiBridge(runtime)` + `MapAsonOpenApiBridge()`, un puerto y, opcionalmente, `ApiKey` | ✅ basta una URL |
| MCP por HTTP | Una URL terminada en `/mcp` y un cliente MCP | `AddAsonMcpBridge(runtime)` + `MapAsonMcpBridge()` y, opcionalmente, `requireAuthorization` | ✅ basta una URL |
| gRPC, .NET | La URL y un envoltorio de cliente: `GrpcAsonBridgeClient.Connect(url)` (o el stub generado) | `AddAsonGrpcBridge(runtime)` + `MapAsonGrpcBridge()`, un listener HTTP/2 y, opcionalmente, una policy | ⚠️ necesita cliente, no fichero de configuración |
| gRPC, otro lenguaje | La URL **y el contrato**: `ason_bridge.proto` del paquete y luego el stub generado — ver [Llamar al puente sin .NET](#llamar-al-puente-sin-net) | lo mismo, y opcionalmente reflexión (`enableReflection`) para no necesitar el `.proto` local | ⚠️ necesita el contrato |
| MCP por stdio | **Un comando de arranque** (`Ason.Bridge.McpHost --url …`), porque en stdio MCP el cliente lanza el servidor | la aplicación debe estar en marcha y accesible por gRPC o MCP | ❌ el llamador escribe un comando |

Tres premisas conviene decirlas sin rodeos, porque cada una produce un fallo desconcertante cuando se pasa por
alto:

1. **No hay registro, mDNS ni autodescubrimiento.** La URL es conocimiento fuera de banda: un argumento de línea
   de comandos, un fichero de configuración, una variable de entorno. El manifiesto es el descubrimiento
   *después* de saber a quién preguntar: dice qué expone la aplicación, no dónde está.
2. **Un handle es estado en tiempo de ejecución.** Las instancias aparecen y desaparecen al abrirse y cerrarse
   vistas, así que quien quiera dirigirse a una debe pedir `instances` antes (o aceptar `handle-required`,
   `handle-ambiguous` o `handle-not-found`). `instancesRevision` es lo que le avisa de que su foto quedó
   obsoleta.
3. **`proxies` es una instantánea.** Un script que usa una variable de instancia solo es correcto en el momento
   de leer el manifiesto; [Instancias vivas y frescura del manifiesto](#instancias-vivas-y-frescura-del-manifiesto)
   tiene las tres maneras de seguir siendo correcto.

Dos interruptores se quedan siempre del lado de la aplicación, y el llamador solo puede leerlos:

- **Las capacidades** deciden qué existe: con `executeScript` desactivado, gRPC responde `Unimplemented`, la
  herramienta MCP no se lista y la ruta HTTP es `404`. El manifiesto lo dice de antemano, así que el llamador se
  adapta en vez de probar.
- **La autorización** decide quién puede llamar: `AddAsonGrpcBridge(runtime, "<policy>")` y
  `AddAsonMcpBridge(endpoint, requireAuthorization: true)` la activan, y un llamador no autorizado recibe
  `Unauthenticated` / `401` — nunca `Unimplemented`, que sería indistinguible de una capacidad desactivada. Ver
  [Exigir autenticación al llamador](#exigir-autenticación-al-llamador).

Las fronteras que se cruzan están dibujadas en [architecture](architecture.es.md#separación-aplicación--agente-el-puente)
(la frontera D es la del llamador) y el eje de ubicación de la ejecución — ortogonal a todo lo anterior — está en
[modos de ejecución](execution-modes.es.md).

## Seguridad

- Enlazar a loopback por defecto; un puente expuesto en red necesita autenticación delante.
- Definir `ForbiddenScriptKeywords` con la misma lista que se daría a un `AsonClient` local; es un filtro de
  palabras clave, no un sandbox.
- Habilitar solo las capacidades necesarias; el manifiesto dice la verdad sobre cuáles están activas.
- `invokeMcpTool` está desactivado por defecto: reenvía a los servidores MCP que consume la *aplicación*, con
  las credenciales que esa aplicación ya tiene. Activarlo expone esas herramientas a todo llamador que el
  puente acepte, así que combínalo con la autorización de abajo cuando el puente no sea solo loopback.

### Exigir autenticación al llamador

La autorización es opt-in por adaptador y está desactivada por defecto, porque el montaje de desarrollo es un
puente en loopback sin credenciales:

| Adaptador | Cómo activarla | Qué ve un llamador no autorizado |
|---|---|---|
| gRPC | `AddAsonGrpcBridge(runtime, "<policy>")` — la policy es una policy normal de autorización de ASP.NET Core | `StatusCode.Unauthenticated` (nunca `Unimplemented`, que en todos los adaptadores significa "capacidad desactivada") |
| MCP (HTTP) | `AddAsonMcpBridge(endpoint, requireAuthorization: true)` | `401` |
| HTTP / OpenAPI | `AsonOpenApiBridgeOptions.ApiKey` (y `ApiKeyHeader`) | `401` |

Los clientes se identifican con cabeceras, y ese es todo el mecanismo: los metadatos de gRPC *son* una cabecera
HTTP/2:

```csharp
await using var grpc = GrpcAsonBridgeClient.Connect("http://localhost:5222", headers);   // p. ej. Authorization: Bearer …
await using var mcp = await McpAsonBridgeClient.ConnectAsync("http://localhost:5223/mcp", headers);
```

Un relé puede llevar las credenciales por un agente que no puede poner cabeceras por sí mismo:

```bash
Ason.Bridge.McpHost --url http://localhost:5222 --key <value>                     # X-Ason-Bridge-Key: <value>
Ason.Bridge.McpHost --url http://localhost:5222 --header "Authorization=Bearer <token>"
```

Los fallos de autorización siguen siendo un problema de credenciales, no un resultado de la aplicación: gRPC
expone `Unauthenticated` (el cliente tipado lo relanza en vez de convertirlo en una llamada fallida), MCP y HTTP
exponen `401`. Los fallos de nivel de aplicación conservan sus códigos de error.

## Ejemplos y cómo ejecutarlos

Qué ejemplo (o combinación de ejemplos) muestra cada forma — desde la disposición en un solo proceso hasta cada
manera de separarla. Las filas con 🔑 necesitan una clave de modelo: `MY_OPEN_AI_KEY` (y opcionalmente
`MY_OPEN_AI_BASE_URL`, `MY_OPEN_AI_MODEL`); `ConsoleMcpSample` además necesita `MY_CONTEXT7_API_KEY`.

**Roles**: `…AppSample` / `…Host` / `…AppOnlyDemo` son el **lado aplicación** — poseen los operadores y publican
los endpoints; `…CallerSample` / `…AgentSample` / `…AgentDemo` son el **lado llamador** — se conectan a esos
endpoints y no poseen ningún operador. Un mismo programa puede estar en los dos lados a la vez: el relé stdio es
llamador de la aplicación y, a la vez, aplicación (servidor) para el agente que lo lanzó.

| Forma | Lado aplicación | Lado llamador / agente | Qué se ve |
|---|---|---|---|
| **Sin separar** — aplicación de escritorio con el agente dentro | `samples/WptDemoApp` | el mismo proceso | el panel de chat conduce la UI de WPF mediante operadores en proceso |
| Sin separar — Blazor Server | `samples/BlazorAdvancedApp` (http://localhost:5240) | el mismo proceso | el panel de chat conduce componentes del servidor |
| Sin separar — consola con el agente extractor | `samples/ConsoleExtractorSample` | el mismo proceso | extracción de texto y llamadas a operadores en una consola |
| Sin separar — consola cuya API viene de un servidor MCP | `samples/ConsoleMcpSample` | el mismo proceso | el script llama a las herramientas MCP de Context7 como si fueran operadores |
| Sin separar — una aplicación nueva | `samples/templates` | el mismo proceso | `dotnet new ason.wpf` / `ason.winforms` / `ason.console` / `ason.blaz.srv` / `ason.maui` generan una app de chat funcional |
| Sin separar, pero con el **host de scripts** en remoto | `samples/WptDemoApp` + `samples/RemoteRunnerService` (http://localhost:5236) | el mismo proceso | solo se mueve la ejecución; app, agente, operadores y datos siguen juntos |
| **Separado** — agente .NET con su propia orquestación | `samples/WpfAppOnlyDemo` o `samples/ConsoleBridgeAppSample` | `samples/WpfAgentDemo`, o cualquier `AsonClient` con `TransportFactory` | el agente lee la API de operadores de la aplicación y la conduce; en el lado del agente no existe ningún operador |
| Separado — el mismo lado agente **sin interfaz** (cualquier SO) | cualquiera de los lados aplicación | `samples/ConsoleAgentSample` (`--list` no necesita clave; `--send "…"` sí) | el agente de consola imprime la API que construyó desde el manifiesto y luego conduce la aplicación |
| Separado, con el **host de scripts como proceso hijo de la aplicación** | `samples/ConsoleBridgeAppSample --execution external` | cualquier llamador de arriba | el manifiesto informa `execution=external-process`; el texto del script se ejecuta en el hijo mientras las llamadas a operadores se resuelven dentro de la aplicación |
| Separado — agente que habla MCP por HTTP | cualquiera de los lados aplicación | cualquier cliente MCP (Claude Desktop, un IDE) apuntando a `/mcp` | la aplicación aparece como cinco herramientas MCP |
| Separado — un cliente configurado para **MCP por stdio** | cualquiera de los lados aplicación | `samples/mcp/claude_desktop_config.json` (relé stdio) o `http_mcp_config.json` (HTTP) | un cliente de escritorio real ve las herramientas de la aplicación; `samples/python/ason_mcp_caller` comprueba esa configuración sin él |
| Separado — un **modelo** eligiendo herramientas MCP, como prueba | cualquiera de los lados aplicación | `samples/python/ason_mcp_agent` (🔑 `MY_OPEN_AI_KEY`) | el modelo elige la herramienta, el operador se ejecuta en la aplicación y `--expect` falla la ejecución si el resultado no aparece |
| Separado — agente que solo puede arrancar un servidor MCP por stdio | cualquiera de los lados aplicación | `src/Ason.Bridge.McpHost` (`--transport grpc` o `--transport mcp`) | las mismas herramientas por el stdin/stdout del agente |
| Separado — **sin agente alguno** | cualquiera de los lados aplicación | `samples/ConsoleBridgeCallerSample`, `curl`, Swagger UI/Postman | un programa o un shell conduce la aplicación: una llamada a función o un script |
| Separado — un llamador en **otro lenguaje** | cualquiera de los lados aplicación | `samples/python` (compila el `.proto` distribuido) | Python lista la API desde el manifiesto, llama a una función, ejecuta un script y lee sus logs |

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
dotnet run --project samples/ConsoleBridgeAppSample -- --port 5222    # los mismos endpoints, operadores de LibDemo

# --- separado: un lado agente ---
dotnet run --project samples/WpfAgentDemo                            # ventana de chat; la clave solo hace falta para el chat
dotnet run --project samples/WpfAgentDemo -- --verify http://localhost:5222           # autocomprobación gRPC, sin clave
dotnet run --project samples/WpfAgentDemo -- --verify http://localhost:5223/mcp --mcp # autocomprobación MCP, sin clave
Ason.Bridge.McpHost --url http://localhost:5222                      # relé MCP por stdio para Claude Desktop/Code

# configuraciones de cliente MCP listas para copiar (stdio y HTTP), y un cliente mínimo para comprobarlas
#   samples/mcp/claude_desktop_config.json · samples/mcp/http_mcp_config.json · samples/mcp/README.md
python samples/python/ason_mcp_caller/main.py --transport http --list

# el mismo lado agente como programa de consola: cualquier SO, y sin clave para inspeccionarlo
dotnet run --project samples/ConsoleAgentSample -- --url http://localhost:5222 --list
dotnet run --project samples/ConsoleAgentSample -- --url http://localhost:5223/mcp --transport mcp --list
dotnet run --project samples/ConsoleAgentSample -- --url http://localhost:5222 --send "add 20 and 22"   # requiere la clave

# la aplicación también puede evaluar scripts en un proceso hijo, quedándose con sus operadores
dotnet run --project samples/ConsoleBridgeAppSample -- --port 5222 --execution external

# --- separado: sin agente, solo un programa ---
#   contra el host de consola de arriba (sus operadores vienen de LibDemo)
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --func LibDemoOperator.GetProducts
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --script "return LibDemoStaticOperator.Add(40, 2);" --stream
curl -s http://localhost:5223/ason/openapi.json
curl -s -X POST http://localhost:5223/ason/functions/LibDemoStaticOperator/Add \
     -H "Content-Type: application/json" -d '{"arguments":[40,2]}'

#   contra la aplicación WPF de arriba (sus propios operadores)
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --func EmployeesOperator.GetDiagnostics
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

- `manifest.proxies` es una instantánea y sus declaraciones de instancia envejecen: compara
  `instancesRevision`, envía solo el cuerpo con `includeInstanceDeclarations`, o define `Proxies` en el
  transporte del runner (ver «Instancias vivas y frescura del manifiesto»). La interfaz de función única
  resuelve el handle vivo en cada llamada y es la ruta robusta para operadores de instancia.
- Invocar un operador cuya vista no está cargada dispara la recarga normal del runtime, que puede abrir o
  navegar la vista.
- El espacio de nombres `Ason.Bridge.Grpc` oculta el espacio raíz `Grpc` dentro de los ficheros que lo
  importan: usar `using Grpc.Net.Client;` y luego `GrpcChannel.ForAddress(...)`.
- El paso directo solo nombra un servidor y una herramienta: el puente no replica la lista de herramientas de
  los servidores MCP que consume la aplicación, así que el llamador las conoce por la aplicación (o por la
  configuración del agente), no por el manifiesto.
