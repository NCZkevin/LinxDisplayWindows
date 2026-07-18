namespace LinxDisplay.Windows.Models;

internal sealed record SystemSnapshot(
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
