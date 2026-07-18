namespace LinxDisplay.Core;

public enum AutomaticSyncAction
{
    None,
    Push,
    RefreshAndPush
}

public static class AutomaticSyncPlanner
{
    public static AutomaticSyncAction ForCodex(
        DateTimeOffset now,
        DateTimeOffset lastRefresh,
        DateTimeOffset lastPush,
        int refreshSeconds)
    {
        if (lastRefresh == DateTimeOffset.MinValue
            || now - lastRefresh >= TimeSpan.FromSeconds(Math.Max(1, refreshSeconds)))
            return AutomaticSyncAction.RefreshAndPush;

        if (lastPush == DateTimeOffset.MinValue || !IsSameLocalMinute(now, lastPush))
            return AutomaticSyncAction.Push;

        return AutomaticSyncAction.None;
    }

    private static bool IsSameLocalMinute(DateTimeOffset left, DateTimeOffset right)
    {
        var a = left.ToLocalTime();
        var b = right.ToLocalTime();
        return a.Year == b.Year
               && a.Month == b.Month
               && a.Day == b.Day
               && a.Hour == b.Hour
               && a.Minute == b.Minute;
    }
}
