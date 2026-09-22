# 文档索引

[English](index.md) | **中文** | [Español](index.es.md)

> 属于 **ASON** 文档的一部分 —— 返回 [README](../README.zh-CN.md)。

每篇指南都提供英文、中文与西班牙语版本。下表列出全部主题，并给出每篇文档三种语言的对应链接。

| 主题 | 语言版本 | 内容 |
|---|---|---|
| **AI providers** | [English](ai-providers.md) · [中文](ai-providers.zh-CN.md) · [Español](ai-providers.es.md) | 环境变量、DeepSeek 及其他与 OpenAI 兼容的端点、通过 Semantic Kernel 使用 Azure / Ollama / Gemini / Anthropic、故障排查 |
| **编写 operator** | [English](operators.md) · [中文](operators.zh-CN.md) · [Español](operators.es.md) | `OperatorBase`、root operator、`GetViewOperator` / `AttachChildOperator` 生命周期、无状态模块、`[AsonModel]`、`Ason.Abstractions` |
| **客户端配置** | [English](configuration.md) · [中文](configuration.zh-CN.md) · [Español](configuration.es.md) | `AsonClient` 构造函数、完整的 `AsonClientOptions` 参考、`AddAson` 注册、提示词覆盖、日志记录 |
| **执行模式** | [English](execution-modes.md) · [中文](execution-modes.zh-CN.md) · [Español](execution-modes.es.md) | 进程内、外部进程、Docker 与远程执行，以及每种模式背后的安全模型 |
| **架构与代理** | [English](architecture.md) · [中文](architecture.zh-CN.md) · [Español](architecture.es.md) | ASON 如何工作、内部代理，以及与 tool calling / MCP 的详细比较 |
| **贡献指南** | [English](contributing.md) · [中文](contributing.zh-CN.md) · [Español](contributing.es.md) | 构建、测试套件、CI，以及哪些内容需要 Windows 或 Docker |

## 从哪里开始

| 你想…… | 去哪里 |
|---|---|
| 尽快跑起来 | [README 快速开始](../README.zh-CN.md#quick-start) |
| 接入 DeepSeek 或其他 OpenAI 兼容端点 | [AI providers](ai-providers.zh-CN.md) |
| 把自己的方法暴露给 agent | [编写 operator](operators.zh-CN.md) |
| 了解内部代理到底做了什么 | [架构与代理](architecture.zh-CN.md) |
| 构建并测试本仓库 | [贡献指南](contributing.zh-CN.md) |
