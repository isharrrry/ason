# Modos de ejecución y ejecución remota

[English](execution-modes.md) | [中文](execution-modes.zh-CN.md) | **Español**

> Parte de la documentación de **ASON** — volver al [README](../README.es.md).

## Modos de ejecución / entornos

- **En proceso** – Los scripts se ejecutan en el mismo proceso que su aplicación.  
  Ideal para desarrollo y pruebas, pero no se recomienda para producción debido al aislamiento limitado.  
  ASON incluye análisis estático para bloquear operaciones no seguras (reflexión, E/S de archivos, red, etc.).

- **Proceso externo** – Los scripts se ejecutan en un proceso separado, lo que proporciona una capa adicional de protección.  
  Agregue el paquete NuGet `Ason.ExternalExecutor` para habilitar este modo.

- **Docker** – Los scripts se ejecutan dentro de un contenedor totalmente aislado para lograr la máxima seguridad.  
  Requiere tener Docker instalado localmente.  
  Extraiga el contenedor necesario antes de ejecutar la aplicación:

  > docker pull ghcr.io/alexgoon/ason:0.8.1

- **Servidor remoto** – Los scripts se ejecutan en un servidor remoto, ya sea en un proceso externo o en un contenedor Docker.  
  Consulte la siguiente sección para obtener los detalles de configuración.
  
![ASON Execution Environments](../images/execution-environments.jpg)


## Elegir un modo

| Situación | Modo sugerido |
|---|---|
| Desarrollo local y pruebas, scripts que deben tocar el estado de la UI | **En proceso** |
| Aplicaciones de escritorio que no deberían ejecutar código generado en su propio proceso | **Proceso externo** |
| Entrada no confiable, o cuando se requiere el máximo aislamiento | **Docker** |
| Clientes móviles, o máquinas sin Docker | **Servidor remoto** |

## Ejecución remota

Puede ejecutar scripts de ASON en un **servidor remoto**, lo cual resulta especialmente útil para clientes móviles o cuando Docker no está disponible en la máquina de un usuario.

Para habilitar la ejecución remota:

1. Cree un proyecto estándar de **ASP.NET Core Web API** e instale el paquete NuGet `Ason.RemoteBridge`.  
2. En `Program.cs`, registre el ejecutor de scripts:

```csharp
builder.Services.AddAsonScriptRunner();  
app.MapAson("/scriptRunnerHub", requireAuthorization: false);
```

3. En la aplicación cliente, habilite el ejecutor remoto en `AsonClientOptions`:

```csharp
AsonClientOptions options = new() {  
    ExecutionMode = ExecutionMode.ExternalProcess,  
    RemoteRunnerBaseUrl = "http://localhost:5222",  
    UseRemoteRunner = true,  
};
```

En la **MAUI Project Template** se incluye un proyecto de ejemplo que demuestra esta configuración.

## Modos vs. despliegue: dos ejes independientes

`ExecutionMode` y `UseRemoteRunner` son ortogonales: el primero selecciona cómo se aísla el script y el
segundo selecciona dónde se ejecuta el host del script. El transporte se deriva de esa combinación:

```csharp
// RunnerTransportManager
RequiresTransport => UseRemoteRunner || Mode != ExecutionMode.InProcess;
// CreateTransport(): UseRemoteRunner -> SignalRTransport(RemoteUrl)
//                    otherwise      -> StdIoProcessTransport(Mode, DockerImage, RunnerExecutablePath)
```

| # | `ExecutionMode` | `UseRemoteRunner` | El script se evalúa en | Ruta de enlace |
|---|---|---|---|---|
| 1 | `InProcess` | `false` | tu propio proceso — sin ningún transporte | solo el cliente |
| 2 | `ExternalProcess` | `false` | proceso hijo de `Ason.ExternalExecutor` en la máquina del cliente | cliente → hijo |
| 3 | `Docker` | `false` | contenedor iniciado como `docker run --rm -i <image>` en la máquina del cliente | cliente → hijo → contenedor |
| 4 | `InProcess` | `true` | el **proceso del servicio** de ejecución remota | cliente → servidor |
| 5 | `ExternalProcess` / `Docker` | `true` | un proceso hijo o un contenedor en el **servidor** | cliente → servidor → ejecutor del lado del servidor |

En todas las filas los métodos de los operadores se siguen ejecutando en el proceso del cliente. Con
`UseRemoteRunner = true` el modo seleccionado se envía al servidor (`StartRunner((int)mode, dockerImage)`),
que decide si evalúa el script en su propio proceso o si lanza un ejecutor. Las fronteras se describen en
[arquitectura](architecture.es.md#topología-de-despliegue).

Con el [puente](app-agent-separation.es.md) apareció un tercer eje: **quién aporta el transporte del runner**.
Con `AsonClientOptions.TransportFactory` (por debajo, `RunnerClient.UseTransport`) el host entrega al cliente un
transporte propio, de modo que el script puede evaluarlo **otro proceso** — una aplicación que publica sus
operadores por gRPC, MCP o HTTP/OpenAPI — mientras el cliente conserva la generación de proxies, los reintentos,
la validación y el manejo del resultado. Los tres ejes se combinan libremente: el modo describe el aislamiento
**en el lado que evalúa** y el transporte solo dice cómo llegar a ese lado.

## Qué configuración encaja con cada forma de aplicación

| Forma de la aplicación | Recomendado | Por qué |
|---|---|---|
| Aplicación de escritorio WPF o WinForms, entrada confiable | `InProcess` (lo predeterminado del ejemplo) o `ExternalProcess` | los datos y la UI permanecen locales, y una llamada a un operador es IPC en proceso o dentro de la misma máquina — la latencia más baja posible |
| Aplicación de escritorio que no debe ejecutar código generado en su propio proceso | `ExternalProcess`, o `Docker` cuando la máquina tiene Docker | aislamiento sin infraestructura adicional |
| Aplicación de escritorio cuyos usuarios no pueden instalar Docker, pero el código generado tampoco debería ejecutarse en su máquina | remoto en tu servidor, con `Docker` (o `InProcess`) allí | el host del script sale del cliente mientras los operadores, los datos y las credenciales permanecen locales — consulta [credenciales y fronteras de datos](architecture.es.md#credenciales-y-fronteras-de-datos) |
| Aplicación Blazor Server / ASP.NET Core donde los datos ya viven en el servidor | ejecutar `AsonClient` **dentro** de esa aplicación mediante `AddAson` | el host del cliente *es* el servidor; los operadores ya trabajan sobre los datos del servidor, así que la ejecución remota no aporta nada |
| Blazor WebAssembly | remoto, o `InProcess` en el navegador si la superficie de operadores lo permite | un navegador no puede lanzar procesos ni contenedores |
| MAUI / clientes móviles u otros clientes ligeros | remoto (`Ason.RemoteBridge` + `UseRemoteRunner`) | el dispositivo no puede alojar un ejecutor — esto es lo que demuestra la plantilla de MAUI |
| Un solo servicio ejecutando scripts para muchos clientes | un host de ejecución remota dedicado | un único lugar para versionar el ejecutor, aplicar políticas y recopilar logs |
| Una aplicación y un proceso de agente separado (incluido un agente que solo habla MCP) | publicar los operadores de la aplicación con `Ason.Bridge` y dejar que el agente los conduzca por gRPC, MCP o HTTP/OpenAPI | los operadores, los datos y la UI permanecen en la aplicación mientras el modelo y la orquestación permanecen en el agente — consulta [separación aplicación / agente](app-agent-separation.es.md) |

## Cómo elegir entre ellos y cuánto cuesta cada opción

<!-- i18n: localize-labels - etiquetas localizadas, estructura intacta -->

```
¿Necesitas aislamiento respecto del código generado?
  no  -> In-process                     lo más rápido; ningún proceso extra; el filtro de palabras clave es la única barrera
  sí  -> ¿Puede este host de cliente alojar un runner (un proceso hijo y, para contenedores, Docker)?
           sí  -> External process / Docker local     latencia más baja, los datos nunca salen de la máquina
           no  -> remoto, con Docker / proceso externo / In-process en el servidor
                  (clientes móviles, navegadores y clientes restringidos o ligeros)
```

El árbol de decisión anterior se lee como una serie de preguntas encadenadas. La primera pregunta es si
necesitas aislamiento respecto del código generado. Si la respuesta es *no*, la rama izquierda lleva
directamente a In-process: es la opción más rápida, no añade ningún proceso extra y el filtro de palabras
clave es la única barrera. Si la respuesta es *sí*, aparece una segunda pregunta: si este host de cliente
puede alojar un runner, es decir, un proceso hijo y, para contenedores, Docker. Una respuesta afirmativa
lleva a un External process o a Docker local, con la latencia más baja y sin que los datos salgan nunca de
la máquina. Una respuesta negativa lleva a la ejecución remota, con Docker, un proceso externo o
In-process en el servidor; la propia rama enumera los casos típicos: clientes móviles, navegadores y
clientes restringidos o ligeros.

La latencia es el principal costo de la ejecución remota: cada llamada a un operador es una ida y vuelta
por la red (más un salto local cuando el servidor lanza un ejecutor), y cada llamada se marshalla de
vuelta al hilo de la UI del cliente. Por lo tanto, un script que llama a operadores una vez por elemento
sobre N elementos cuesta aproximadamente N idas y vueltas, mientras que un script evaluado localmente no
cuesta ninguna.
