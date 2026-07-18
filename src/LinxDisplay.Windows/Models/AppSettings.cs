namespace LinxDisplay.Windows.Models;

internal enum DisplayMode
{
    Codex = 0,
    CustomImage = 1,
    Pomodoro = 2,
    SystemMonitor = 3
}

internal enum CardTheme
{
    DeepSpace = 0,
    MinimalLight = 1,
    NeonPurple = 2,
    AmberTerminal = 3
}

internal sealed class AppSettings
{
    public string Endpoint { get; set; } = "http://192.168.31.71/image/upload";
    public int RefreshIntervalSeconds { get; set; } = 300;
    public int SafeAreaHeight { get; set; } = 56;
    public int JpegQuality { get; set; } = 90;
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Codex;
    public string? CustomImagePath { get; set; }
    public string? CustomImageName { get; set; }
    public int SystemMonitorUploadIntervalSeconds { get; set; } = 5;
    public CardTheme CardTheme { get; set; } = CardTheme.DeepSpace;
}
