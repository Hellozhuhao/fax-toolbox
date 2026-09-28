# Windows 前后端协议 v1

此协议连接 JavaScript 页面、C# WebView2 外壳与 Rust 后端。当前只开放内置 `toolbox.smoke` 模块。所有消息按 UTF-8 编码，字段名区分大小写。

## 边界 1：工具箱 UI → C#

工具箱 UI 运行于 `https://ui.toolbox.invalid/index.html`，由独立 WebView2 加载。它向 `window.chrome.webview.postMessage` 发送 JSON **字符串**：

```json
{"v":1,"id":"ui-request-id","command":"session.start"}
```

`command` 只允许 `session.start` 和 `session.cancel`。C# 检查来源 URL、消息长度（不超过 16 KiB）、版本、请求 ID 与命令，再执行。C# 使用 `PostWebMessageAsJson` 回送状态对象：

```json
{"v":1,"kind":"status","state":"OBSERVING","detail":"已发现视频"}
```

当前 UI 消息只是操作意图；模块 ID 和能力由 C# 根据内置模块清单确定，网页不能自行指定原生能力。

## 边界 2：模块页面 → C#

页面运行于 `https://module.toolbox.invalid/smoke.html`，与工具箱 UI 使用不同的 WebView2 和用户数据目录。C# 在文档创建时注入共享 `page-agent.js`。代理发送 JSON 字符串，沿用已有页面事件信封：

```json
{"protocol":1,"moduleId":"toolbox.smoke","sessionId":"uuid","frameId":"main","generation":1,"seq":2,"type":"VIDEO_FOUND","payload":{"id":"sample-video"}}
```

C# 检查准确的页面 URL、协议版本、模块 ID、会话 ID、代数、主帧标识、递增序号、允许的事件类型和大小上限。不接受页面直接要求文件读写或执行原生命令。每次重新开始都更换 `sessionId` 和注入脚本，旧页面消息失效。

## 边界 3：C# ↔ Rust

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
- 这是第一条可运行纵向链路；正式接入外部网站前需增加模块清单与权限的后端加载、登录会话和帧级来源规则。
