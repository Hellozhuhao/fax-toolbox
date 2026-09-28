# 最小可行性验证

此 PoC 刻意不连接学习通账号，不调用外部服务。它验证两件可在当前电脑上运行的事：

1. 同一份页面代理在真实 Chromium 中加载、发现 DOM 视频、发送版本化消息；用两种模拟宿主桥运行。
2. 一个无 WebView 依赖的 C++ 核心通过稳定 C ABI 接收事件、去重和转移状态，Windows MSVC 可编译并执行测试。

## 运行

```powershell
node --test .\poc\tests\agent.test.cjs
```

浏览器夹具使用 `poc/browser/fixture.html?host=windows` 与 `?host=android`。打开后页面会显示 `PASS` 或错误。Windows 可用 Edge 无头模式运行并检查 `id="proof"` 内容；该验证是**真实 Chromium + 模拟宿主桥**，不是 WebView2/Android WebView 原生集成。

Windows C++：在 Visual Studio Build Tools 开发者命令提示符运行：

```cmd
cd poc\native
build.cmd
```

`build.cmd` 生成 `build\tb_core.dll` 和 `build\native_smoke.exe`，并让测试程序跨 DLL ABI 调用原生核心。若要避免复制构建产物，可以在交付前清理 `build` 目录。

真正的 Windows WebView2 宿主验证源码位于 `poc/windows`。先从官方 NuGet 获取 `Microsoft.Web.WebView2` 1.0.4258.31，解压后设置 `WEBVIEW2_SDK_ROOT`，再在 VS 开发者命令提示符执行 `poc\windows\build.cmd`。运行生成的 `windows_smoke.exe` 时传入页面代理路径与独立 Profile 路径。该程序在离屏窗口创建 WebView2、文档开始时注入代理，并验证 `HELLO` 与 `VIDEO_FOUND` 消息。

## 文件

- `shared/page-agent.js`：不依赖油猴 API 的最小页面代理。
- `browser/fixture.html`：真实 Chromium 夹具，模拟桌面/安卓消息桥。
- `tests/agent.test.cjs`：协议、DOM 事件与停止行为测试。
- `native/tb_core.h`、`tb_core.cpp`：可移植 C ABI 状态核心。
- `native/native_smoke.cpp`：原生核心正确性验证。
- `windows/windows_smoke.cpp`：真实 WebView2 文档起始注入与消息桥验证。

安卓 SDK、模拟器和真机在当前电脑上均未发现，因此这里不把安卓原生注入、登录或后台运行标为已验证。
