using System.Drawing.Drawing2D;
using CodexLinxDisplay.Windows.Models;

namespace CodexLinxDisplay.Windows.Services;

internal static class StatusCardRenderer
{
    private static readonly Color Background = Color.FromArgb(8, 11, 18);
    private static readonly Color Card = Color.FromArgb(17, 24, 39);
    private static readonly Color Inset = Color.FromArgb(11, 18, 32);
    private static readonly Color Border = Color.FromArgb(38, 52, 74);
    private static readonly Color Mint = Color.FromArgb(85, 230, 184);
    private static readonly Color Blue = Color.FromArgb(96, 165, 250);
    private static readonly Color Amber = Color.FromArgb(251, 191, 36);
    private static readonly Color White = Color.FromArgb(248, 250, 252);
    private static readonly Color Muted = Color.FromArgb(148, 163, 184);

    public static Bitmap RenderPomodoro(PomodoroSnapshot snapshot, int safeAreaHeight, DateTimeOffset now)
    {
        var bitmap = CreateCanvas(safeAreaHeight, out var graphics, out var card);
        using (graphics)
        {
            var isBreak = snapshot.EffectivePhase is PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak;
            var accent = isBreak ? Blue : Mint;
            DrawHeader(graphics, card, isBreak ? "BREAK" : "FOCUS", snapshot.IsRunning ? "LIVE" : snapshot.IsPaused ? "PAUSE" : "READY", accent);

            using var taskFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Pixel);
            using var timeFont = new Font("Segoe UI", 28F, FontStyle.Bold, GraphicsUnit.Pixel);
            using var labelFont = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Pixel);
            using var smallFont = new Font("Microsoft YaHei UI", 7F, FontStyle.Regular, GraphicsUnit.Pixel);
            using var whiteBrush = new SolidBrush(White);
            using var mutedBrush = new SolidBrush(Muted);
            using var accentBrush = new SolidBrush(accent);

            var task = string.IsNullOrWhiteSpace(snapshot.TaskName) ? "专注工作" : snapshot.TaskName;
            DrawCentered(graphics, task, taskFont, whiteBrush, new Rectangle(card.Left + 10, card.Top + 50, card.Width - 20, 34));

            var totalSeconds = Math.Max(0, (int)Math.Ceiling(snapshot.Remaining.TotalSeconds));
            var time = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
            DrawCentered(graphics, time, timeFont, whiteBrush, new Rectangle(card.Left + 6, card.Top + 86, card.Width - 12, 45));

            var bar = new Rectangle(card.Left + 12, card.Top + 139, card.Width - 24, 8);
            DrawProgress(graphics, bar, snapshot.Progress, accent);
            DrawCentered(graphics, snapshot.IsPaused ? "已暂停" : snapshot.IsRunning ? "保持节奏" : "点击开始", labelFont,
                mutedBrush, new Rectangle(card.Left + 8, card.Top + 153, card.Width - 16, 20));

            var sessionBox = new Rectangle(card.Left + 10, card.Top + 184, card.Width - 20, 73);
            DrawBox(graphics, sessionBox);
            DrawCentered(graphics, "今日完成", smallFont, mutedBrush,
                new Rectangle(sessionBox.Left, sessionBox.Top + 10, sessionBox.Width, 18));
            DrawCentered(graphics, snapshot.CompletedFocusSessions.ToString(), timeFont, accentBrush,
                new Rectangle(sessionBox.Left, sessionBox.Top + 28, sessionBox.Width, 36));

            DrawCentered(graphics, snapshot.IsRunning && snapshot.EndsAt is { } end
                    ? $"结束 {end.LocalDateTime:HH:mm}"
                    : $"现在 {now.LocalDateTime:HH:mm}",
                labelFont, mutedBrush, new Rectangle(card.Left + 8, card.Bottom - 46, card.Width - 16, 20));
            DrawCentered(graphics, "POMODORO", smallFont, accentBrush,
                new Rectangle(card.Left + 8, card.Bottom - 24, card.Width - 16, 16));
        }
        return bitmap;
    }

    public static Bitmap RenderSystem(SystemSnapshot snapshot, int safeAreaHeight)
    {
        var bitmap = CreateCanvas(safeAreaHeight, out var graphics, out var card);
        using (graphics)
        {
            DrawHeader(graphics, card, "SYSTEM", "LIVE", Mint);
            using var bigFont = new Font("Segoe UI", 24F, FontStyle.Bold, GraphicsUnit.Pixel);
            using var valueFont = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Pixel);
            using var labelFont = new Font("Microsoft YaHei UI", 7.5F, FontStyle.Regular, GraphicsUnit.Pixel);
            using var whiteBrush = new SolidBrush(White);
            using var mutedBrush = new SolidBrush(Muted);
            using var mintBrush = new SolidBrush(Mint);

            graphics.DrawString("CPU", labelFont, mutedBrush, card.Left + 12, card.Top + 53);
            DrawRight(graphics, $"{snapshot.CpuPercent:0}%", bigFont, whiteBrush,
                new Rectangle(card.Left + 8, card.Top + 62, card.Width - 16, 36));
            DrawProgress(graphics, new Rectangle(card.Left + 12, card.Top + 105, card.Width - 24, 7), snapshot.CpuPercent / 100, Mint);

            graphics.DrawString("内存", labelFont, mutedBrush, card.Left + 12, card.Top + 128);
            DrawRight(graphics, $"{snapshot.MemoryPercent:0}%", valueFont, whiteBrush,
                new Rectangle(card.Left + 46, card.Top + 122, card.Width - 58, 23));
            DrawProgress(graphics, new Rectangle(card.Left + 12, card.Top + 151, card.Width - 24, 7), snapshot.MemoryPercent / 100, Blue);
            DrawCentered(graphics, $"{ToGiB(snapshot.UsedMemoryBytes):0.0} / {ToGiB(snapshot.TotalMemoryBytes):0.0} GB",
                labelFont, mutedBrush, new Rectangle(card.Left + 8, card.Top + 163, card.Width - 16, 17));

            var networkBox = new Rectangle(card.Left + 10, card.Top + 194, card.Width - 20, 91);
            DrawBox(graphics, networkBox);
            graphics.DrawString("网络速率", labelFont, mutedBrush, networkBox.Left + 9, networkBox.Top + 8);
            graphics.DrawString("↓", valueFont, mintBrush, networkBox.Left + 9, networkBox.Top + 32);
            DrawRight(graphics, FormatRate(snapshot.DownloadBytesPerSecond), valueFont, whiteBrush,
                new Rectangle(networkBox.Left + 29, networkBox.Top + 29, networkBox.Width - 37, 22));
            graphics.DrawString("↑", valueFont, Brushes.LightSkyBlue, networkBox.Left + 9, networkBox.Top + 60);
            DrawRight(graphics, FormatRate(snapshot.UploadBytesPerSecond), valueFont, whiteBrush,
                new Rectangle(networkBox.Left + 29, networkBox.Top + 57, networkBox.Width - 37, 22));

            var uptime = snapshot.Uptime.TotalDays >= 1
                ? $"运行 {(int)snapshot.Uptime.TotalDays}天 {snapshot.Uptime.Hours}时"
                : $"运行 {(int)snapshot.Uptime.TotalHours}时 {snapshot.Uptime.Minutes}分";
            DrawCentered(graphics, uptime, labelFont, mutedBrush,
                new Rectangle(card.Left + 8, card.Bottom - 47, card.Width - 16, 18));
            DrawCentered(graphics, snapshot.SampledAt.LocalDateTime.ToString("HH:mm:ss"), labelFont, mutedBrush,
                new Rectangle(card.Left + 8, card.Bottom - 26, card.Width - 16, 18));
        }
        return bitmap;
    }

    private static Bitmap CreateCanvas(int safeAreaHeight, out Graphics graphics, out Rectangle card)
    {
        var safeArea = Math.Clamp(safeAreaHeight, 44, 80);
        var bitmap = new Bitmap(ScreenImageRenderer.Width, ScreenImageRenderer.Height,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Background);
        card = new Rectangle(9, safeArea + 1, 124, Math.Max(320, ScreenImageRenderer.Height - safeArea - 10));
        using var path = RoundedRectangle(card, 13);
        using var fill = new SolidBrush(Card);
        using var pen = new Pen(Border);
        graphics.FillPath(fill, path);
        graphics.DrawPath(pen, path);
        return bitmap;
    }

    private static void DrawHeader(Graphics graphics, Rectangle card, string title, string badge, Color accent)
    {
        using var titleFont = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Pixel);
        using var badgeFont = new Font("Segoe UI", 6F, FontStyle.Bold, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(White);
        using var accentBrush = new SolidBrush(accent);
        graphics.FillEllipse(accentBrush, card.Left + 10, card.Top + 14, 7, 7);
        graphics.DrawString(title, titleFont, titleBrush, card.Left + 22, card.Top + 11);
        DrawRight(graphics, badge, badgeFont, accentBrush,
            new Rectangle(card.Right - 43, card.Top + 12, 32, 14));
        using var pen = new Pen(Border);
        graphics.DrawLine(pen, card.Left + 9, card.Top + 38, card.Right - 9, card.Top + 38);
    }

    private static void DrawBox(Graphics graphics, Rectangle bounds)
    {
        using var path = RoundedRectangle(bounds, 10);
        using var brush = new SolidBrush(Inset);
        using var pen = new Pen(Border);
        graphics.FillPath(brush, path);
        graphics.DrawPath(pen, path);
    }

    private static void DrawProgress(Graphics graphics, Rectangle bounds, double progress, Color color)
    {
        using var backgroundPath = RoundedRectangle(bounds, bounds.Height / 2f);
        using var backgroundBrush = new SolidBrush(Border);
        graphics.FillPath(backgroundBrush, backgroundPath);
        var width = (int)Math.Round(bounds.Width * Math.Clamp(progress, 0, 1));
        if (width <= 0) return;
        var filled = new Rectangle(bounds.X, bounds.Y, Math.Max(bounds.Height, width), bounds.Height);
        using var filledPath = RoundedRectangle(filled, bounds.Height / 2f);
        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, filledPath);
    }

    private static void DrawCentered(Graphics graphics, string text, Font font, Brush brush, Rectangle bounds)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static void DrawRight(Graphics graphics, string text, Font font, Brush brush, Rectangle bounds)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static double ToGiB(ulong bytes) => bytes / 1024d / 1024d / 1024d;

    internal static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024) return $"{bytesPerSecond / 1024 / 1024:0.0} MB/s";
        if (bytesPerSecond >= 1024) return $"{bytesPerSecond / 1024:0.0} KB/s";
        return $"{bytesPerSecond:0} B/s";
    }
}
