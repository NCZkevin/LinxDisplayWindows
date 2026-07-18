using Microsoft.Win32;

namespace LinxDisplay.Windows.Services;

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LinxDisplay";
    private const string LegacyValueName = "CodexLinxDisplay";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        if (key?.GetValue(ValueName) is string) return true;
        if (key?.GetValue(LegacyValueName) is not string) return false;
        SetEnabled(true);
        return true;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定当前程序路径。");
            key.SetValue(ValueName, $"\"{executable}\" --background", RegistryValueKind.String);
            key.DeleteValue(LegacyValueName, false);
        }
        else
        {
            key.DeleteValue(ValueName, false);
            key.DeleteValue(LegacyValueName, false);
        }
    }
}
