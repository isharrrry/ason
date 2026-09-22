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

