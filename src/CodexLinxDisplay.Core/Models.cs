namespace CodexLinxDisplay.Core;

public enum DisplayMode
{
    Codex,
    Pomodoro,
    SystemMonitor,
    CustomImage
}

public enum CardTheme
{
    DeepSpace,
    MinimalLight,
    NeonPurple,
    AmberTerminal
}

public sealed class AppSettings
{
    public string Endpoint { get; set; } = "http://192.168.31.71/image/upload";
    public int CodexRefreshSeconds { get; set; } = 300;
    public int DynamicUploadSeconds { get; set; } = 5;
    public int SafeAreaHeight { get; set; } = 56;
    public int JpegQuality { get; set; } = 90;
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Codex;
    public CardTheme CardTheme { get; set; } = CardTheme.DeepSpace;
    public string? CustomImagePath { get; set; }
    public string? CustomImageName { get; set; }
    public bool StartWithSystem { get; set; }
    public string? CodexCliPath { get; set; }
}

public sealed record UsageSnapshot(
    int RemainingPercent,
    DateTimeOffset? ResetDate,
    int? WindowMinutes,
    int AvailableResetCount,
    string? PlanType)
{
    public string WindowTitle => WindowMinutes switch
    {
        >= 10_080 => "本周剩余",
        >= 1_440 => "本日剩余",
        300 => "5 小时剩余",
        _ => "周期剩余"
    };

    public string WindowDescription => WindowMinutes switch
    {
        null => "当前周期",
        var minutes when minutes % 1_440 == 0 => $"{minutes / 1_440} 天周期",
        var minutes when minutes % 60 == 0 => $"{minutes / 60} 小时周期",
        var minutes => $"{minutes} 分钟周期"
    };

    public static UsageSnapshot Sample { get; } = new(98, DateTimeOffset.Now.AddDays(5), 10_080, 3, "plus");
}

public enum PomodoroPhase
{
    Idle,
    Focus,
    ShortBreak,
    LongBreak,
    Paused
}

public sealed class PomodoroState
{
    public PomodoroPhase Phase { get; set; } = PomodoroPhase.Idle;
    public PomodoroPhase ResumePhase { get; set; } = PomodoroPhase.Focus;
    public DateTimeOffset? EndsAt { get; set; }
    public int PausedRemainingSeconds { get; set; }
    public int CompletedFocusSessions { get; set; }
    public string TaskName { get; set; } = "专注工作";
    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
}

public sealed record PomodoroSnapshot(
    PomodoroPhase Phase,
    PomodoroPhase EffectivePhase,
    string TaskName,
    TimeSpan Remaining,
    TimeSpan Duration,
    int CompletedFocusSessions,
    DateTimeOffset? EndsAt)
{
    public bool IsRunning => Phase is PomodoroPhase.Focus or PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak;
    public bool IsPaused => Phase == PomodoroPhase.Paused;
    public double Progress => Duration.TotalSeconds <= 0
        ? 0
        : Math.Clamp(1 - Remaining.TotalSeconds / Duration.TotalSeconds, 0, 1);
}

public sealed record SystemSnapshot(
    double CpuPercent,
    double MemoryPercent,
    ulong UsedMemoryBytes,
    ulong TotalMemoryBytes,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond,
    TimeSpan Uptime,
    DateTimeOffset SampledAt)
{
    public static SystemSnapshot Empty => new(0, 0, 0, 0, 0, 0,
        TimeSpan.FromMilliseconds(Environment.TickCount64), DateTimeOffset.Now);
}

public readonly record struct Choice<T>(string Name, T Value)
{
    public override string ToString() => Name;
}
