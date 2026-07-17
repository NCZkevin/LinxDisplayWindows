namespace CodexLinxDisplay.Windows.Models;

internal enum DisplayMode
{
    Codex,
    CustomImage
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
}
