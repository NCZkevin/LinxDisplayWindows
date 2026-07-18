using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LinxDisplay.Windows.Models;

namespace LinxDisplay.Windows.Services;

internal sealed class SystemMonitorService
{
    private ulong _previousIdle;
    private ulong _previousTotal;
    private long _previousReceived;
    private long _previousSent;
    private DateTimeOffset _previousSampledAt;
    private bool _hasBaseline;

    public SystemSnapshot Sample(DateTimeOffset? sampledAt = null)
    {
        var now = sampledAt ?? DateTimeOffset.Now;
        ReadCpuTimes(out var idle, out var total);
        ReadNetworkTotals(out var received, out var sent);
        ReadMemory(out var usedMemory, out var totalMemory);

        var cpu = 0d;
        var download = 0d;
        var upload = 0d;
        if (_hasBaseline)
        {
            var totalDelta = total >= _previousTotal ? total - _previousTotal : 0;
            var idleDelta = idle >= _previousIdle ? idle - _previousIdle : 0;
            if (totalDelta > 0)
                cpu = Math.Clamp((totalDelta - Math.Min(idleDelta, totalDelta)) * 100d / totalDelta, 0, 100);

            var seconds = Math.Max(0.001, (now - _previousSampledAt).TotalSeconds);
            download = Math.Max(0, received - _previousReceived) / seconds;
            upload = Math.Max(0, sent - _previousSent) / seconds;
        }

        _previousIdle = idle;
        _previousTotal = total;
        _previousReceived = received;
        _previousSent = sent;
        _previousSampledAt = now;
        _hasBaseline = true;

        var memoryPercent = totalMemory == 0 ? 0 : usedMemory * 100d / totalMemory;
        return new SystemSnapshot(cpu, memoryPercent, usedMemory, totalMemory, download, upload,
            TimeSpan.FromMilliseconds(Environment.TickCount64), now);
    }

    private static void ReadCpuTimes(out ulong idle, out ulong total)
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            throw new InvalidOperationException("无法读取 Windows CPU 计数器。");
        idle = idleTime.Value;
        total = kernelTime.Value + userTime.Value;
    }

    private static void ReadMemory(out ulong used, out ulong total)
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
            throw new InvalidOperationException("无法读取 Windows 内存状态。");
        total = status.TotalPhysical;
        used = total >= status.AvailablePhysical ? total - status.AvailablePhysical : 0;
    }

    private static void ReadNetworkTotals(out long received, out long sent)
    {
        received = 0;
        sent = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up
                || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            try
            {
                var stats = adapter.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            catch (NetworkInformationException)
            {
                // An adapter can disappear while enumerating; ignore it until the next sample.
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
