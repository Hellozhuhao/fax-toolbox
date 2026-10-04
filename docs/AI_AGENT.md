# AI 模块与 Agent 接口

AI 服务接入 [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) 的
`ChatClientAgent`（MIT，NuGet `Microsoft.Agents.AI` 1.20.0）。工具箱通过
`AiAgentService` 实现 `IChatClient` 适配，沿用现有兼容 Chat Completions 的 HTTPS
传输、请求超时、图片输入和结果校验。学习通和作业校对的模型调用不共享对话。
本地 Ollama 连接只允许通过回环 IP 的 `11434` 端口使用 HTTP。

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
与学习通、作业校对分开。会话、模型连接、提示词、记忆池、模块配置和用量从左侧图标列进入。
会话列表可切换、置顶、归档和删除对话，顶部“新对话”建立另一个会话。
每个会话分别保存 Chat/Code 模式、推理档位、项目文件夹、对话、步骤记录与用量，
经当前 Windows 用户加密后保存在 `%LOCALAPPDATA%\FaxToolbox\agent-sessions.dat`，
下次启动继续使用；图片本身不写入会话文件。

输入框支持最多四张图片和粘贴截图，图片先在本机缩放并转为 JPEG，再传给当前 Agent
模型。旧预设 `qwen-plus` 和本地 Huihui Qwen3 8B 是文字模型，因此图片入口在这些配置下禁用。
Chat 模式直接输出自然语言文本，不附带工具；Code 模式使用兼容 Chat Completions 的原生
`tools`/`tool_calls` 接口，最终回答也是自然语言文本。只有学习通答题和作业校对需要程序读取的
严格 JSON。工具输出一律当作不可信数据。

系统提示词由通用约束、模型适配、Chat/Code 功能、推理档位和写作任务部分组成。
左侧“提示词”可修改当前模型与模式的附加系统提示词；学习通和作业校对的附加提示词在各自
模型配置中编辑。自定义提示词按接口、模型和功能分别加密保存于
`%LOCALAPPDATA%\FaxToolbox\agent-prompts.dat`。会话背景由用户设置，随该会话保存。
快速、标准、深入档位可在输入框旁切换；本地 Ollama 和阿里百炼 Qwen 3.8
会把档位映射为接口支持的 `reasoning_effort` 值。Qwen 3.8 分别使用 none、low、medium，
避免模型默认的高思考开销。模型是否实际进行推理取决于所选模型。

## 国内云端模型

在 Agent 的“管理模型连接”中选择“阿里百炼”，默认使用北京地域接口的 Qwen 3.8 Flash。
输入框旁的模型列表会直接显示国内云端推荐入口；模型配置窗口按国内云端、海外云端、
本地模型和自定义分类，选中百炼后立即显示可选型号。
同一密钥可切换 Qwen 3.8 Max 处理要求更高的任务；原有 `qwen-plus` 配置仍可使用。
Flash 按 2026-10-04 的北京地域公开原价，每百万 token 输入 ¥0.8、输出 ¥2.7；
Max 为 ¥12、¥36。云端模型仍会遵守服务商的内容规则，不能承诺完全不限制回答。

## Venice 云端模型

在 Agent 的“管理模型连接”中选择 Venice，选用“Qwen 3.6 Plus”（中文写作）或
“Venice Uncensored 1.2”（自由创作），填写 Venice API Key 后保存。输入框旁的模型面板可在
两者之间切换，沿用同一个密钥；“测试连接”会调用所选模型。密钥仍由当前 Windows 用户加密
保存在本机。Chat 使用纯文本回复，Code 可使用通用 Agent 工具；项目写入和模块操作仍需确认。
Venice 对合法创作的拒答较少，但云端服务仍受其条款约束，模型效果需要用实际题材检验。

对话接近模型上下文上限的约 80%，或即将超过实际发送预算时，Agent 会把较旧对话分段整理到
可编辑的记忆池，并保留最近四条消息。记忆整理会消耗模型 token；原始会话记录仍保留在本地。
记忆摘要是压缩结果，重要细节可能需要从原始记录重新查阅。

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

## 本地中文模型

安装 [Ollama for Windows](https://ollama.com/download/windows)，再运行
`ollama pull huihui_ai/qwen3-abliterated:8b-v2`。在工具箱 Agent 的现有模型配置中选择
“本地模型”；无需 API Key。工具箱使用本机 `127.0.0.1:11434`，不会把对话发到云端。
同一配置中的模型可从本机已下载的 Ollama 模型列表选择，也可手填模型名称。
Agent 输入框旁也提供已安装模型的快速切换。
该模型是约 5 GB 的 Q4_K_M 文字模型，适合 8 GB 显存下的中文对话。
如果需要更长的对话上下文，在仓库根目录运行
`ollama create fax-toolbox-qwen3:8k -f models/ollama/Huihui-Qwen3-8B-v2-8k.Modelfile`，
然后把 Agent 模型名称改为 `fax-toolbox-qwen3:8k`。此配置将上下文从 4096 提升至 8192；
在当前 8 GB 显存电脑上验证为 100% GPU 运行。Agent 会优先保留当前请求、最近的工具结果
和近期对话，并把较旧消息压缩到记忆池。写作能力仍取决于模型本身；较长或要求较高的中文
写作任务可以在列表中选择更强的已安装模型，并使用“深入”档。
其去拒答训练旨在减少拒答，但无法保证任何话题都绝不拒答或输出正确。
项目文件写入和模块操作仍须用户逐项确认。

## 用量与费用

每次模型响应只读取服务商返回的 `usage`。输入、输出 token 和缓存命中量没有响应字段时
不推算。Agent 在当前会话内累加已测量的请求，费用是基于 token 的估算，不代表服务商账单；
免费额度、优惠、资源包、税费和汇率不计入。工具箱每次启动只在后台查询一次内置模型的官方价格页；
查询失败或页面结构不匹配时使用 2026-10-04 核对的内置价格快照。该查询不使用 API Key，
也不因打开 Agent 标签或调用模型而重复发起。预设模型自动匹配价格，
只有自定义接口在配置中填写每百万 token 单价。内置价格来源：

- 本地 Ollama：没有按 token 向模型服务商付费的费用，估算单价为零；电费和设备成本不计入。

- [DeepSeek Flash](https://api-docs.deepseek.com/zh-cn/quick_start/pricing/)：按请求开始时的 UTC 星期与时段切换高峰/空闲单价；输入缓存另计。
- [阿里百炼 qwen-plus](https://help.aliyun.com/zh/model-studio/qwen-plus)：预设接口位于北京，按单次输入长度选择非思考模式阶梯价；缓存输入另计。
- [阿里百炼 Qwen 3.8 Flash](https://help.aliyun.com/zh/model-studio/qwen3-8-flash) 与 [Qwen 3.8 Max](https://help.aliyun.com/zh/model-studio/qwen3-8-max)：使用北京地域公开的人民币输入、缓存输入、输出价。
- [硅基流动 Qwen3.6-27B](https://www.siliconflow.cn/models)：使用页面公开的人民币输入/输出价；页面未给缓存单价，估算时缓存输入按普通输入价计算。
- [OpenAI GPT-4.1 mini](https://developers.openai.com/api/docs/models/gpt-4.1-mini)：使用标准即时请求的美元输入、缓存输入、输出价。
- [Venice Qwen 3.6 Plus](https://venice.ai/models/qwen-3-6-plus) 与 [Venice Uncensored 1.2](https://venice.ai/models/venice-uncensored-1-2)：按各自官方模型页的美元单价估算。若 Uncensored 1.2 未列缓存输入单价，按普通输入价计算。

价格可能调整；更新内置型号时要重新核对上述页面。若一次会话有未返回用量的调用，
界面显示的是已测量部分；若某个已测量调用找不到价格或出现币种混合，费用不汇总。
