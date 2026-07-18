namespace LinxDisplay.Windows.Models;

internal enum PomodoroPhase
{
    Idle,
    Focus,
    ShortBreak,
    LongBreak,
    Paused
}

internal sealed class PomodoroState
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

internal sealed record PomodoroSnapshot(
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
