# 执行模式与远程执行

[English](execution-modes.md) | **中文** | [Español](execution-modes.es.md)

> 本文档是 **ASON** 文档的一部分 —— 返回 [README](../README.zh-CN.md)。

## 执行模式 / 环境

- **In-process（进程内）** —— 脚本在你的应用程序所在的同一进程中运行。  
  适合开发和测试，但由于隔离能力有限，不建议用于生产环境。  
  ASON 包含静态分析，用于阻止不安全的操作（反射、文件 I/O、网络等）。

- **External process（外部进程）** —— 脚本在单独的进程中运行，提供额外一层保护。  
  添加 `Ason.ExternalExecutor` NuGet 包即可启用此模式。

- **Docker** —— 脚本在完全隔离的容器内执行，以获得最高级别的安全性。  
  需要在本地安装 Docker。  
  运行应用前请先拉取所需的容器：

  > docker pull ghcr.io/alexgoon/ason:0.8.1

- **Remote server（远程服务器）** —— 脚本在远程服务器上执行，可能位于外部进程或 Docker 容器中。  
  配置细节见下一节。
  
![ASON 执行环境](../images/execution-environments.jpg)


## 选择模式

| 场景 | 建议模式 |
|---|---|
| 本地开发与测试，以及必须访问 UI 状态的脚本 | **In-process** |
| 不应在自身进程中运行生成代码的桌面应用 | **External process** |
| 不可信输入，或需要最高级别隔离时 | **Docker** |
| 移动客户端，或没有 Docker 的机器 | **Remote server** |

## 远程执行

你可以在 **远程服务器** 上运行 ASON 脚本，这对于移动客户端，或者用户机器上无法使用 Docker 的情况特别有用。

要启用远程执行：

1. 创建一个标准的 **ASP.NET Core Web API** 项目，并安装 `Ason.RemoteBridge` NuGet 包。  
2. 在 `Program.cs` 中注册脚本运行器：

```csharp
builder.Services.AddAsonScriptRunner();  
app.MapAson("/scriptRunnerHub", requireAuthorization: false);
```

3. 在客户端应用程序中，于 `AsonClientOptions` 中启用远程运行器：

```csharp
AsonClientOptions options = new() {  
    ExecutionMode = ExecutionMode.ExternalProcess,  
    RemoteRunnerBaseUrl = "http://localhost:5222",  
    UseRemoteRunner = true,  
};
```

演示此配置的示例项目包含在 **MAUI Project Template** 中。

