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
与学习通、作业校对分开；展开右侧模块配置后，使用现有状态卡切换。右侧会话列表可切换和删除对话，
顶部“新对话”建立另一个会话。每个会话分别保存项目文件夹、对话、步骤记录与用量，
经当前 Windows 用户加密后保存在 `%LOCALAPPDATA%\FaxToolbox\agent-sessions.dat`，
下次启动继续使用；图片本身不写入会话文件。

输入框支持最多四张图片和粘贴截图，图片先在本机缩放并转为 JPEG，再传给当前 Agent
模型。预设的 `qwen-plus` 是文字模型，因此图片入口在该配置下禁用。
系统提示词按 DeepSeek、阿里百炼、硅基流动、OpenAI 和自定义兼容接口调整输出约束，
要求模型以单步 JSON 表达工具调用；工具输出一律当作不可信数据。

Agent 当前可查看模块状态、打开模块、列出及搜索所选项目中的文件、读取文本、创建文件、
替换唯一的文本片段、列出计划和项目概览，以及调用已注册的模块操作。学习通的开始、暂停、结束和作业校对的
开始操作已注册；新模块可通过 `RegisterAgentModuleAction` 接入同一工作区。Agent 可在所选项目目录内
执行经过确认的 Git 查看、.NET/Rust 构建与测试、Node 语法检查及 pytest 命令。项目路径必须由用户通过
系统文件夹选择器指定。访问限制在该目录下；链接路径、构建目录及常见密钥文件受保护。
模型返回的工具请求在宿主中逐项验证，最多执行八步。创建和修改文件、控制课程任务前，
界面会显示待执行内容并等待用户确认；用户可以停止整个任务。项目命令按固定程序与子命令名单执行，
不通过 shell 拼接或运行任意命令。

右侧模块状态卡选择要配置的模块。余额和连接测试只针对当前选择；模块卡片分别显示
自己的模型配置与运行状态。

## 用量与费用

每次模型响应只读取服务商返回的 `usage`。输入、输出 token 和缓存命中量没有响应字段时
不推算。Agent 在当前会话内累加已测量的请求，费用是基于 token 的估算，不代表服务商账单；
免费额度、优惠、资源包、税费和汇率不计入。工具箱每次启动只在后台查询一次内置模型的官方价格页；
查询失败或页面结构不匹配时使用 2026-10-03 核对的内置价格快照。该查询不使用 API Key，
也不因打开 Agent 标签或调用模型而重复发起。预设模型自动匹配价格，
只有自定义接口在配置中填写每百万 token 单价。内置价格来源：

- [DeepSeek Flash](https://api-docs.deepseek.com/zh-cn/quick_start/pricing/)：按请求开始时的 UTC 星期与时段切换高峰/空闲单价；输入缓存另计。
- [阿里百炼 qwen-plus](https://help.aliyun.com/zh/model-studio/qwen-plus)：预设接口位于北京，按单次输入长度选择非思考模式阶梯价；缓存输入另计。
- [硅基流动 Qwen3.6-27B](https://www.siliconflow.cn/models)：使用页面公开的人民币输入/输出价；页面未给缓存单价，估算时缓存输入按普通输入价计算。
- [OpenAI GPT-4.1 mini](https://developers.openai.com/api/docs/models/gpt-4.1-mini)：使用标准即时请求的美元输入、缓存输入、输出价。

价格可能调整；更新内置型号时要重新核对上述页面。若一次会话有未返回用量的调用，
界面显示的是已测量部分；若某个已测量调用找不到价格或出现币种混合，费用不汇总。
