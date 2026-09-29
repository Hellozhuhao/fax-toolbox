# Toolbox 起步工程

这是面向 **Windows + Android（免 root）** 的工具箱起步代码。当前 Windows 主线采用 C# WPF + WebView2 外壳和 Rust 后端；原有 C++ Windows PoC 与 Android 起步工程保留。Windows 版已有学习通模块的独立页面、课程会话、视频与文档动作、章节答案库，以及密码箱。真实账号课程尚待人工登录验证。

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

在 PowerShell 中进入项目根目录，运行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-windows.ps1 -Test
powershell -ExecutionPolicy Bypass -File scripts/run-windows.ps1
```

`-Test` 同时运行 Rust 单元测试，以及原演示、测验、视频、嵌套文档四条隐藏窗口端到端冒烟测试。首次构建会下载并校验 WebView2 SDK NuGet 包。需要已安装的 .NET 10 SDK、Rust、Visual Studio Build Tools 和 WebView2 Runtime。浏览器界面、模块页面和 Rust 后端之间的协议见 [WINDOWS_PROTOCOL.md](packages/protocol/WINDOWS_PROTOCOL.md)。学习通模块的操作与验证边界见 [模块说明](modules/chaoxing/README.md)。

## Windows EXE 发布

开发完成后运行 `powershell -ExecutionPolicy Bypass -File scripts/publish-windows.ps1 -Test`。它生成 `dist/windows/FaxToolbox.Windows.exe` 和同目录下必需的 Rust 后端、WebView2 页面资源；直接双击 EXE 即可启动，不需要先运行 PowerShell 或安装 .NET 10 运行时。发布时保留整个 `dist/windows` 目录。Windows 仍需安装 WebView2 Runtime。

当前 UI 包含全宽首页、模块列表、演示与学习通工作台、本次运行活动、系统选项和密码箱入口。深色/浅色偏好仅保存在工具箱 UI 的本地存储中；活动记录仅保留本次运行。

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

