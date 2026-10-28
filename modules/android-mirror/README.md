# 安卓投屏

工具箱首页和工具目录中的“安卓投屏”打开内置 WPF 工作台。
scrcpy 的原生视频窗口嵌入工作台，保持其解码、音频和键鼠控制；
模块自行管理设备选择、无线配对、进程启动、缩放和关闭。
这是基于开源 scrcpy 的独立实现，不包含 Android DEX 专有代码。

## 使用

1. USB：手机开启开发者选项和 USB 调试，连接数据线并允许调试授权。
2. 打开模块，刷新设备，选择手机，点击“开始投屏”。
3. 无线：Android 11+ 开启无线调试，在“使用配对码配对设备”页面
   填写配对 IP:端口及六位配对码；配对完成后，填写无线调试主页显示的
   连接 IP:端口并连接。两个端口通常不同。
4. 应用窗口：填写完整包名（如 `com.android.settings`），点击“打开应用”。
   使用虚拟显示，需 Android 10+，应用和厂商系统可能限制独立显示。
   每个工作台同时运行一个投屏会话；切换模式会结束旧会话。
5. 点击投屏画面使用键鼠。Alt+H 回桌面、Alt+B 返回。关闭模块或工具箱
   会结束该模块拥有的 scrcpy 进程，不关闭其他程序的 ADB 服务。

声音转发需要 Android 11+。关闭手机屏幕仅关闭实体屏幕，不停止投屏。
无线失败时可先用 USB 确认授权和设备兼容性。

## 开发与验证

运行 `powershell -ExecutionPolicy Bypass -File scripts/setup-scrcpy.ps1` 下载
固定版本 scrcpy 5.0 官方 Windows x64 包并校验官方 SHA-256。
开发环境放在 `runtime/scrcpy`（Git 忽略）；发布脚本自动准备并携带组件。
ADB 密钥和配置由 Windows ADB 保存在当前用户个人目录的 `.android`，
不会包含在发布包中。配对码不持久化。

```
dotnet run --project tests/android-mirror/AndroidMirror.Tests.csproj -- <工具箱绝对路径>
```

测试覆盖输入验证、设备解析、原生窗口嵌入与缩放、界面渲染和真实 ADB
设备扫描。无手机环境无法验证真机画面、声音、键鼠和无线配对。

参考：https://github.com/Genymobile/scrcpy
构建说明：https://github.com/Genymobile/scrcpy/tree/v5.0/doc
