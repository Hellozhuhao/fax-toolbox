# Toolbox 起步工程

这是面向 **Windows + Android（免 root）** 的工具箱起步代码。当前 Windows 主线采用 C# WPF + WebView2 外壳和 Rust 后端；原有 C++ Windows PoC 与 Android 起步工程保留。Windows 版已有学习通模块的独立页面、课程会话、视频与文档动作、章节答案库、可配置 AI 答题，以及密码箱。

## 目录

| 路径 | 作用 |
| --- | --- |
| `apps/windows-host/` | Win32 + WebView2 桌面宿主，可运行 |
| `apps/windows-shell/` | C# WPF + WebView2 桌面外壳 |
| `apps/rust-backend/` | Rust 独立进程与会话状态机 |
| `modules/chaoxing/` | 学习通页面适配器与本地测试夹具 |
| `apps/android-host/` | Android WebView 宿主与 JNI 工程 |
| `packages/protocol/` | Windows 前后端接口约定 |
| `packages/page-agent/` | 两端共用的文档注入代理 |
| `native/core/` | 两端共用的 C++ 状态核心 |
| `modules/smoke/` | 第一个模块的清单和本地页面 |
| `tests/` | 共享代理与模块清单检查 |
| `poc/` | 上一阶段独立可行性验证 |

模块页面含一个视频节点和“模拟视频结束”按钮。代理发出 `HELLO`、`VIDEO_FOUND`、`VIDEO_ENDED`，Windows 新外壳把事件交给 Rust 状态机，状态依次变为 `READY`、`OBSERVING`、`COMPLETE`。

## Windows 主线：C# + Rust

新增“安卓投屏”内置工作台，支持 USB 设备选择、Android 无线配对与连接、
键鼠控制、声音及应用虚拟显示。首次开发运行前执行
`powershell -ExecutionPolicy Bypass -File scripts/setup-scrcpy.ps1`；
发布脚本自动携带投屏组件。使用步骤和验证边界见
[安卓投屏](modules/android-mirror/README.md)。

在 PowerShell 中进入项目根目录，运行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-windows.ps1 -Test
powershell -ExecutionPolicy Bypass -File scripts/run-windows.ps1
```

`-Test` 同时运行 Rust 单元测试及演示、课程、章节目录、AI 设置、多标签与加载失败重试等隐藏窗口端到端测试。首次构建会下载并校验 WebView2 SDK NuGet 包。需要已安装的 .NET 10 SDK、Rust、Visual Studio Build Tools 和 WebView2 Runtime。浏览器界面、模块页面和 Rust 后端之间的协议见 [WINDOWS_PROTOCOL.md](packages/protocol/WINDOWS_PROTOCOL.md)；代码边界与新模块开发步骤见 [Rust 后端架构](apps/rust-backend/ARCHITECTURE.md)。学习通模块的操作与验证边界见 [模块说明](modules/chaoxing/README.md)。

## Windows 安装包与便携版

安装器由 Inno Setup 7 编译。开发机安装官方 `JRSoftware.InnoSetup.7` 后运行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1 -Test
powershell -ExecutionPolicy Bypass -File scripts/package-windows.ps1 -SkipPublish -Test
```

第一条命令先发布 x64 自包含程序，再生成 `dist/FaxToolbox-Setup-v0.1.0-preview.1-win-x64.exe`，并用独立的测试安装标识完成安装、启动和卸载验证。第二条命令生成 `dist/FaxToolbox-Windows-win-x64.zip`，作为免安装便携版。两种产物都包含 Windows 前端、Rust 后端和运行所需页面资源；不需要另装 .NET 运行时。安装器面向当前用户，无需管理员权限，创建开始菜单入口并提供卸载，桌面快捷方式可选。缺少 WebView2 Runtime 时，安装器使用内置的微软签名引导程序联网补装；便携版需要用户自行安装 WebView2 Runtime。构建脚本在打包前检查发布目录，拒绝加入账号密码、答案缓存、浏览器配置、数据库和调试符号。编译器可用 `-InnoCompiler` 指定，WebView2 引导程序从微软官方下载并验签后缓存到 `.cache/installer-tools/`。

当前 UI 采用紧凑活动栏、模块工作台与顶部快速入口，包含首页、模块列表、演示与学习通工作台、本次运行活动、系统选项和密码箱入口。按 `Ctrl+K` 可打开快速操作，直接切换模块或打开设置。深色/浅色偏好仅保存在工具箱 UI 的本地存储中；活动记录仅保留本次运行。

AI 答题设置可从学习通工作台、系统选项或快速操作打开。支持 DeepSeek、阿里百炼、硅基流动与 OpenAI 预设，选择后只需填写 API Key；自定义选项可填写兼容 Chat Completions 的 HTTPS 完整地址和模型名，不需要密钥的服务可以留空。工作台与系统选项显示已保存的服务商、模型、接口和密钥状态，并提供只发送示例题的“测试连接”。DeepSeek 配置会使用官方只读接口显示 API 余额并支持手动刷新；其他服务商尚无已适配的余额接口，界面会明确提示。是否有免费额度、额度大小与可用模型由服务商决定。没有本章答案或已有答案与页面不匹配时，工具箱会尝试读取当前可见题目并请求模型；模型返回不完整答案、请求超时或页面发生变化时显示原因并允许用户处理。密钥由 Windows 当前用户加密保存；切换到其他服务或模型时不会沿用旧密钥。当前题目文本和选项会发送给配置的服务。验证答案格式可运行 `dotnet run --project tests/ai-answers/AiAnswers.csproj`；本机保存了密钥时，加 `-- --live` 可主动发送一条短测试请求，`-- --live-balance` 可只读验证余额接口。

活动栏按“首页 → 模块 → 工作台 → 活动 → 系统选项”组织；密码箱位于系统选项内。顶部“＋”会新建工具箱首页标签；从首页打开学习通或演示模块会创建独立任务标签，同一模块也可多开。切换标签保留各自的网页和任务状态；关闭标签会结束对应任务，点击窗口关闭按钮则退出全部任务。学习通首次跳转若在空白页被取消，会自动重试一次；仍失败时显示原因和重试按钮。“打开学习通”也可在失败后重新导航。暂停、结束、重新开始及 AI 错误的操作规则见 [学习通工作台状态约定](modules/chaoxing/UX_STATES.md)。

工具箱内的 WebView2 网页会话直接联网，不使用 Windows 系统代理、`HTTPS_PROXY` 或其他代理环境变量；Rust 后端进程也不会继承代理环境变量。当前预览版通过 WebView2 的 `--no-proxy-server` 参数强制直连。如果本机无法直连目标网站，应用会报告加载失败，不会自动切回代理。

## 原有 C++ Windows PoC

需要 Visual Studio Build Tools 的 C++ 桌面工具、Windows SDK 和 WebView2 Runtime。在 **x64 Native Tools Command Prompt** 中进入本目录：

```bat
powershell -ExecutionPolicy Bypass -File scripts\setup-webview2.ps1
apps\windows-host\build.cmd
apps\windows-host\build\toolbox.exe
```

按钮可重新运行演示模块。在页面点“模拟视频结束”后，顶部状态应显示 `COMPLETE`。自动验证：

```bat
apps\windows-host\build\toolbox.exe --smoke
```

若已在本机安装 WebView2 SDK，也可设置 `WEBVIEW2_SDK_ROOT` 指向 NuGet 包解压目录后直接构建。`setup-webview2.ps1` 下载官方 NuGet 包并校验 SHA-256。

## Android 构建

需要 JDK 17、Android SDK 36、SDK Build Tools 36、NDK `28.2.13676358`、CMake 和 Gradle 9.6.0。在 `apps/android-host` 目录执行：

```bat
gradle :app:assembleDebug
```

Gradle wrapper 的版本和下载校验值已记录在 `apps/android-host/gradle/wrapper/gradle-wrapper.properties`；本次未能取得官方 wrapper JAR，因此先使用本机安装的 Gradle 9.6.0。也可在安装 Gradle 后运行 `gradle wrapper --gradle-version 9.6.0` 生成 wrapper，再用 `gradlew.bat` 构建。Android 端使用 `WebViewAssetLoader` 加载本地模块，不需要 root。

## 验证

```bat
node --test tests\starter.test.cjs
```

本机 Windows 程序编译和 `--smoke` 已通过。Android 工程尚未在本机编译或运行，因为目前没有 JDK、SDK、NDK 和 Gradle；见 [STARTER_STATUS.md](STARTER_STATUS.md)。

长期设计见 [ARCHITECTURE.md](ARCHITECTURE.md)，方案调研见 [RESEARCH.md](RESEARCH.md)，上一阶段结果见 [POC_REPORT.md](POC_REPORT.md)。

## Windows 产品与开发环境

- [PRODUCT_DESIGN.md](PRODUCT_DESIGN.md)：产品目标、界面流程、MVP 和阶段验收。
- [TOOLCHAIN.md](TOOLCHAIN.md)：已安装的 Windows 工具链、验证结果和开发环境入口。

