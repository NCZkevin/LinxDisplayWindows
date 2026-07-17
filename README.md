# Codex 屏显 Windows 版

这是 macOS 版 CodexLinxDisplay 的原生 Windows 实现。它常驻系统托盘，从本机 Codex 读取用量，生成固定为 `142 × 428` 的 JPEG，并通过局域网推送到 Linx68 键盘。

## 功能

- 读取 Codex 本周/当前周期剩余用量、可用重置次数和重置时间。
- 按 1、5、10 或 30 分钟自动刷新；内容没有变化时不重复推送。
- 支持自定义图片，自动居中裁切，并为键盘自身的天气、Wi-Fi、电量状态栏保留顶部安全区。
- 可调整顶部安全区（44–80px）和 JPEG 质量（50%–100%）。
- 关闭窗口后继续在系统托盘运行，支持登录 Windows 时自动启动。
- 卡片明确区分“当前时间”和“下次重置”，当前时间每分钟自动推送，不会额外启动 Codex 进程。
- 不需要键盘厂商 SDK，也不需要云端中转服务。

## 运行

安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 后，在仓库根目录运行：

```powershell
dotnet run --project .\src\CodexLinxDisplay.Windows\CodexLinxDisplay.Windows.csproj
```

在“图像 API”中填写键盘当前地址，例如：

```text
http://192.168.31.71/image/upload
```

程序会发送一个请求体为原始 JPEG 数据的 `POST` 请求，请求头为 `Content-Type: image/jpeg`。电脑与键盘需要连接在同一局域网。

Codex 读取依赖本机的 `codex.exe`。程序会先读取 `CODEX_CLI_PATH` 环境变量，再从 `PATH` 和 Windows 应用执行别名目录查找。若自动查找失败，可设置：

```powershell
[Environment]::SetEnvironmentVariable(
  "CODEX_CLI_PATH",
  "C:\path\to\codex.exe",
  "User"
)
```

设置后重新启动 Codex 屏显。

## 构建单文件 EXE

在仓库根目录运行：

```powershell
.\scripts\build.ps1
```

默认生成自包含的 x64 单文件版本：

```text
artifacts\windows-x64\CodexLinxDisplay.exe
```

自包含版本启用了单文件压缩，目标电脑无需另行安装 .NET Runtime。也可指定 ARM64：

当前 x64 构建约为 `63MB`；未压缩版本原先约为 `154MB`。

```powershell
.\scripts\build.ps1 -Runtime win-arm64
```

如果目标电脑已经安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)，可以生成体积最小的框架依赖版本：

```powershell
.\scripts\build.ps1 -FrameworkDependent
```

输出位于 `artifacts\windows-x64-framework-dependent`。它本身很小，但离开已安装的 .NET Desktop Runtime 无法运行。
当前 x64 框架依赖构建约为 `0.21MB`。

## 验证

不依赖第三方测试框架的冒烟测试会检查卡片尺寸、JPEG 编码、顶部安全区，以及发送给本地模拟设备的 HTTP 请求：

```powershell
dotnet run --project .\tests\CodexLinxDisplay.Windows.SmokeTests --configuration Release
```

追加 `-- --codex` 还会实际启动本机 Codex app-server 并读取一次用量：

```powershell
dotnet run --project .\tests\CodexLinxDisplay.Windows.SmokeTests --configuration Release -- --codex
```

## 本地数据与隐私

配置和自定义图片副本保存在 `%APPDATA%\CodexLinxDisplay`。程序不会上传 Codex 凭据或原始用量数据；只有渲染后的 JPEG 会发送到用户填写的键盘地址。

登录时启动使用当前用户的注册表项：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexLinxDisplay
```

## 项目结构

- `src/CodexLinxDisplay.Windows`：Windows 托盘应用。
- `src/CodexLinxDisplay.Windows/Services/CodexRateLimitClient.cs`：Codex app-server stdio 协议。
- `src/CodexLinxDisplay.Windows/Services/ScreenImageRenderer.cs`：用量卡片与自定义图片渲染、JPEG 编码。
- `src/CodexLinxDisplay.Windows/Services/ImageApiClient.cs`：Linx68 图像接口上传。
- `tests/CodexLinxDisplay.Windows.SmokeTests`：无需第三方测试框架的冒烟测试。
- `scripts/build.ps1`：x64/ARM64 单文件发布脚本。
