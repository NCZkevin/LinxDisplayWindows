using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace LinxDisplay.Core;

public interface ISystemMonitorService
{
    SystemSnapshot Sample(DateTimeOffset? sampledAt = null);
}

public interface IStartupManager
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

public static class PlatformServiceFactory
{
    public static ISystemMonitorService CreateSystemMonitor() =>
        OperatingSystem.IsWindows() ? new WindowsSystemMonitorService()
        : OperatingSystem.IsMacOS() ? new MacSystemMonitorService()
        : OperatingSystem.IsLinux() ? new LinuxSystemMonitorService()
        : throw new PlatformNotSupportedException("当前操作系统暂不支持系统监控。");

    public static IStartupManager CreateStartupManager() =>
        OperatingSystem.IsWindows() ? new WindowsStartupManager()
        : OperatingSystem.IsMacOS() ? new MacStartupManager()
        : OperatingSystem.IsLinux() ? new LinuxStartupManager()
        : new UnsupportedStartupManager();
}

public abstract class SystemMonitorServiceBase : ISystemMonitorService
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
        ReadMemory(out var usedMemory, out var totalMemory);
        ReadNetworkTotals(out var received, out var sent);

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

    protected abstract void ReadCpuTimes(out ulong idle, out ulong total);
    protected abstract void ReadMemory(out ulong used, out ulong total);

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
            catch (NetworkInformationException) { }
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemMonitorService : SystemMonitorServiceBase
{
    protected override void ReadCpuTimes(out ulong idle, out ulong total)
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            throw new InvalidOperationException("无法读取 Windows CPU 计数器。");
        idle = idleTime.Value;
        total = kernelTime.Value + userTime.Value;
    }

    protected override void ReadMemory(out ulong used, out ulong total)
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
            throw new InvalidOperationException("无法读取 Windows 内存状态。");
        total = status.TotalPhysical;
        used = total >= status.AvailablePhysical ? total - status.AvailablePhysical : 0;
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
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}

[SupportedOSPlatform("linux")]
public sealed class LinuxSystemMonitorService : SystemMonitorServiceBase
{
    protected override void ReadCpuTimes(out ulong idle, out ulong total)
    {
        var fields = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var values = fields.Skip(1).Select(ulong.Parse).ToArray();
        idle = values.ElementAtOrDefault(3) + values.ElementAtOrDefault(4);
        total = values.Aggregate(0UL, (sum, value) => sum + value);
    }

    protected override void ReadMemory(out ulong used, out ulong total)
    {
        var values = File.ReadLines("/proc/meminfo")
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => ParseKilobytes(parts[1]));
        total = values.GetValueOrDefault("MemTotal") * 1024;
        var available = values.GetValueOrDefault("MemAvailable") * 1024;
        used = total >= available ? total - available : 0;
    }

    private static ulong ParseKilobytes(string value) =>
        ulong.TryParse(value.Trim().Split(' ')[0], out var parsed) ? parsed : 0;
}

[SupportedOSPlatform("macos")]
public sealed class MacSystemMonitorService : SystemMonitorServiceBase
{
    protected override void ReadCpuTimes(out ulong idle, out ulong total)
    {
        var info = new CpuLoadInfo();
        var count = 4u;
        if (host_statistics(mach_host_self(), 3, ref info, ref count) != 0)
            throw new InvalidOperationException("无法读取 macOS CPU 计数器。");
        idle = info.Idle;
        total = (ulong)info.User + info.System + info.Idle + info.Nice;
    }

    protected override void ReadMemory(out ulong used, out ulong total)
    {
        total = ReadSysctlUInt64("hw.memsize");
        var availablePages = ReadSysctlUInt64("vm.page_free_count")
                             + ReadSysctlUInt64("vm.page_inactive_count")
                             + ReadSysctlUInt64("vm.page_speculative_count");
        var available = availablePages * (ulong)Environment.SystemPageSize;
        used = total >= available ? total - available : 0;
    }

    private static ulong ReadSysctlUInt64(string name)
    {
        var size = (nuint)8;
        var pointer = Marshal.AllocHGlobal(8);
        try
        {
            Marshal.WriteInt64(pointer, 0);
            if (sysctlbyname(name, pointer, ref size, IntPtr.Zero, 0) != 0)
                return 0;
            return size <= 4 ? (uint)Marshal.ReadInt32(pointer) : (ulong)Marshal.ReadInt64(pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CpuLoadInfo
    {
        public uint User;
        public uint System;
        public uint Idle;
        public uint Nice;
    }

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern uint mach_host_self();

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int host_statistics(uint host, int flavor, ref CpuLoadInfo info, ref uint count);

    [DllImport("/usr/lib/libSystem.B.dylib", CharSet = CharSet.Ansi)]
    private static extern int sysctlbyname(string name, IntPtr oldValue, ref nuint oldLength,
        IntPtr newValue, nuint newLength);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsStartupManager : IStartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LinxDisplay";
    private const string LegacyValueName = "CodexLinxDisplay";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        if (key?.GetValue(ValueName) is string) return true;
        if (key?.GetValue(LegacyValueName) is not string) return false;
        SetEnabled(true);
        return true;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
        {
            key.SetValue(ValueName, ApplicationCommand.QuotedCommand, RegistryValueKind.String);
            key.DeleteValue(LegacyValueName, false);
        }
        else
        {
            key.DeleteValue(ValueName, false);
            key.DeleteValue(LegacyValueName, false);
        }
    }
}

[SupportedOSPlatform("macos")]
public sealed class MacStartupManager : IStartupManager
{
    private static string PathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", "com.linxdisplay.desktop.plist");
    private static string LegacyPathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", "com.codexlinxdisplay.desktop.plist");

    public bool IsEnabled()
    {
        if (File.Exists(PathName)) return true;
        if (!File.Exists(LegacyPathName)) return false;
        SetEnabled(true);
        return true;
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(PathName)) File.Delete(PathName);
            if (File.Exists(LegacyPathName)) File.Delete(LegacyPathName);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        var arguments = string.Join(Environment.NewLine,
            ApplicationCommand.Parts.Select(part => $"      <string>{SecurityElement.Escape(part)}</string>"));
        File.WriteAllText(PathName, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict>
              <key>Label</key><string>com.linxdisplay.desktop</string>
              <key>ProgramArguments</key><array>
            {arguments}
              </array>
              <key>RunAtLoad</key><true/>
            </dict></plist>
            """);
        if (File.Exists(LegacyPathName)) File.Delete(LegacyPathName);
    }
}

[SupportedOSPlatform("linux")]
public sealed class LinuxStartupManager : IStartupManager
{
    private static string PathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "autostart", "linx-display.desktop");
    private static string LegacyPathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "autostart", "codex-linx-display.desktop");

    public bool IsEnabled()
    {
        if (File.Exists(PathName)) return true;
        if (!File.Exists(LegacyPathName)) return false;
        SetEnabled(true);
        return true;
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(PathName)) File.Delete(PathName);
            if (File.Exists(LegacyPathName)) File.Delete(LegacyPathName);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName, $"""
            [Desktop Entry]
            Type=Application
            Name=LinxDisplay
            Exec={ApplicationCommand.QuotedCommand}
            Terminal=false
            X-GNOME-Autostart-enabled=true
            """);
        if (File.Exists(LegacyPathName)) File.Delete(LegacyPathName);
    }
}

public sealed class UnsupportedStartupManager : IStartupManager
{
    public bool IsEnabled() => false;
    public void SetEnabled(bool enabled)
    {
        if (enabled) throw new PlatformNotSupportedException("当前操作系统暂不支持自动启动。");
    }
}

internal static class ApplicationCommand
{
    public static IReadOnlyList<string> Parts
    {
        get
        {
            var process = Environment.ProcessPath
                          ?? throw new InvalidOperationException("无法确定程序路径。");
            var parts = new List<string> { process };
            if (Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                var managedEntryPoint = Environment.GetCommandLineArgs().FirstOrDefault();
                if (string.IsNullOrWhiteSpace(managedEntryPoint))
                    throw new InvalidOperationException("无法确定程序程序集路径。");
                parts.Add(Path.GetFullPath(managedEntryPoint));
            }
            parts.Add("--background");
            return parts;
        }
    }

    public static string QuotedCommand => string.Join(' ', Parts.Select(Quote));

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
