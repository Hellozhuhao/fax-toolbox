# 起步工程验证记录

## 已完成

- Windows 宿主实际用 WebView2 打开 `modules/smoke/smoke.html`，在文档起始阶段注入 `packages/page-agent/page-agent.js`。
- 宿主校验本地页面来源和消息字段，再将事件送入共享 C++ 核心。
- Android 工程复用同一代理、模块页面和 C++ 核心；使用 `WebViewAssetLoader`、来源限定的 `WebViewCompat` 消息桥及 JNI。
- `node --test tests/starter.test.cjs` 检查模块清单和两种宿主传输。

## 实机结果

- Windows：MSVC `/W4 /WX` 编译成功，WebView2 `--smoke` 经页面按钮事件让 C++ 核心进入 `COMPLETE`，输出 `PASS Windows WebView2 -> shared agent -> C++ core COMPLETE`。
- Android：仅静态检查；本机缺少 JDK、Android SDK/NDK、Gradle，尚无 APK 和设备运行结果。
- Gradle wrapper：下载官方文件时自动批准服务连接中断，未取得 wrapper JAR；仓库保留版本和 SHA-256 配置，使用系统 Gradle 9.6.0 构建。

## 后续工程化工作

当前 Windows 消息字段采用面向本地演示模块的最小解析逻辑。接入不受信任网页前，要换成完整 JSON 解析、每次导航独立的会话标识和模块级权限验证。当前页面也只是本地测试夹具，正式模块需要独立生命周期、配置持久化和升级机制。
