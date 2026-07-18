# Codex 屏显 Windows 版

这是 macOS 版 CodexLinxDisplay 的原生 Windows 实现。它常驻系统托盘，把 Codex 用量、番茄钟、系统状态或自定义图片渲染成固定为 `142 × 428` 的 JPEG，并通过局域网推送到 Linx68 键盘。

<p align="center">
  <img src="src/CodexLinxDisplay.Windows/Assets/app-icon.png" width="128" alt="Codex 屏显应用图标">
</p>

## 下载

普通用户请从 [GitHub Releases](https://github.com/NCZkevin/LinxDisplayWindows/releases/latest) 下载最新版本：

- `SelfContained`：推荐版本，解压即可运行，不需要预先安装 .NET。
- `FrameworkDependent`：体积更小，但电脑必须安装 .NET 8 Desktop Runtime。
- `SHA256SUMS.txt`：下载文件的 SHA-256 校验值。

## 功能

- 读取 Codex 本周/当前周期剩余用量、可用重置次数和重置时间。
- 内置番茄钟：可设置任务、专注/短休/长休时长，支持开始、暂停、继续、跳过和重置；每完成四个番茄自动进入长休息。
- 内置系统监控：显示 CPU、内存占用、实时下载/上传速率和系统运行时间，推送间隔可选 2、5、10 或 30 秒。
- 四套卡片主题可随时切换：深空薄荷、明亮极简、霓虹紫和琥珀终端；Codex、番茄钟与系统监控共用所选主题。
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

在“显示模式”中选择“番茄钟”或“系统监控”即可切换屏幕内容。番茄钟使用绝对结束时间，即使电脑锁屏或睡眠，恢复后也会自动推进到正确阶段；运行状态保存在本地，重启软件不会丢失。系统监控直接读取 Windows 和网卡的本机计数器，不需要管理员权限或第三方服务。

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
当前 x64 框架依赖构建约为 `0.40MB`。

仓库包含自动发布流程：项目版本更新后推送对应的 `vX.Y.Z` Git 标签，GitHub Actions 会运行冒烟测试、构建以上两个版本，并自动创建带 ZIP 和校验文件的 GitHub Release。

## 验证

不依赖第三方测试框架的冒烟测试会检查四套主题的全部卡片尺寸与差异、番茄钟阶段切换、Windows 系统采样、JPEG 编码、顶部安全区，以及发送给本地模拟设备的 HTTP 请求：

```powershell
dotnet run --project .\tests\CodexLinxDisplay.Windows.SmokeTests --configuration Release
```

追加 `-- --codex` 还会实际启动本机 Codex app-server 并读取一次用量：

```powershell
dotnet run --project .\tests\CodexLinxDisplay.Windows.SmokeTests --configuration Release -- --codex
```

## 本地数据与隐私

配置、番茄钟状态和自定义图片副本保存在 `%APPDATA%\CodexLinxDisplay`。程序不会上传 Codex 凭据或原始用量/系统监控数据；只有渲染后的 JPEG 会发送到用户填写的键盘地址。

登录时启动使用当前用户的注册表项：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexLinxDisplay
```

## 项目结构

- `src/CodexLinxDisplay.Windows`：Windows 托盘应用。
- `src/CodexLinxDisplay.Windows/Services/CodexRateLimitClient.cs`：Codex app-server stdio 协议。
- `src/CodexLinxDisplay.Windows/Services/ScreenImageRenderer.cs`：用量卡片与自定义图片渲染、JPEG 编码。
- `src/CodexLinxDisplay.Windows/Services/PomodoroService.cs`：可跨睡眠恢复的番茄钟状态机。
- `src/CodexLinxDisplay.Windows/Services/SystemMonitorService.cs`：CPU、内存与网卡速率采样。
- `src/CodexLinxDisplay.Windows/Services/StatusCardRenderer.cs`：番茄钟与系统监控卡片渲染。
- `src/CodexLinxDisplay.Windows/Services/ScreenThemes.cs`：四套卡片主题及共享调色板。
- `src/CodexLinxDisplay.Windows/Services/ImageApiClient.cs`：Linx68 图像接口上传。
- `tests/CodexLinxDisplay.Windows.SmokeTests`：无需第三方测试框架的冒烟测试。
- `scripts/build.ps1`：x64/ARM64 单文件发布脚本。
