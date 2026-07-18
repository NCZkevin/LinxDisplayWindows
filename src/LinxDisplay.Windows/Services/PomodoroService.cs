using LinxDisplay.Windows.Models;

namespace LinxDisplay.Windows.Services;

internal sealed class PomodoroService
{
    public PomodoroState State { get; }

    public PomodoroService(PomodoroState state)
    {
        State = state;
        Normalize();
    }

    public bool Tick(DateTimeOffset now)
    {
        var changed = false;
        while (IsActive(State.Phase) && State.EndsAt is { } endsAt && now >= endsAt)
        {
            Advance(endsAt);
            changed = true;
        }
        return changed;
    }

    public void StartOrResume(DateTimeOffset now)
    {
        if (State.Phase == PomodoroPhase.Idle)
        {
            State.Phase = PomodoroPhase.Focus;
            State.EndsAt = now.AddMinutes(State.FocusMinutes);
            return;
        }

        if (State.Phase != PomodoroPhase.Paused) return;
        State.Phase = IsActive(State.ResumePhase) ? State.ResumePhase : PomodoroPhase.Focus;
        State.EndsAt = now.AddSeconds(Math.Max(1, State.PausedRemainingSeconds));
        State.PausedRemainingSeconds = 0;
    }

    public void Pause(DateTimeOffset now)
    {
        if (!IsActive(State.Phase)) return;
        State.PausedRemainingSeconds = Math.Max(0,
            (int)Math.Ceiling((State.EndsAt.GetValueOrDefault(now) - now).TotalSeconds));
        State.ResumePhase = State.Phase;
        State.Phase = PomodoroPhase.Paused;
        State.EndsAt = null;
    }

    public void Skip(DateTimeOffset now)
    {
        if (State.Phase == PomodoroPhase.Idle)
        {
            StartOrResume(now);
            return;
        }

        var phase = State.Phase == PomodoroPhase.Paused ? State.ResumePhase : State.Phase;
        State.Phase = phase;
        Advance(now);
    }

    public void Reset()
    {
        State.Phase = PomodoroPhase.Idle;
        State.ResumePhase = PomodoroPhase.Focus;
        State.EndsAt = null;
        State.PausedRemainingSeconds = 0;
        State.CompletedFocusSessions = 0;
    }

    public PomodoroSnapshot GetSnapshot(DateTimeOffset now)
    {
        var effective = State.Phase == PomodoroPhase.Paused ? State.ResumePhase : State.Phase;
        if (effective == PomodoroPhase.Idle) effective = PomodoroPhase.Focus;
        var duration = TimeSpan.FromMinutes(GetMinutes(effective));
        var remaining = State.Phase switch
        {
            PomodoroPhase.Idle => duration,
            PomodoroPhase.Paused => TimeSpan.FromSeconds(Math.Max(0, State.PausedRemainingSeconds)),
            _ => State.EndsAt is { } endsAt ? endsAt - now : TimeSpan.Zero
        };
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        if (remaining > duration) remaining = duration;
        return new PomodoroSnapshot(State.Phase, effective, State.TaskName.Trim(), remaining,
            duration, State.CompletedFocusSessions, State.EndsAt);
    }

    private void Advance(DateTimeOffset nextStart)
    {
        switch (State.Phase)
        {
            case PomodoroPhase.Focus:
                State.CompletedFocusSessions++;
                State.Phase = State.CompletedFocusSessions % 4 == 0
                    ? PomodoroPhase.LongBreak
                    : PomodoroPhase.ShortBreak;
                break;
            case PomodoroPhase.ShortBreak:
            case PomodoroPhase.LongBreak:
                State.Phase = PomodoroPhase.Focus;
                break;
            default:
                State.Phase = PomodoroPhase.Focus;
                break;
        }
        State.ResumePhase = State.Phase;
        State.PausedRemainingSeconds = 0;
        State.EndsAt = nextStart.AddMinutes(GetMinutes(State.Phase));
    }

    private int GetMinutes(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.ShortBreak => State.ShortBreakMinutes,
        PomodoroPhase.LongBreak => State.LongBreakMinutes,
        _ => State.FocusMinutes
    };

    private void Normalize()
    {
        State.FocusMinutes = Math.Clamp(State.FocusMinutes, 1, 120);
        State.ShortBreakMinutes = Math.Clamp(State.ShortBreakMinutes, 1, 60);
        State.LongBreakMinutes = Math.Clamp(State.LongBreakMinutes, 1, 120);
        State.CompletedFocusSessions = Math.Max(0, State.CompletedFocusSessions);
        State.TaskName = string.IsNullOrWhiteSpace(State.TaskName) ? "专注工作" : State.TaskName.Trim();
        if (State.Phase == PomodoroPhase.Paused && !IsActive(State.ResumePhase))
            State.ResumePhase = PomodoroPhase.Focus;
        if (IsActive(State.Phase) && State.EndsAt is null)
            State.Phase = PomodoroPhase.Idle;
    }

    private static bool IsActive(PomodoroPhase phase) =>
        phase is PomodoroPhase.Focus or PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak;
}
