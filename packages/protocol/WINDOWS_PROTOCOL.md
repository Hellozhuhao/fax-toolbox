# Windows 前后端协议 v1

此协议连接 JavaScript 页面、C# WebView2 外壳与 Rust 后端。已接入内置 `toolbox.smoke` 和 `toolbox.chaoxing` 模块。所有消息按 UTF-8 编码，字段名区分大小写。

## 边界 1：工具箱 UI → C#

工具箱 UI 运行于 `https://ui.toolbox.invalid/index.html`，由独立 WebView2 加载。它向 `window.chrome.webview.postMessage` 发送 JSON **字符串**：

```json
{"v":1,"id":"ui-request-id","command":"session.start"}
```

`command` 由 C# 明确列举；演示模块允许 `demo.activate`、`session.start/cancel`，学习模块允许 `course.activate/open/start/pause/stop/jump`、`answers.save`，密码箱允许 `vault.list/save/delete`。`ai.configure` 只打开原生设置窗口；`ai.get-config` 返回不含密钥的配置摘要，`ai.test` 发起用户点击触发的短连接测试。C# 检查来源 URL、消息长度（不超过 16 KiB）、版本、请求 ID 与命令，再执行。C# 使用 `PostWebMessageAsJson` 回送状态对象：

```json
{"v":1,"kind":"status","state":"OBSERVING","detail":"已发现视频"}
```

UI 消息只是操作意图；模块 ID 和能力由 C# 确定，网页不能自行指定原生能力。密码不会进入此消息桥。

## 边界 2：模块页面 → C#

页面运行于 `https://module.toolbox.invalid/smoke.html`，与工具箱 UI 使用不同的 WebView2 和用户数据目录。C# 在文档创建时注入共享 `page-agent.js`。代理发送 JSON 字符串，沿用已有页面事件信封：

```json
{"protocol":1,"moduleId":"toolbox.smoke","sessionId":"uuid","frameId":"main","generation":1,"seq":2,"type":"VIDEO_FOUND","payload":{"id":"sample-video"}}
```

C# 检查准确的页面 URL、协议版本、模块 ID、会话 ID、代数、主帧标识、递增序号、允许的事件类型和大小上限。不接受页面直接要求文件读写或执行原生命令。每次重新开始都更换 `sessionId` 和注入脚本，旧页面消息失效。

## 边界 3：C# ↔ Rust

Rust 将协议解析与命令路由分开。`session.*` 归演示模块，`course.*`、`answers.*`、`catalog.*` 归学习通模块，`vault.*` 归密码箱服务。每个模块仅管理自己的状态与数据；分派表及扩展步骤见 [Rust 后端架构](../../apps/rust-backend/ARCHITECTURE.md)。后端在读取阶段限制单行大小；超长行被丢弃后，下一条请求仍可继续处理。

C# 启动一个 Rust 子进程，使用重定向的标准输入/输出进行一行一个 JSON 对象的请求/响应通信（NDJSON）。Rust 的标准输出只包含协议响应；诊断写标准错误。单行不超过 32 KiB。当前同一连接一次只发一个请求；请求 ID 仍用于核对响应，后续可以扩展为多路复用。

请求：

```json
{"v":1,"id":"uuid","method":"session.event","params":{"moduleId":"toolbox.smoke","sessionId":"uuid","generation":1,"frameId":"main","seq":2,"event":"VIDEO_FOUND"}}
```

成功响应：

```json
{"v":1,"id":"uuid","ok":true,"result":{"state":"OBSERVING","lastSeq":2,"generation":1}}
```

失败响应：

```json
{"v":1,"id":"uuid","ok":false,"error":{"code":"INVALID_STATE","message":"event is not valid in current state"}}
```

| 方法 | 必要参数 | 作用 |
| --- | --- | --- |
| `system.ping` | 空对象 | 检查后端与协议版本 |
| `session.open` | `moduleId, sessionId, generation` | 创建会话，返回 `CREATED` |
| `session.event` | 上述字段及 `frameId, seq, event` | 推进状态机 |
| `session.state` | 会话字段 | 查询当前状态 |
| `session.cancel` | 会话字段 | 将活动会话置为 `CANCELLED` |

事件只允许 `HELLO`、`VIDEO_FOUND`、`VIDEO_ENDED`。状态转换：`CREATED → READY → OBSERVING → COMPLETE`；活动状态可取消。错误码包括 `BAD_JSON`、`BAD_REQUEST`、`BAD_VERSION`、`TOO_LARGE`、`MODULE_NOT_FOUND`、`NOT_FOUND`、`STALE_SESSION`、`DUPLICATE`、`INVALID_STATE`、`UNKNOWN_METHOD`、`ALREADY_OPEN`、`BAD_FRAME`。错误响应不改变状态。

## 生命周期与限制

- C# 启动时先 `system.ping`，再允许用户开始模块。后端退出或通信超时要在界面明确报错。
- C# 关闭时终止子进程；每个请求有截止时间。当前无自动恢复运行中的会话。
- 页面代理的 `payload` 仅供诊断；Rust v1 状态机只接受通过校验的事件类型与序号。
- 外部站点只通过模块 WebView2 打开；非学习通来源不能向 Rust 提交课程观察。首次登录由用户自行完成。

## 学习通模块消息

工具箱导航选择“学习通刷课”后，C# 在专属 WebView2 中打开 `https://i.chaoxing.com/`。用户进入包含 `/mycourse/studentstudy` 的课程学习页，点击“开始自动完成”才创建 Rust 会话。页面适配器只读取 DOM、上报观察和执行 Rust 返回的限定动作；它没有密码箱或通用文件访问能力。

| UI → C# 命令 | 作用 |
| --- | --- |
| `course.open` | 打开学习通主页，等待用户登录选课 |
| `course.activate` | 显示已打开的学习通网页，不重新导航；首次进入时打开主页 |
| `course.start` | 校验当前课程 URL，创建或恢复会话并启动适配器 |
| `course.pause` / `course.stop` | 暂停决策或停止适配器 |
| `answers.save` | 保存章节编号与逐题字母答案 |

页面观察格式为 `{v:1,kind:"course.observe",sessionId,generation,seq,observation,chapter}`。C# 校验 HTTPS 学习通来源、当前会话、代数及 16 KiB 上限，再给 Rust 提交 `course.observe`。Rust 根据状态和观察返回 `PLAY_VIDEO`、`ADVANCE_AFTER_VIDEO`、`NEXT_UNIT`、`DOCUMENT_NEXT`、`SWITCH_VIDEO_TAB`、`DISMISS_PROMPT`、`FILL_QUIZ` 或 `NEEDS_ANSWERS`。`ADVANCE_AFTER_VIDEO` 先检查同章测验页签，没有时才进入下一节。答题提交依次上报 `QUIZ_CONFIRM_PENDING` 和 `QUIZ_SUBMITTED`；十秒内无法确认时上报 `QUIZ_SUBMIT_UNCONFIRMED`，后端转为 `NEEDS_INPUT`。C# 只把动作发回对应 WebView2 页面或 frame。Rust 还支持 `course.open`、`course.command`、`course.state`、`answers.put`、`answers.list`。未知测验或题目结构不匹配时，状态变为 `NEEDS_INPUT`，等待人工处理。

## 密码箱边界

“系统选项 · 密码箱”的账号和密码由 WPF 原生输入框接收，再以 Windows DPAPI `CurrentUser` 加密。C# 向 Rust 的 `vault.put` 仅发送 `encryptedUsername`、`ciphertext` 和随机条目 ID；Rust 将密文写入当前用户的 `LocalAppData/FaxToolbox/vault.json`。`vault.list` 只返回账号密文和 ID，C# 解密账号名后向工具箱 UI 显示；密码密文不进入 UI WebView2。`vault.get` 仅供原生宿主未来的凭据操作使用，当前 UI 不开放。

登录页的密码不会被网页注入代码截取。用户若希望保存凭据，需要在密码箱原生窗口主动输入一次。WebView2 登录 Cookie 保留在学习模块的独立用户数据目录。密码箱密文与 Cookie 是两份不同的数据。Windows 用户配置文件丢失或迁移到别的账号时，DPAPI 密文无法直接解密。
