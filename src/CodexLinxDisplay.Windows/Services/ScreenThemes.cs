using CodexLinxDisplay.Windows.Models;

namespace CodexLinxDisplay.Windows.Services;

internal sealed record ScreenPalette(
    Color Background,
    Color Card,
    Color Inset,
    Color Border,
    Color Accent,
    Color SecondaryAccent,
    Color PrimaryText,
    Color SecondaryText,
    Color TertiaryText);

internal static class ScreenThemes
{
    public static ScreenPalette Get(CardTheme theme) => theme switch
    {
        CardTheme.MinimalLight => new ScreenPalette(
            Color.FromArgb(238, 242, 247),
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(243, 246, 250),
            Color.FromArgb(203, 213, 225),
            Color.FromArgb(15, 118, 110),
            Color.FromArgb(37, 99, 235),
            Color.FromArgb(15, 23, 42),
            Color.FromArgb(71, 85, 105),
            Color.FromArgb(100, 116, 139)),
        CardTheme.NeonPurple => new ScreenPalette(
            Color.FromArgb(9, 6, 23),
            Color.FromArgb(23, 16, 43),
            Color.FromArgb(16, 11, 32),
            Color.FromArgb(73, 49, 110),
            Color.FromArgb(192, 132, 252),
            Color.FromArgb(34, 211, 238),
            Color.FromArgb(250, 245, 255),
            Color.FromArgb(216, 180, 254),
            Color.FromArgb(139, 123, 168)),
        CardTheme.AmberTerminal => new ScreenPalette(
            Color.FromArgb(11, 10, 7),
            Color.FromArgb(26, 22, 13),
            Color.FromArgb(16, 14, 8),
            Color.FromArgb(91, 71, 32),
            Color.FromArgb(251, 191, 36),
            Color.FromArgb(251, 146, 60),
            Color.FromArgb(255, 247, 214),
            Color.FromArgb(214, 185, 108),
            Color.FromArgb(140, 117, 64)),
        _ => new ScreenPalette(
            Color.FromArgb(8, 11, 18),
            Color.FromArgb(17, 24, 39),
            Color.FromArgb(11, 18, 32),
            Color.FromArgb(38, 52, 74),
            Color.FromArgb(85, 230, 184),
            Color.FromArgb(96, 165, 250),
            Color.FromArgb(248, 250, 252),
            Color.FromArgb(148, 163, 184),
            Color.FromArgb(100, 116, 139))
    };

    public static string DisplayName(CardTheme theme) => theme switch
    {
        CardTheme.MinimalLight => "明亮极简",
        CardTheme.NeonPurple => "霓虹紫",
        CardTheme.AmberTerminal => "琥珀终端",
        _ => "深空薄荷"
    };
}
