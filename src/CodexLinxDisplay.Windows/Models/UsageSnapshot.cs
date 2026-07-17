namespace CodexLinxDisplay.Windows.Models;

internal sealed record UsageSnapshot(
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

    public static UsageSnapshot Sample { get; } = new(
        98,
        DateTimeOffset.Now.AddDays(5),
        10_080,
        3,
        "plus");
}
