# Execution modes and remote execution

[English](execution-modes.md) | [中文](execution-modes.zh-CN.md) | [Español](execution-modes.es.md)

> Part of the **ASON** documentation — back to the [README](../README.md).

## Execution modes / environments

- **In-process** – Scripts run in the same process as your application.  
  Ideal for development and testing, but not recommended for production due to limited isolation.  
  ASON includes static analysis to block unsafe operations (reflection, file I/O, networking, etc.).

- **External process** – Scripts run in a separate process, providing an extra layer of protection.  
  Add the `Ason.ExternalExecutor` NuGet package to enable this mode.

- **Docker** – Scripts execute inside a fully isolated container for maximum security.  
  Requires Docker installed locally.  
  Pull the required container before running the app:

  > docker pull ghcr.io/alexgoon/ason:0.8.1

- **Remote server** – Scripts execute on a remote server, either in an external process or Docker container.  
  See the next section for configuration details.
  
![ASON Execution Environments](../images/execution-environments.jpg)


## Choosing a mode

| Situation | Suggested mode |
|---|---|
| Local development and tests, scripts that must touch UI state | **In-process** |
| Desktop apps that should not run generated code in their own process | **External process** |
| Untrusted input, or when maximum isolation is required | **Docker** |
| Mobile clients, or machines without Docker | **Remote server** |

## Remote execution

You can run ASON scripts on a **remote server**, which is especially useful for mobile clients or when Docker is unavailable on a user machine.

To enable remote execution:

1. Create a standard **ASP.NET Core Web API** project and install the `Ason.RemoteBridge` NuGet package.  
2. In `Program.cs`, register the script runner:

```csharp
builder.Services.AddAsonScriptRunner();  
app.MapAson("/scriptRunnerHub", requireAuthorization: false);
```

3. In the client application, enable the remote runner in `AsonClientOptions`:

```csharp
AsonClientOptions options = new() {  
    ExecutionMode = ExecutionMode.ExternalProcess,  
    RemoteRunnerBaseUrl = "http://localhost:5222",  
    UseRemoteRunner = true,  
};
```

A sample project demonstrating this setup is included in the **MAUI Project Template**.

