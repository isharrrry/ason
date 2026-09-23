# Contribuir

[English](contributing.md) | [中文](contributing.zh-CN.md) | **Español**

> Parte de la documentación de **ASON** — volver al [README](../README.es.md).

## Compilación

```bash
dotnet build Ason.sln --configuration Release
```

Toda la solución se compila en Windows. El ejemplo de WPF (`samples/WptDemoApp`) y las pruebas de interfaz de FlaUI están dirigidos a `*-windows`, por lo que no se espera que la compilación de toda la solución en Linux o macOS sea exitosa; en su lugar, compila los proyectos que necesites:

```bash
dotnet build src/Ason/Ason.csproj --configuration Release
```

## Estructura del repositorio

| Ruta | Contenido |
|---|---|
| `src/Ason.Abstractions` | solo atributos marcadores (`netstandard2.0`) |
| `src/Ason` | el runtime: cliente, orquestación, generación de proxies (`net6.0`, `net9.0`) |
| `src/Ason.Runner.Core` | host de ejecución de scripts |
| `src/Ason.ExternalExecutor` | ejecutable del runner fuera de proceso |
| `src/Ason.RemoteBridge` | runner remoto de ASP.NET Core |
| `src/Ason.Bridge` | puente neutral respecto al transporte: manifiesto, capacidades, ejecutores, directorio de operadores (`net6.0`, `net9.0`) |
| `src/Ason.Bridge.Grpc` | adaptador gRPC: servicio, cliente tipado, transporte del runner, endpoint de reenvío |
| `src/Ason.Bridge.Mcp` | adaptador MCP: superficie de herramientas, cliente tipado, transporte del runner (Streamable HTTP) |
| `src/Ason.Bridge.OpenApi` | adaptador HTTP + OpenAPI (Swagger): endpoints y un documento generado desde el manifiesto |
| `src/Ason.Bridge.McpHost` | relé que republica el puente gRPC de una aplicación como MCP por stdio |
| `samples/WpfAppOnlyDemo` | lado aplicación en WPF: operadores `[Ason*]` más servicios gRPC, MCP y HTTP/OpenAPI, sin agente |
| `samples/WpfAgentDemo` | lado agente en WPF: chat y selección de endpoint/transporte, sin un solo `[AsonOperator]` |
| `samples/ConsoleGrpcBridgeHost` | lado aplicación de un despliegue dividido: operadores `[Ason*]` más servicios gRPC y MCP |
| `samples/ConsoleGrpcBridgeDemo` | lado solicitante externo: manifiesto, llamadas a funciones, scripts, logs |
| `samples/WptDemoApp` | demo de WPF (`net10.0`, `net9.0`, `net6.0-windows`) |
| `samples/LibDemo` | biblioteca de clases que solo usa los marcadores (`net6.0`, `netstandard2.0`) |
| `samples/templates` | las plantillas de `dotnet new` |
| `tests/*` | suites de pruebas, ver más abajo |

## Suites de pruebas

| Suite | Framework | Notas |
|---|---|---|
| `tests/LibDemo.SmokeTests` | net6.0 / net9.0 / net10.0 | descubrimiento de marcadores, reenvíos de tipos e invocación real de operadores en proceso |
| `tests/Ason.Tests` | net9.0 | los casos de `E2E_AllExecutionModes(executionMode: Docker, …)` necesitan un demonio de Docker; `McpClientTests` necesita servidores MCP activos |
| `tests/Ason.Runner.Tests` | net9.0 | runner de scripts |
| `tests/Ason.RemoteRunner.Tests` | net9.0 | la prueba de integración se omite a menos que `ASON_REMOTE_RUNNER_URL` apunte a un runner remoto en ejecución |
| `tests/Ason.Bridge.Tests` | net9.0 | el núcleo del puente, los adaptadores gRPC/MCP/OpenAPI (cada host se levanta en proceso y se conduce por el cable), la costura del transporte, el endpoint de reenvío y las pruebas de extremo a extremo de los ejemplos de WPF (se omiten en Linux o si los ejemplos de Windows no están compilados) |
| `tests/WpfDemoApp.UiTests` | net9.0-windows | automatización de interfaz de FlaUI — necesita una sesión interactiva de escritorio de Windows |

Ejecución del subconjunto hermético sin Docker:

```bash
dotnet test tests/Ason.Tests/Ason.Tests.csproj --configuration Release --filter "DisplayName!~Docker&FullyQualifiedName!~McpClientTests"
```

La cobertura de la suite del puente se recoge con `coverlet.runsettings`, que deja fuera el código gRPC que
genera protoc para que el porcentaje describa los adaptadores escritos a mano:

```bash
dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj --configuration Release --collect:"XPlat Code Coverage" --settings coverlet.runsettings
```

Variables de entorno que modifican el comportamiento de las pruebas de interfaz:

| Variable | Significado |
|---|---|
| `WPF_DEMO_TFM` | qué compilación de ejemplo ejecutar — `net9.0-windows` (predeterminada), `net6.0-windows` o `net10.0-windows` |
| `WPF_DEMO_CONFIG` | `Release` (predeterminada) o `Debug` |
| `MY_OPEN_AI_KEY`, `MY_OPEN_AI_BASE_URL`, `MY_OPEN_AI_MODEL` | habilitan la prueba integral en vivo; sin una clave, se informa como omitida |

La configuración de un proveedor se describe en [Proveedores de IA](ai-providers.es.md).

## Integración continua

El archivo `.github/workflows/ci.yml` se ejecuta en cada push y en los pull requests. Su job de Linux compila los proyectos multiplataforma y ejecuta las suites herméticas; los casos de modo Docker y MCP se excluyen allí, y las pruebas de extremo a extremo de los ejemplos de WPF se omiten. Un segundo job (`windows-samples`) compila los dos ejemplos de WPF y vuelve a ejecutar `tests/Ason.Bridge.Tests` en Windows, que es lo que hace que esas pruebas se ejecuten de verdad; también compila la demo de WPF original, para que un cambio en la biblioteca no pueda romper el ejemplo que debe seguir funcionando. Las pruebas de interfaz de la demo original, los casos de FlaUI y la variante net10.0 siguen sin cobertura en ningún job.
