using LinxDisplay.Core;
using LinxDisplay.Rendering;
using SkiaSharp;
using System.Text.Json;

var checks = new List<(string Name, Action Run)>();
foreach (var theme in Enum.GetValues<CardTheme>())
{
    checks.Add(($"{theme} Codex renderer", () => AssertJpeg(ScreenRenderer.RenderUsage(
        new UsageSnapshot(91, DateTimeOffset.Now.AddDays(5), 10_080, 4, "plus"),
        Settings(theme), new DateTimeOffset(2026, 7, 18, 2, 14, 0, TimeSpan.FromHours(8))))));
    checks.Add(($"{theme} Pomodoro renderer", () => AssertJpeg(ScreenRenderer.RenderPomodoro(
        new PomodoroSnapshot(PomodoroPhase.Focus, PomodoroPhase.Focus, "跨平台开发",
            TimeSpan.FromMinutes(18), TimeSpan.FromMinutes(25), 3, DateTimeOffset.Now.AddMinutes(18)),
        Settings(theme)))));
    checks.Add(($"{theme} system renderer", () => AssertJpeg(ScreenRenderer.RenderSystem(
        new SystemSnapshot(42, 68, 11UL << 30, 16UL << 30, 2.5 * 1024 * 1024, 384 * 1024,
            TimeSpan.FromHours(53), DateTimeOffset.Now), Settings(theme)))));
}

checks.Add(("Pomodoro transitions", () =>
{
    var start = new DateTimeOffset(2026, 7, 18, 2, 14, 0, TimeSpan.FromHours(8));
    var service = new PomodoroService(new PomodoroState { FocusMinutes = 1, ShortBreakMinutes = 1 });
    service.StartOrResume(start);
    Assert(service.GetSnapshot(start).Phase == PomodoroPhase.Focus, "番茄钟未进入专注阶段");
    service.Tick(start.AddMinutes(1));
    var snapshot = service.GetSnapshot(start.AddMinutes(1));
    Assert(snapshot.Phase == PomodoroPhase.ShortBreak, "番茄钟未进入短休息");
    Assert(snapshot.CompletedFocusSessions == 1, "完成次数不正确");
}));

checks.Add(("Codex automatic sync schedule", () =>
{
    var now = new DateTimeOffset(2026, 7, 18, 2, 14, 30, TimeSpan.FromHours(8));
    Assert(AutomaticSyncPlanner.ForCodex(now, DateTimeOffset.MinValue, DateTimeOffset.MinValue, 300)
           == AutomaticSyncAction.RefreshAndPush, "首次启动没有安排刷新并推送");
    Assert(AutomaticSyncPlanner.ForCodex(now, now.AddMinutes(-5), now.AddSeconds(-10), 300)
           == AutomaticSyncAction.RefreshAndPush, "刷新周期到期没有安排刷新并推送");
    Assert(AutomaticSyncPlanner.ForCodex(now, now.AddMinutes(-1), now.AddMinutes(-1), 300)
           == AutomaticSyncAction.Push, "分钟变化没有安排时钟推送");
    Assert(AutomaticSyncPlanner.ForCodex(now, now.AddMinutes(-1), now.AddSeconds(-10), 300)
           == AutomaticSyncAction.None, "同一分钟内发生了重复推送");
}));

checks.Add(("macOS Codex CLI discovery paths", () =>
{
    var home = Path.Combine(Path.GetTempPath(), "codex-cli-home");
    var configured = Path.Combine(home, "custom", "codex");
    var path = string.Join(Path.PathSeparator, Path.Combine(home, "bin"), "/usr/bin");
    var candidates = CodexCliLocator.BuildCandidatePaths(configured, null, path, home, false, true);
    Assert(candidates[0] == configured, "应用内指定的 Codex CLI 路径没有最高优先级");
    Assert(candidates.Contains(Path.Combine(home, "bin", "codex")), "没有检查继承的 PATH");
    Assert(candidates.Contains("/opt/homebrew/bin/codex"), "没有检查 Apple Silicon Homebrew 路径");
    Assert(candidates.Contains("/usr/local/bin/codex"), "没有检查 Intel Homebrew/npm 路径");
    Assert(candidates.Contains(Path.Combine(home, ".volta", "bin", "codex")), "没有检查 Volta 路径");
    Assert(candidates.Contains(Path.Combine(home, "Library", "pnpm", "codex")), "没有检查 macOS pnpm 路径");
}));

checks.Add(("Legacy application data migration", () =>
{
    var applicationData = Path.Combine(Path.GetTempPath(), $"linxdisplay-migration-{Guid.NewGuid():N}");
    var legacyDirectory = Path.Combine(applicationData, "CodexLinxDisplay");
    try
    {
        Directory.CreateDirectory(legacyDirectory);
        var legacyImagePath = Path.Combine(legacyDirectory, "custom-image.png");
        File.WriteAllBytes(legacyImagePath, [0x89, 0x50, 0x4e, 0x47]);
        File.WriteAllText(Path.Combine(legacyDirectory, "settings-v2.json"), JsonSerializer.Serialize(
            new AppSettings
            {
                Endpoint = "http://192.0.2.68/image/upload",
                CustomImagePath = legacyImagePath,
                CustomImageName = "migration.png"
            }));
        File.WriteAllText(Path.Combine(legacyDirectory, "pomodoro.json"), JsonSerializer.Serialize(
            new PomodoroState { TaskName = "Migration test", CompletedFocusSessions = 2 }));

        var store = new SettingsStore(applicationData);
        var settings = store.Load();
        Assert(settings.Endpoint == "http://192.0.2.68/image/upload", "旧 API 地址未迁移");
        Assert(settings.CustomImagePath == store.CustomImagePath, "自定义图片路径未切换到 LinxDisplay 目录");
        Assert(File.Exists(store.CustomImagePath), "自定义图片文件未迁移");
        Assert(store.LoadPomodoro().CompletedFocusSessions == 2, "番茄钟状态未迁移");
    }
    finally
    {
        if (Directory.Exists(applicationData)) Directory.Delete(applicationData, true);
    }
}));

checks.Add(("Platform system monitor", () =>
{
    var monitor = PlatformServiceFactory.CreateSystemMonitor();
    monitor.Sample();
    Thread.Sleep(50);
    var sample = monitor.Sample();
    Assert(sample.CpuPercent is >= 0 and <= 100, "CPU 百分比越界");
    Assert(sample.MemoryPercent is >= 0 and <= 100, "内存百分比越界");
    Assert(sample.TotalMemoryBytes > 0, "未读取到系统内存");
}));

var failures = 0;
foreach (var check in checks)
{
    try
    {
        check.Run();
        Console.WriteLine($"PASS  {check.Name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL  {check.Name}: {error}");
    }
}

Console.WriteLine($"{checks.Count - failures}/{checks.Count} checks passed");
return failures == 0 ? 0 : 1;

static AppSettings Settings(CardTheme theme) => new() { CardTheme = theme, SafeAreaHeight = 56, JpegQuality = 90 };

static void AssertJpeg(byte[] data)
{
    Assert(data.Length is > 1000 and <= ScreenRenderer.MaximumFileSize, "JPEG 文件大小异常");
    Assert(data[0] == 0xff && data[1] == 0xd8, "不是 JPEG 数据");
    using var bitmap = SKBitmap.Decode(data);
    if (bitmap is null) throw new InvalidOperationException("JPEG 无法解码");
    Assert(bitmap.Width == ScreenRenderer.Width && bitmap.Height == ScreenRenderer.Height,
        $"图片尺寸错误：{bitmap.Width}×{bitmap.Height}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
