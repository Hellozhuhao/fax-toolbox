# 方案调研与来源

调研日期：2026-09-28。以下依据官方文档和项目官方仓库；技术结论与项目取舍分开记录。

## 现有成熟方案给出的启发

| 方案 | 官方事实 | 本项目取舍 |
| --- | --- | --- |
| VS Code Extension Host | 扩展运行在 Extension Host，隔离对主 UI/启动性能的影响。 | 借鉴独立生命周期、权限 API 与故障隔离；首期不要照搬完整插件市场。 |
| Tauri 2 | 提供桌面和移动端框架、能力与权限配置，UI 使用系统 WebView。 | 可用于通用工具箱外壳，但第三方站点的深度注入、帧控制须先双端实测。 |
| WebView2 | 提供文档创建时注入脚本、帧 API、DevTools Protocol 和独立用户数据目录。 | Windows 首选宿主；登录数据与普通 Edge 配置分离。 |
| Android WebView / AndroidX WebKit | WebView 基于 Chromium；AndroidX WebKit 提供按特性检测的文档起始注入与消息接口。 | 安卓免 root 首选；需处理 API 差异、Activity 重建和后台限制。 |
| Electron | 采用 Chromium 多进程架构。 | 适合纯桌面 JS 团队；不作为 Windows+Android 统一浏览器宿主。 |
| CEF | 官方定位为嵌入 Chromium 的框架。 | 只有平台原生 WebView 无法满足经验证的功能时再考虑维护成本。 |
| Android NDK | 支持 C/C++、CMake 和打包 `.so`。 | 共享 C++ 计算核心可行；不要把 DOM 操作搬进 JNI。 |

## 本项目决策依据

1. **共用的是模块契约与计算核心，浏览器宿主按平台实现。** 这避免把移动端能力建立在桌面专有 API 上。
2. **原生 C++ 只优化可批处理计算。** DOM 操作、网络等待和跨语言消息频率先被测量和减少；JNI 文档也强调避免不必要的跨边界工作。
3. **第三方网页不能获得通用原生桥。** Android 官方明确指出 `addJavascriptInterface` 面对不可信网页的风险；消息必须按来源和能力验证。
4. **移动端前台要求是产品能力声明。** Android 后台执行限制与 WebView 生命周期共同决定可支持的体验。

## 官方资料

- [VS Code Extension Host](https://code.visualstudio.com/api/advanced-topics/extension-host)
- [Tauri 2 总览](https://v2.tauri.app/start/)；[Tauri 能力权限](https://v2.tauri.app/security/capabilities/)
- [WebView2 API 概览](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/overview-features-apis)；[用户数据目录](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder)
- [Android WebView](https://developer.android.com/develop/ui/views/layout/webapps/webview)；[AndroidX WebViewCompat](https://developer.android.com/reference/androidx/webkit/WebViewCompat)
- [Android WebView 原生桥安全风险](https://developer.android.com/privacy-and-security/risks/insecure-webview-native-bridges)；[后台工作限制](https://developer.android.com/develop/background-work/background-tasks/bg-work-restrictions)
- [Android NDK 指南](https://developer.android.com/ndk/guides)；[JNI 性能建议](https://developer.android.com/training/articles/perf-jni)；[Arm Neon](https://developer.android.com/ndk/guides/cpu-arm-neon)
- [Electron Process Model](https://www.electronjs.org/docs/latest/tutorial/process-model)；[CEF 官方仓库](https://github.com/chromiumembedded/cef)

资料支持平台能力，不代表本目录已构建 WebView2/Android 宿主；实际完成范围以 `POC_REPORT.md` 为准。
