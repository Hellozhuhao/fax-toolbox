# AI 模块与 Agent 接口

AI 服务接入 [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) 的
`ChatClientAgent`（MIT，NuGet `Microsoft.Agents.AI` 1.20.0）。工具箱通过
`AiAgentService` 实现 `IChatClient` 适配，沿用现有兼容 Chat Completions 的 HTTPS
传输、请求超时、图片输入和结果校验。学习通和作业校对的模型调用不共享对话。

## 模块边界

- `AiConsumerRegistry` 注册稳定的模块 ID、显示名和允许的操作。内置 `agent/chat`、
  `course/answer` 及 `homework/review|explain`；未来模块用 `Register` 接入。
- 每个模块的模型、接口和 Windows 用户加密的 API Key 存在
  `%LOCALAPPDATA%\FaxToolbox\ai\<consumer>.json`。旧版 `ai-settings.json` 只作为
  尚未分别保存配置时的兼容默认值；新设置不会覆盖其他模块。
- `AiModuleRuntime.RunAsync` 为每个模块分配单独的请求序号，Rust `ai` 命名空间按
  `scopeId + consumer` 管理请求租约。新模块只需注册操作并从自己的工作台调用
  `RunAsync`。提示词、密钥和回答不经过 Rust 管道。
- 新模块调用 `AiAgentService.CompleteAsync(consumer, settings, instructions, prompt, ...)`
  获取文本或图片模型响应，并自行验证业务输出。

## Agent 工作区

AI 页面包含独立的 Agent 对话与步骤记录。Agent 的模型配置属于 `agent` consumer，
与学习通、作业校对分开；在右侧现有模块状态卡上选择要管理的配置。Agent 对话保留在
当前任务窗口内，关闭窗口后清除。

Agent 当前可查看模块状态、打开模块、列出及搜索所选项目中的文件、读取文本、创建文件、
替换唯一的文本片段，以及调用已注册的模块操作。学习通的开始、暂停、结束和作业校对的
开始操作已注册；新模块可通过 `RegisterAgentModuleAction` 接入同一工作区。项目路径必须由用户通过
系统文件夹选择器指定。访问限制在该目录下；链接路径、构建目录及常见密钥文件受保护。
模型返回的工具请求在宿主中逐项验证，最多执行八步。创建和修改文件、控制课程任务前，
界面会显示待执行内容并等待用户确认；用户可以停止整个任务。Agent 不运行任意终端命令。

右侧模块状态卡选择要配置的模块。余额和连接测试只针对当前选择；模块卡片分别显示
自己的模型配置与运行状态。
