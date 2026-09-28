# 最小可行性验证报告

执行日期：2026-09-28。验证使用本机现有工具链，未访问真实学习通账号或课程。

| 验证项 | 实际结果 | 能说明什么 |
| --- | --- | --- |
| Node 24.21.0 代理协议测试 | `node --test`：3/3 通过 | 共享代理的事件顺序、去重、停止和两种宿主消息入口可测试 |
| Windows MSVC 14.51 C++ | `/W4 /WX /O2` 编译 `tb_core.dll` 和测试程序；执行输出 `PASS native C ABI state core` | C++ 核心可作为独立 DLL，经 C ABI 调用并保持确定性状态转换 |
| 本机 Edge Chromium 无头模式 | `PASS windows`、`PASS android`（模拟宿主桥） | 同一 JS 包在真实 Chromium 页面、文档早期加载并发现视频节点 |
| 本机真实 WebView2 | SDK `Microsoft.Web.WebView2` 1.0.4258.31；程序输出 `PASS real WebView2 document-start bridge and DOM event`，退出码 0 | Windows WebView2 可在文档创建时注入共享代理，并接收其 `HELLO` / `VIDEO_FOUND` 消息 |

## 本机环境与未验证项

- 已有：Node、Edge、WebView2 Runtime、Visual Studio C++ Build Tools、Windows SDK。
- 未发现：.NET SDK、Android SDK、NDK、Gradle、ADB 或连接的安卓设备。
- **Android 真正的 WebView 注入、JNI `.so` 构建、登录、视频/iframe、退后台/锁屏行为均未验证。** `PASS android` 仅表示共享代理在 Chromium 夹具中通过了模拟 Android 消息入口。
- WebView2 冒烟使用 `NavigateToString` 本地模拟页面，尚未验证真实站点登录和跨域 iframe。
- C++ 测试是正确性与 ABI 验证，没有做性能基准，不能据此声称 C++ 或汇编比现有 JS 更快。

## 下一道验证门槛

在装有 Android Studio/SDK/NDK 的真机或模拟器上，先实现最小 Android 宿主：仅打开测试站点、文档起始注入共享代理、经 `WebViewCompat.addWebMessageListener` 接收两个事件，明确记录 `WebViewFeature` 支持情况。随后再验证目标站点登录、帧、视频及前后台行为。只有双端宿主通过，才开始完整迁移现有功能。

## 本地复现

见 [poc/README.md](poc/README.md)。Windows WebView2 冒烟程序源码是 [poc/windows/windows_smoke.cpp](poc/windows/windows_smoke.cpp)，依赖可从 [官方 NuGet 包](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4258.31) 获取。测试不需要把 SDK 二进制提交进本目录。
