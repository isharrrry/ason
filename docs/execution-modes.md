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

  > docker pull ghcr.io/alexgoon/ason:0.9.0

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

## Modes vs. deployment: two independent axes

`ExecutionMode` and `UseRemoteRunner` are orthogonal: the first selects how the script is isolated, the
second selects where the script host runs. The transport follows from that combination:

```csharp
// RunnerTransportManager
RequiresTransport => UseRemoteRunner || Mode != ExecutionMode.InProcess;
// CreateTransport(): UseRemoteRunner -> SignalRTransport(RemoteUrl)
//                    otherwise      -> StdIoProcessTransport(Mode, DockerImage, RunnerExecutablePath)
```

| # | `ExecutionMode` | `UseRemoteRunner` | The script is evaluated in | Link path |
|---|---|---|---|---|
| 1 | `InProcess` | `false` | your own process — no transport at all | client only |
| 2 | `ExternalProcess` | `false` | `Ason.ExternalExecutor` child process on the client machine | client → child |
| 3 | `Docker` | `false` | container started as `docker run --rm -i <image>` on the client machine | client → child → container |
| 4 | `InProcess` | `true` | the remote runner **service process** | client → server |
| 5 | `ExternalProcess` / `Docker` | `true` | a child process or container on the **server** | client → server → server-side executor |

In every row the operator methods still run in the client process. With `UseRemoteRunner = true` the
selected mode is sent to the server (`StartRunner((int)mode, dockerImage)`), which decides whether to
evaluate the script in its own process or to spawn an executor. The boundaries are described in
[architecture](architecture.md#deployment-topology).

A third axis appeared with the [bridge](app-agent-separation.md): **who supplies the runner transport**. With
`AsonClientOptions.TransportFactory` (or `RunnerClient.UseTransport` underneath it) a host hands the client a
transport of its own, so the script can be evaluated by another process — an application that publishes its
operators over gRPC, MCP or HTTP/OpenAPI — while the client keeps proxy generation, retries, validation and
result handling. The three axes combine freely: the mode still describes the isolation **on the side that
evaluates**, and the transport only says how to reach that side. Function-level invocation is outside these
axes altogether: calling one operator method does not involve a script host at all (no executor process, no
compilation, one round trip), which is why a test harness or an automation script can use it without caring
where scripts would run.

## Which configuration fits which application shape

| Application shape | Recommended | Why |
|---|---|---|
| WPF or WinForms desktop app, trusted input | `InProcess` (the sample default) or `ExternalProcess` | data and UI stay local, and an operator call is in-process or in-machine IPC — the lowest latency possible |
| Desktop app that must not run generated code in its own process | `ExternalProcess`, or `Docker` when the machine has Docker | isolation without any extra infrastructure |
| Desktop app whose users cannot install Docker, but the generated code should still not run on their machine | remote on your server, with `Docker` (or `InProcess`) there | the script host leaves the client while operators, data and credentials stay local — see [credentials and data boundaries](architecture.md#credentials-and-data-boundaries) |
| Blazor Server / ASP.NET Core app where the data already lives on the server | run `AsonClient` **inside** that app via `AddAson` | the client host *is* the server; operators already work on server data, so remote execution adds nothing |
| Blazor WebAssembly | remote, or `InProcess` in the browser if the operator surface allows it | a browser cannot spawn processes or containers |
| MAUI / mobile or other thin clients | remote (`Ason.RemoteBridge` + `UseRemoteRunner`) | the device cannot host an executor — this is what the MAUI template demonstrates |
| One service running scripts for many clients | a dedicated remote runner host | a single place to version the executor, enforce policy and collect logs |
| An application and a separate agent process (including an MCP-only agent) | publish the application's operators with `Ason.Bridge` and let the agent drive them over gRPC, MCP or HTTP/OpenAPI | the operators, the data and the UI stay in the application while the model and the orchestration stay in the agent — see [application / agent separation](app-agent-separation.md) |

A runnable example of every row above — the single-process shapes and the split deployment alike — is listed
with its exact command in [samples and how to run them](app-agent-separation.md#samples-and-how-to-run-them).

## Choosing between them, and what each choice costs

<!-- i18n: localize-labels - translate the labels, keep the structure (arrows, indentation) -->

```
Do you need isolation from the generated code?
  no  -> In-process                     fastest; no extra process; the keyword filter is the only barrier
  yes -> Can this client host a runner (a child process, plus Docker for containers)?
           yes -> local External process / Docker    lowest latency, data never leaves the machine
           no  -> remote, with Docker / external process / in-process on the server
                  (mobile, browser, locked-down and thin clients)
```

Latency is the main cost of remote execution: every operator call is one network round trip (plus one
local hop when the server spawns an executor), and each call is marshalled back onto the client's UI
thread. A script that calls operators once per item over N items therefore costs roughly N round trips,
where a locally evaluated script costs none.

