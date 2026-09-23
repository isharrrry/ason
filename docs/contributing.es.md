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
| `samples/ConsoleBridgeAppSample` | lado aplicación de un despliegue dividido: operadores `[Ason*]` más servicios gRPC y MCP |
| `samples/ConsoleAgentSample` | lado agente como programa de consola: construye su API desde el manifiesto de la aplicación y la conduce por gRPC o MCP (multiplataforma, sin interfaz) |
| `samples/ConsoleBridgeCallerSample` | lado solicitante externo: manifiesto, llamadas a funciones, scripts, logs |
| `samples/WptDemoApp` | demo de WPF (`net10.0`, `net9.0`, `net6.0-windows`) |
| `samples/LibDemo` | biblioteca de clases que solo usa los marcadores (`net6.0`, `netstandard2.0`) |
| `samples/mcp` | configuraciones de cliente MCP listas para copiar (relé stdio y HTTP) y la lista de herramientas |
| `samples/python` | llamadores que no son .NET: cliente gRPC, cliente MCP de biblioteca estándar y una prueba de tool calling MCP dirigida por OpenAI |
| `samples/bridge-examples.http` | todas las rutas HTTP del puente, agrupadas y listas para enviar de una en una |
| `samples/templates` | las plantillas de `dotnet new` |
| `scripts` | comprobaciones que ejecuta CI (suelo de cobertura, contrato empaquetado, anotaciones de fallo) y el ejecutor del job de Linux |
| `tests/*` | suites de pruebas, ver más abajo |
| `.agents/plans` | planes de implementación, guardados en el repositorio a propósito para revisar las decisiones junto al código |
| `CHANGELOG.md` | cambios publicados, una sección por versión |

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

Los cuatro proyectos del puente tienen un **suelo**, y el job de CI lo comprueba adaptador por adaptador
(línea ≥ 87%, ramas ≥ 70%; a fecha de 0.9.0 están en 89–98% y 75–82%):

```bash
./scripts/check-bridge-coverage.ps1 -CoverageFile 'artifacts/coverage/*/coverage.cobertura.xml'
```

Los ensamblados de los ejemplos quedan fuera de ese suelo a propósito: cada ejemplo es un proceso aparte y la
cobertura se recoge dentro del host de pruebas, así que el ensamblado de un ejemplo no se puede medir así. Los
ejemplos los cubren las pruebas de extremo a extremo a nivel de proceso (`ConsoleSamplesEndToEndTests`,
`WpfApplicationEndToEndTests`, `RemoteRunnerBridgeEndToEndTests`), que primero los compilan y luego conducen los
procesos reales.

Variables de entorno que modifican el comportamiento de las pruebas de interfaz:

| Variable | Significado |
|---|---|
| `WPF_DEMO_TFM` | qué compilación de ejemplo ejecutar — `net9.0-windows` (predeterminada), `net6.0-windows` o `net10.0-windows` |
| `WPF_DEMO_CONFIG` | `Release` (predeterminada) o `Debug` |
| `MY_OPEN_AI_KEY`, `MY_OPEN_AI_BASE_URL`, `MY_OPEN_AI_MODEL` | habilitan la prueba integral en vivo; sin una clave, se informa como omitida |
| `ASON_BRIDGE_KEY`, `ASON_BRIDGE_REMOTE_URL`, `ASON_BRIDGE_EXECUTION` | equivalentes de `--key`, `--remote-url` y `--execution` para el relé y el ejemplo de aplicación |

La configuración de un proveedor se describe en [Proveedores de IA](ai-providers.es.md).

## Integración continua

El archivo `.github/workflows/ci.yml` se ejecuta en cada push y en los pull requests. Su job de Linux compila los
proyectos multiplataforma (incluidos los ejemplos de consola y del runner remoto) y ejecuta las suites
herméticas; los casos de modo Docker y MCP se excluyen allí, y las pruebas de extremo a extremo de los ejemplos
de WPF se omiten. Después recoge la cobertura, comprueba los suelos por adaptador y descomprime el paquete
`Ason.Bridge.Grpc` para demostrar que el contrato distribuido sigue conteniendo `protos/ason_bridge.proto`. Un
segundo job (`windows-samples`) compila los ejemplos de WPF y vuelve a ejecutar `tests/Ason.Bridge.Tests` y la
suite de la biblioteca en Windows, que es lo que hace que esas pruebas se ejecuten de verdad; además ejecuta las
pruebas de UI de FlaUI con `continue-on-error`, porque UI Automation necesita una sesión de escritorio
interactiva que un runner alojado solo ofrece de forma inconsistente. La variante net10.0 queda fuera hasta que
ese SDK sea GA.

Cada paso de pruebas escribe un archivo TRX, y un último paso (`scripts/emit-test-failures.ps1`, protegido con
`if: failure()`) los convierte en anotaciones del check run. Es intencionado: el log del job de una ejecución
fallida solo lo puede descargar quien tenga permisos de administrador, mientras que la anotación que lleva el
nombre de la prueba y la aserción se puede leer de forma anónima, incluso por las personas y las herramientas que
tienen que explicar el fallo.

## Reproducir el job de Linux en tu propia máquina

`scripts/ci-linux.sh` ejecuta ese mismo job con los mismos comandos y en el mismo orden, de modo que una pasada
local y una de CI significan lo mismo. Solo necesita un SDK de .NET, PowerShell 7 y git: nada de Docker, ni
Python, ni sesión de escritorio:

```bash
# .NET 9 compila y ejecuta todo; 6.0 se compila y ejecuta sus smoke tests; 10.0 hace falta porque
# tests/LibDemo.SmokeTests apunta a net10.0 (la imagen del runner de CI lo trae, por eso CI no lo nota).
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --channel 9.0 --install-dir "$HOME/.dotnet"
bash dotnet-install.sh --channel 6.0 --runtime dotnet --install-dir "$HOME/.dotnet"
bash dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
sudo apt-get install -y powershell        # PowerShell 7, desde packages.microsoft.com
export PATH="$HOME/.dotnet:$PATH"

./scripts/ci-linux.sh                     # compilación, todas las suites, suelo de cobertura, contrato empaquetado
./scripts/ci-linux.sh --skip-smoke        # cuando no hay SDK de .NET 10
./scripts/ci-linux.sh --skip-build --suite bridge --filter "FullyQualifiedName~McpRelayHostTests"
```

Hay dos diferencias deliberadas con CI: ejecuta todos los pasos y los informa todos en lugar de detenerse en el
primer fallo (un fallo de *compilación* sí lo detiene, porque todo lo demás necesita la compilación), y cuando algo
falla imprime las mismas anotaciones `::error` que publica CI. El job de Windows —los ejemplos de WPF y la
automatización de UI con FlaUI— no se puede reproducir aquí; ese necesita Windows.
