# Windows 工具链记录

日期：2026-09-28  
目标：Windows x64；产品设计见 `PRODUCT_DESIGN.md`。

## 已安装并验证

| 工具 | 版本与位置 | 验证 |
| --- | --- | --- |
| .NET SDK | 10.0.401，`%LOCALAPPDATA%\Microsoft\dotnet` | `dotnet new wpf` 与 Release 构建成功，0 警告/0 错误 |
| Rust | stable 1.98.1，`%USERPROFILE%\.cargo\bin`，MSVC 目标 | `rustc` 编译并运行最小程序，输出 `RUST_SMOKE_OK` |
| Go | 1.27.1，`%LOCALAPPDATA%\Programs\Toolbox\Go-1.27.1\go\bin` | `go build` 编译并运行最小程序，输出 `GO_SMOKE_OK` |
| C++ | Visual Studio Build Tools 18，Windows SDK | 已有 WebView2/C++ 程序编译并通过 `--smoke` |
| WebView2 | Runtime 和 SDK 均可用 | 页面代理到 C++ 状态机的 Windows 测试通过 |

安装采用当前用户目录，没有覆盖系统自带的旧 .NET 运行时。普通命令行可能先找到 `C:\Program Files\dotnet\dotnet.exe`，所以开发时在 PowerShell **点源**项目脚本：

```powershell
. C:\Users\21588\Desktop\toolbox\scripts\Enter-ToolboxDev.ps1
dotnet --list-sdks
rustc --version
go version
cl
```

脚本只调整当前 PowerShell 进程的环境变量，也会导入 Visual Studio x64 编译环境。新终端需再次点源。使用完整路径调用工具也有效。

## 下载与完整性

- .NET：从微软官方 `dotnet-install.ps1` 安装固定 SDK `10.0.401`。
- Go：官方 `go1.27.1.windows-amd64.zip`，SHA-256 `a3911b5e0e1b1053f25ed0675f4c1c6aad1e2bfcf253df2b9be4caabd2edd95d`，已在解压前校验。
- Rust：官方下载 `rustup-init.exe`，SHA-256 `6f4bef66261261fcb43131be8720bab817d403a09edec7455c371974b90bdb7e`，与同目录官方 `.sha256` 一致；安装 stable MSVC minimal profile。

官方来源：[.NET 下载](https://dotnet.microsoft.com/en-us/download)、[Go 下载与校验值](https://go.dev/dl/)、[Rust 安装](https://rust-lang.org/tools/install/)。

## 后续构建入口

W1 阶段将使用 .NET 10 WPF + WebView2 创建 C# Windows 宿主；Rust 进程负责模块与任务状态；已有 C++ 演示保持为纵向验证。Go 暂作为独立工具链保留，首个工作进程的职责在出现具体需求后确定。
