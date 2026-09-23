# 文档索引

[English](index.md) | **中文** | [Español](index.es.md)

> 属于 **ASON** 文档的一部分 —— 返回 [README](../README.zh-CN.md)。

每篇指南都提供英文、中文与西班牙语版本。下表列出全部主题，并给出每篇文档三种语言的对应链接。

| 主题 | 语言版本 | 内容 |
|---|---|---|
| **AI providers** | [English](ai-providers.md) · [中文](ai-providers.zh-CN.md) · [Español](ai-providers.es.md) | 环境变量、DeepSeek 及其他与 OpenAI 兼容的端点、通过 Semantic Kernel 使用 Azure / Ollama / Gemini / Anthropic、故障排查 |
| **编写 operator** | [English](operators.md) · [中文](operators.zh-CN.md) · [Español](operators.es.md) | `OperatorBase`、root operator、`GetViewOperator` / `AttachChildOperator` 生命周期、无状态模块、`[AsonModel]`、`Ason.Abstractions` |
| **客户端配置** | [English](configuration.md) · [中文](configuration.zh-CN.md) · [Español](configuration.es.md) | `AsonClient` 构造函数、完整的 `AsonClientOptions` 参考、`AddAson` 注册、提示词覆盖、日志记录 |
| **执行模式** | [English](execution-modes.md) · [中文](execution-modes.zh-CN.md) · [Español](execution-modes.es.md) | 进程内、外部进程、Docker 与远程执行，五种部署组合，以及各种应用形态该选哪种配置 |
| **应用 / Agent 分离** | [English](app-agent-separation.md) · [中文](app-agent-separation.zh-CN.md) · [Español](app-agent-separation.es.md) | 把“只含 `[Ason*]` operator 与桥”的应用与驱动它的 Agent 分开：清单、脚本与单函数接口、gRPC/MCP/HTTP-OpenAPI 适配器、执行位置、安全，以及**不含模型**的普通程序如何驱动同一批端点 |
| **架构与代理** | [English](architecture.md) · [中文](architecture.zh-CN.md) · [Español](architecture.es.md) | ASON 如何工作、部署拓扑与进程边界、凭据边界、内部代理，以及与 tool calling / MCP 的详细比较 |
| **贡献指南** | [English](contributing.md) · [中文](contributing.zh-CN.md) · [Español](contributing.es.md) | 构建、测试套件、CI，以及哪些内容需要 Windows 或 Docker |

## 从哪里开始

| 你想…… | 去哪里 |
|---|---|
| 尽快跑起来 | [README 快速开始](../README.zh-CN.md#quick-start) |
| 接入 DeepSeek 或其他 OpenAI 兼容端点 | [AI providers](ai-providers.zh-CN.md) |
| 把自己的方法暴露给 agent | [编写 operator](operators.zh-CN.md) |
| 了解内部代理到底做了什么 | [架构与代理](architecture.zh-CN.md) |
| 构建并测试本仓库 | [贡献指南](contributing.zh-CN.md) |
| 为我的应用选择执行模式与部署方式 | [执行模式](execution-modes.zh-CN.md) |
| 让测试、CI 任务或另一个程序驱动正在运行的应用（全程不含模型） | [应用 / Agent 分离](app-agent-separation.zh-CN.md#不用-agent外部程序直接驱动应用) |
| 把每一种形态（不分离或分离）都跑起来看效果 | [示例与运行方式](app-agent-separation.zh-CN.md#示例与运行方式) |
