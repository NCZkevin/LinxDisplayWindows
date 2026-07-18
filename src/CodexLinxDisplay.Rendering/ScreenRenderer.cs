using CodexLinxDisplay.Core;
using SkiaSharp;

namespace CodexLinxDisplay.Rendering;

public sealed record ScreenPalette(
    SKColor Background,
    SKColor Card,
    SKColor Inset,
    SKColor Border,
    SKColor Accent,
    SKColor SecondaryAccent,
    SKColor PrimaryText,
    SKColor SecondaryText,
    SKColor TertiaryText);

public static class ScreenThemes
{
    public static ScreenPalette Get(CardTheme theme) => theme switch
    {
        CardTheme.MinimalLight => Palette("eef2f7", "ffffff", "f3f6fa", "cbd5e1", "0f766e", "2563eb", "0f172a", "475569", "64748b"),
        CardTheme.NeonPurple => Palette("090617", "17102b", "100b20", "49316e", "c084fc", "22d3ee", "faf5ff", "d8b4fe", "8b7ba8"),
        CardTheme.AmberTerminal => Palette("0b0a07", "1a160d", "100e08", "5b4720", "fbbf24", "fb923c", "fff7d6", "d6b96c", "8c7540"),
        _ => Palette("080b12", "111827", "0b1220", "26344a", "55e6b8", "60a5fa", "f8fafc", "94a3b8", "64748b")
    };

    public static string DisplayName(CardTheme theme) => theme switch
    {
        CardTheme.MinimalLight => "明亮极简",
        CardTheme.NeonPurple => "霓虹紫",
        CardTheme.AmberTerminal => "琥珀终端",
        _ => "深空薄荷"
    };

    private static ScreenPalette Palette(params string[] colors) => new(
        Parse(colors[0]), Parse(colors[1]), Parse(colors[2]), Parse(colors[3]), Parse(colors[4]),
        Parse(colors[5]), Parse(colors[6]), Parse(colors[7]), Parse(colors[8]));

    private static SKColor Parse(string value) => SKColor.Parse('#' + value);
}

public static class ScreenRenderer
{
    public const int Width = 142;
    public const int Height = 428;
    public const int MaximumFileSize = 512 * 1024;

    public static byte[] RenderUsage(UsageSnapshot snapshot, AppSettings settings,
        DateTimeOffset? currentTime = null)
    {
        var colors = ScreenThemes.Get(settings.CardTheme);
        using var bitmap = CreateCanvas(settings.SafeAreaHeight, colors, out var canvas, out var card);
        using (canvas)
        {
            DrawHeader(canvas, card, "CODEX", "实时", colors.Accent, colors);
            DrawLine(canvas, card.Left + 9, card.Top + 38, card.Right - 9, card.Top + 38, colors.Border);

            var top = card.Top + 49;
            DrawText(canvas, snapshot.WindowTitle, 10, true, colors.SecondaryText,
                new SKRect(card.Left + 9, top, card.Right - 9, top + 18), SKTextAlign.Center);
            DrawText(canvas, snapshot.RemainingPercent.ToString(), 38, true, colors.PrimaryText,
                new SKRect(card.Left + 5, top + 18, card.Right - 28, top + 73), SKTextAlign.Right);
            DrawText(canvas, "%", 11, true, colors.Accent,
                new SKRect(card.Right - 25, top + 42, card.Right - 8, top + 62), SKTextAlign.Left);
            DrawProgress(canvas, new SKRect(card.Left + 9, top + 71, card.Right - 9, top + 79),
                snapshot.RemainingPercent / 100d, colors.Accent, colors.Border);
            DrawText(canvas, snapshot.WindowDescription, 9, false, colors.TertiaryText,
                new SKRect(card.Left + 9, top + 83, card.Right - 9, top + 101), SKTextAlign.Center);

            var reset = new SKRect(card.Left + 9, card.Top + 163, card.Right - 9, card.Top + 233);
            DrawBox(canvas, reset, colors);
            DrawText(canvas, "可用重置", 10, true, colors.SecondaryText,
                new SKRect(reset.Left + 10, reset.Top + 7, reset.Right - 10, reset.Top + 27), SKTextAlign.Left);
            DrawText(canvas, snapshot.AvailableResetCount.ToString(), 28, true, colors.PrimaryText,
                new SKRect(reset.Left + 10, reset.Top + 24, reset.Left + 68, reset.Bottom - 6), SKTextAlign.Left);
            DrawText(canvas, "次", 11, true, colors.Accent,
                new SKRect(reset.Right - 30, reset.Top + 37, reset.Right - 10, reset.Top + 58), SKTextAlign.Right);

            var now = (currentTime ?? DateTimeOffset.Now).ToLocalTime();
            var resetDate = snapshot.ResetDate?.ToLocalTime();
            var bottom = card.Bottom - 15;
            DrawText(canvas, resetDate is null ? "重置时间未知" : $"下次重置 {resetDate:M/d HH:mm}",
                8, false, colors.TertiaryText,
                new SKRect(card.Left + 7, bottom - 94, card.Right - 7, bottom - 79), SKTextAlign.Center);
            DrawText(canvas, "当前时间", 9, true, colors.TertiaryText,
                new SKRect(card.Left + 9, bottom - 76, card.Right - 9, bottom - 59), SKTextAlign.Center);
            DrawText(canvas, now.ToString("M月d日"), 17, true, colors.PrimaryText,
                new SKRect(card.Left + 5, bottom - 57, card.Right - 5, bottom - 33), SKTextAlign.Center);
            DrawText(canvas, now.ToString("HH:mm"), 22, true, colors.Accent,
                new SKRect(card.Left + 5, bottom - 32, card.Right - 5, bottom), SKTextAlign.Center);
        }
        return Encode(bitmap, settings.JpegQuality);
    }

    public static byte[] RenderPomodoro(PomodoroSnapshot snapshot, AppSettings settings,
        DateTimeOffset? currentTime = null)
    {
        var colors = ScreenThemes.Get(settings.CardTheme);
        var now = currentTime ?? DateTimeOffset.Now;
        using var bitmap = CreateCanvas(settings.SafeAreaHeight, colors, out var canvas, out var card);
        using (canvas)
        {
            var isBreak = snapshot.EffectivePhase is PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak;
            var accent = isBreak ? colors.SecondaryAccent : colors.Accent;
            DrawHeader(canvas, card, isBreak ? "BREAK" : "FOCUS",
                snapshot.IsRunning ? "LIVE" : snapshot.IsPaused ? "PAUSE" : "READY", accent, colors);
            DrawLine(canvas, card.Left + 9, card.Top + 38, card.Right - 9, card.Top + 38, colors.Border);
            DrawText(canvas, string.IsNullOrWhiteSpace(snapshot.TaskName) ? "专注工作" : snapshot.TaskName,
                9, true, colors.PrimaryText,
                new SKRect(card.Left + 10, card.Top + 50, card.Right - 10, card.Top + 84), SKTextAlign.Center);

            var seconds = Math.Max(0, (int)Math.Ceiling(snapshot.Remaining.TotalSeconds));
            DrawText(canvas, $"{seconds / 60:00}:{seconds % 60:00}", 28, true, colors.PrimaryText,
                new SKRect(card.Left + 6, card.Top + 86, card.Right - 6, card.Top + 131), SKTextAlign.Center);
            DrawProgress(canvas, new SKRect(card.Left + 12, card.Top + 139, card.Right - 12, card.Top + 147),
                snapshot.Progress, accent, colors.Border);
            DrawText(canvas, snapshot.IsPaused ? "已暂停" : snapshot.IsRunning ? "保持节奏" : "点击开始",
                8, false, colors.SecondaryText,
                new SKRect(card.Left + 8, card.Top + 153, card.Right - 8, card.Top + 173), SKTextAlign.Center);

            var completed = new SKRect(card.Left + 10, card.Top + 184, card.Right - 10, card.Top + 257);
            DrawBox(canvas, completed, colors);
            DrawText(canvas, "今日完成", 7, false, colors.SecondaryText,
                new SKRect(completed.Left, completed.Top + 10, completed.Right, completed.Top + 28), SKTextAlign.Center);
            DrawText(canvas, snapshot.CompletedFocusSessions.ToString(), 28, true, accent,
                new SKRect(completed.Left, completed.Top + 28, completed.Right, completed.Bottom - 8), SKTextAlign.Center);
            DrawText(canvas, snapshot.IsRunning && snapshot.EndsAt is { } end
                    ? $"结束 {end.LocalDateTime:HH:mm}" : $"现在 {now.LocalDateTime:HH:mm}",
                8, false, colors.SecondaryText,
                new SKRect(card.Left + 8, card.Bottom - 46, card.Right - 8, card.Bottom - 26), SKTextAlign.Center);
            DrawText(canvas, "POMODORO", 7, false, accent,
                new SKRect(card.Left + 8, card.Bottom - 24, card.Right - 8, card.Bottom - 8), SKTextAlign.Center);
        }
        return Encode(bitmap, settings.JpegQuality);
    }

    public static byte[] RenderSystem(SystemSnapshot snapshot, AppSettings settings)
    {
        var colors = ScreenThemes.Get(settings.CardTheme);
        using var bitmap = CreateCanvas(settings.SafeAreaHeight, colors, out var canvas, out var card);
        using (canvas)
        {
            DrawHeader(canvas, card, "SYSTEM", "LIVE", colors.Accent, colors);
            DrawLine(canvas, card.Left + 9, card.Top + 38, card.Right - 9, card.Top + 38, colors.Border);
            DrawText(canvas, "CPU", 7.5f, false, colors.SecondaryText,
                new SKRect(card.Left + 12, card.Top + 50, card.Right - 12, card.Top + 69), SKTextAlign.Left);
            DrawText(canvas, $"{snapshot.CpuPercent:0}%", 24, true, colors.PrimaryText,
                new SKRect(card.Left + 8, card.Top + 62, card.Right - 10, card.Top + 98), SKTextAlign.Right);
            DrawProgress(canvas, new SKRect(card.Left + 12, card.Top + 105, card.Right - 12, card.Top + 112),
                snapshot.CpuPercent / 100, colors.Accent, colors.Border);

            DrawText(canvas, "内存", 7.5f, false, colors.SecondaryText,
                new SKRect(card.Left + 12, card.Top + 124, card.Left + 50, card.Top + 145), SKTextAlign.Left);
            DrawText(canvas, $"{snapshot.MemoryPercent:0}%", 11, true, colors.PrimaryText,
                new SKRect(card.Left + 46, card.Top + 122, card.Right - 12, card.Top + 145), SKTextAlign.Right);
            DrawProgress(canvas, new SKRect(card.Left + 12, card.Top + 151, card.Right - 12, card.Top + 158),
                snapshot.MemoryPercent / 100, colors.SecondaryAccent, colors.Border);
            DrawText(canvas, $"{ToGiB(snapshot.UsedMemoryBytes):0.0} / {ToGiB(snapshot.TotalMemoryBytes):0.0} GB",
                7.5f, false, colors.SecondaryText,
                new SKRect(card.Left + 8, card.Top + 163, card.Right - 8, card.Top + 180), SKTextAlign.Center);

            var network = new SKRect(card.Left + 10, card.Top + 194, card.Right - 10, card.Top + 285);
            DrawBox(canvas, network, colors);
            DrawText(canvas, "网络速率", 7.5f, false, colors.SecondaryText,
                new SKRect(network.Left + 9, network.Top + 6, network.Right - 9, network.Top + 25), SKTextAlign.Left);
            DrawText(canvas, "↓", 11, true, colors.Accent,
                new SKRect(network.Left + 9, network.Top + 30, network.Left + 28, network.Top + 52), SKTextAlign.Left);
            DrawText(canvas, FormatRate(snapshot.DownloadBytesPerSecond), 11, true, colors.PrimaryText,
                new SKRect(network.Left + 29, network.Top + 29, network.Right - 8, network.Top + 52), SKTextAlign.Right);
            DrawText(canvas, "↑", 11, true, colors.SecondaryAccent,
                new SKRect(network.Left + 9, network.Top + 58, network.Left + 28, network.Top + 80), SKTextAlign.Left);
            DrawText(canvas, FormatRate(snapshot.UploadBytesPerSecond), 11, true, colors.PrimaryText,
                new SKRect(network.Left + 29, network.Top + 57, network.Right - 8, network.Top + 80), SKTextAlign.Right);

            var uptime = snapshot.Uptime.TotalDays >= 1
                ? $"运行 {(int)snapshot.Uptime.TotalDays}天 {snapshot.Uptime.Hours}时"
                : $"运行 {(int)snapshot.Uptime.TotalHours}时 {snapshot.Uptime.Minutes}分";
            DrawText(canvas, uptime, 7.5f, false, colors.SecondaryText,
                new SKRect(card.Left + 8, card.Bottom - 47, card.Right - 8, card.Bottom - 29), SKTextAlign.Center);
            DrawText(canvas, snapshot.SampledAt.LocalDateTime.ToString("HH:mm:ss"), 7.5f, false,
                colors.SecondaryText,
                new SKRect(card.Left + 8, card.Bottom - 26, card.Right - 8, card.Bottom - 8), SKTextAlign.Center);
        }
        return Encode(bitmap, settings.JpegQuality);
    }

    public static byte[] RenderCustomImage(string path, AppSettings settings)
    {
        using var source = SKBitmap.Decode(path) ?? throw new InvalidOperationException("无法读取自定义图片。");
        using var bitmap = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        var safe = Math.Clamp(settings.SafeAreaHeight, 44, 80);
        var target = new SKRect(0, safe, Width, Height);
        var sourceRatio = (double)source.Width / source.Height;
        var targetRatio = (double)target.Width / target.Height;
        SKRect sourceRect;
        if (sourceRatio > targetRatio)
        {
            var cropWidth = (float)(source.Height * targetRatio);
            sourceRect = new SKRect((source.Width - cropWidth) / 2f, 0,
                (source.Width + cropWidth) / 2f, source.Height);
        }
        else
        {
            var cropHeight = (float)(source.Width / targetRatio);
            sourceRect = new SKRect(0, (source.Height - cropHeight) / 2f,
                source.Width, (source.Height + cropHeight) / 2f);
        }
#pragma warning disable CS0618
        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
        canvas.DrawBitmap(source, sourceRect, target, paint);
#pragma warning restore CS0618
        return Encode(bitmap, settings.JpegQuality);
    }

    public static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024) return $"{bytesPerSecond / 1024 / 1024:0.0} MB/s";
        if (bytesPerSecond >= 1024) return $"{bytesPerSecond / 1024:0.0} KB/s";
        return $"{bytesPerSecond:0} B/s";
    }

    private static SKBitmap CreateCanvas(int safeAreaHeight, ScreenPalette colors,
        out SKCanvas canvas, out SKRect card)
    {
        var bitmap = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        canvas = new SKCanvas(bitmap);
        canvas.Clear(colors.Background);
        var safe = Math.Clamp(safeAreaHeight, 44, 80);
        card = new SKRect(9, safe + 1, 133, Height - 9);
        using var paint = new SKPaint { Color = colors.Card, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawRoundRect(card, 13, 13, paint);
        paint.Color = colors.Border;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        canvas.DrawRoundRect(card, 13, 13, paint);
        return bitmap;
    }

    private static byte[] Encode(SKBitmap bitmap, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 50, 100))
            ?? throw new InvalidOperationException("无法编码 JPEG 图片。");
        var result = data.ToArray();
        if (result.Length > MaximumFileSize)
            throw new InvalidOperationException($"屏幕图片为 {result.Length} 字节，超过 512KB 限制。");
        return result;
    }

    private static void DrawHeader(SKCanvas canvas, SKRect card, string title, string badge,
        SKColor accent, ScreenPalette colors)
    {
        using var paint = new SKPaint { Color = accent, IsAntialias = true };
        canvas.DrawCircle(card.Left + 13.5f, card.Top + 18.5f, 3.5f, paint);
        DrawText(canvas, title, 10, true, colors.PrimaryText,
            new SKRect(card.Left + 22, card.Top + 8, card.Left + 84, card.Top + 33), SKTextAlign.Left);
        DrawText(canvas, badge, 6, true, accent,
            new SKRect(card.Right - 43, card.Top + 10, card.Right - 10, card.Top + 30), SKTextAlign.Right);
    }

    private static void DrawBox(SKCanvas canvas, SKRect bounds, ScreenPalette colors)
    {
        using var paint = new SKPaint { Color = colors.Inset, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawRoundRect(bounds, 10, 10, paint);
        paint.Color = colors.Border;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        canvas.DrawRoundRect(bounds, 10, 10, paint);
    }

    private static void DrawProgress(SKCanvas canvas, SKRect bounds, double progress,
        SKColor accent, SKColor background)
    {
        using var paint = new SKPaint { Color = background, IsAntialias = true };
        canvas.DrawRoundRect(bounds, bounds.Height / 2, bounds.Height / 2, paint);
        var width = bounds.Width * (float)Math.Clamp(progress, 0, 1);
        if (width <= 0) return;
        var filled = new SKRect(bounds.Left, bounds.Top, bounds.Left + Math.Max(bounds.Height, width), bounds.Bottom);
        paint.Color = accent;
        canvas.DrawRoundRect(filled, bounds.Height / 2, bounds.Height / 2, paint);
    }

    private static void DrawLine(SKCanvas canvas, float x1, float y1, float x2, float y2, SKColor color)
    {
        using var paint = new SKPaint { Color = color, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    private static void DrawText(SKCanvas canvas, string text, float size, bool bold, SKColor color,
        SKRect bounds, SKTextAlign alignment)
    {
        using var typeface = SKTypeface.FromFamilyName(FontFamily(), bold ? SKFontStyle.Bold : SKFontStyle.Normal)
            ?? SKTypeface.FromFamilyName(null, bold ? SKFontStyle.Bold : SKFontStyle.Normal);
        if (typeface is null) throw new InvalidOperationException("当前系统没有可用字体。");
        using var font = new SKFont(typeface, size) { Embolden = bold };
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        text = TrimText(text, bounds.Width, font);
        var metrics = font.Metrics;
        var baseline = bounds.MidY - (metrics.Ascent + metrics.Descent) / 2;
        var x = alignment switch
        {
            SKTextAlign.Left => bounds.Left,
            SKTextAlign.Right => bounds.Right,
            _ => bounds.MidX
        };
        canvas.DrawText(text, x, baseline, alignment, font, paint);
    }

    private static string TrimText(string text, float width, SKFont font)
    {
        if (font.MeasureText(text) <= width) return text;
        const string ellipsis = "…";
        while (text.Length > 1 && font.MeasureText(text + ellipsis) > width)
            text = text[..^1];
        return text + ellipsis;
    }

    private static string FontFamily() => OperatingSystem.IsWindows() ? "Microsoft YaHei UI"
        : OperatingSystem.IsMacOS() ? "PingFang SC"
        : "Noto Sans CJK SC";

    private static double ToGiB(ulong bytes) => bytes / 1024d / 1024d / 1024d;
}
