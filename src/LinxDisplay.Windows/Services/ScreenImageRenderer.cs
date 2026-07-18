using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using LinxDisplay.Windows.Models;

namespace LinxDisplay.Windows.Services;

internal static class ScreenImageRenderer
{
    public const int Width = 142;
    public const int Height = 428;
    public const int DefaultSafeArea = 56;
    public const int MaximumFileSize = 512 * 1024;

    public static Bitmap RenderUsage(
        UsageSnapshot? snapshot,
        int safeAreaHeight,
        DateTimeOffset? currentTime = null,
        CardTheme theme = CardTheme.DeepSpace)
    {
        var colors = ScreenThemes.Get(theme);
        var safeArea = Math.Clamp(safeAreaHeight, 44, 80);
        var bitmap = NewBitmap();
        using var graphics = PrepareGraphics(bitmap);
        graphics.Clear(colors.Background);

        var cardBounds = new Rectangle(9, safeArea + 1, 124, Math.Max(320, Height - safeArea - 10));
        using (var cardPath = RoundedRectangle(cardBounds, 13))
        using (var fill = new SolidBrush(colors.Card))
        using (var borderPen = new Pen(colors.Border))
        {
            graphics.FillPath(fill, cardPath);
            graphics.DrawPath(borderPen, cardPath);
        }

        DrawHeader(graphics, cardBounds, snapshot is not null, colors);
        using (var pen = new Pen(colors.Border))
            graphics.DrawLine(pen, cardBounds.Left + 9, cardBounds.Top + 38,
                cardBounds.Right - 9, cardBounds.Top + 38);
        DrawUsage(graphics, cardBounds, snapshot, colors);
        DrawResetCard(graphics, cardBounds, snapshot, colors);
        DrawClock(graphics, cardBounds, snapshot, currentTime ?? DateTimeOffset.Now, colors);
        return bitmap;
    }

    public static Bitmap RenderCustomImage(Image source, int safeAreaHeight)
    {
        var safeArea = Math.Clamp(safeAreaHeight, 44, 80);
        var bitmap = NewBitmap();
        using var graphics = PrepareGraphics(bitmap);
        graphics.Clear(Color.Black);

        var target = new Rectangle(0, safeArea, Width, Height - safeArea);
        var sourceRatio = (double)source.Width / source.Height;
        var targetRatio = (double)target.Width / target.Height;
        RectangleF crop;
        if (sourceRatio > targetRatio)
        {
            var cropWidth = (float)(source.Height * targetRatio);
            crop = new RectangleF((source.Width - cropWidth) / 2f, 0, cropWidth, source.Height);
        }
        else
        {
            var cropHeight = (float)(source.Width / targetRatio);
            crop = new RectangleF(0, (source.Height - cropHeight) / 2f, source.Width, cropHeight);
        }

        graphics.DrawImage(source, target, crop.X, crop.Y, crop.Width, crop.Height, GraphicsUnit.Pixel);
        return bitmap;
    }

    public static byte[] EncodeJpeg(Image image, int quality)
    {
        if (image.Width != Width || image.Height != Height)
            throw new InvalidOperationException($"屏幕图片尺寸不是 {Width}×{Height}。");

        var jpegEncoder = ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 50, 100));
        using var stream = new MemoryStream();
        image.Save(stream, jpegEncoder, parameters);
        var data = stream.ToArray();
        if (data.Length > MaximumFileSize)
            throw new InvalidOperationException($"屏幕图片为 {data.Length} 字节，超过 512KB 限制。");
        return data;
    }

    public static Bitmap LoadDetached(string path)
    {
        using var source = Image.FromFile(path);
        return new Bitmap(source);
    }

    private static Bitmap NewBitmap() => new(Width, Height, PixelFormat.Format24bppRgb);

    private static Graphics PrepareGraphics(Bitmap bitmap)
    {
        var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        return graphics;
    }

    private static void DrawHeader(Graphics graphics, Rectangle card, bool hasSnapshot, ScreenPalette colors)
    {
        using var accentBrush = new SolidBrush(colors.Accent);
        graphics.FillEllipse(accentBrush, card.Left + 9, card.Top + 15, 8, 8);
        DrawText(graphics, "CODEX", 15, FontStyle.Bold, colors.PrimaryText,
            new RectangleF(card.Left + 22, card.Top + 8, 62, 25), StringAlignment.Near);
        DrawText(graphics, hasSnapshot ? "实时" : "等待", 9, FontStyle.Bold,
            hasSnapshot ? colors.Accent : colors.TertiaryText,
            new RectangleF(card.Right - 40, card.Top + 10, 31, 20), StringAlignment.Far);
    }

    private static void DrawUsage(Graphics graphics, Rectangle card, UsageSnapshot? snapshot, ScreenPalette colors)
    {
        var top = card.Top + 39;
        DrawText(graphics, snapshot?.WindowTitle ?? "等待同步", 10, FontStyle.Bold, colors.SecondaryText,
            new RectangleF(card.Left + 9, top + 10, card.Width - 18, 18), StringAlignment.Center);

        DrawText(graphics, snapshot is null ? "--" : snapshot.RemainingPercent.ToString(), 38,
            FontStyle.Bold, colors.PrimaryText,
            new RectangleF(card.Left + 5, top + 24, card.Width - 29, 55), StringAlignment.Far);
        DrawText(graphics, "%", 11, FontStyle.Bold, colors.Accent,
            new RectangleF(card.Right - 25, top + 48, 17, 20), StringAlignment.Near);

        var progress = new Rectangle(card.Left + 9, top + 77, 106, 8);
        using (var path = RoundedRectangle(progress, 4))
        using (var brush = new SolidBrush(colors.Border))
            graphics.FillPath(brush, path);
        var progressWidth = (int)Math.Round(progress.Width * (snapshot?.RemainingPercent ?? 0) / 100d);
        if (progressWidth > 0)
        {
            using var path = RoundedRectangle(new Rectangle(progress.X, progress.Y, progressWidth, progress.Height), 4);
            using var brush = new SolidBrush(colors.Accent);
            graphics.FillPath(brush, path);
        }

        DrawText(graphics, snapshot?.WindowDescription ?? "尚无数据", 9, FontStyle.Regular,
            colors.TertiaryText, new RectangleF(card.Left + 9, top + 89, 106, 18), StringAlignment.Center);
    }

    private static void DrawResetCard(Graphics graphics, Rectangle card, UsageSnapshot? snapshot, ScreenPalette colors)
    {
        var bounds = new Rectangle(card.Left + 9, card.Top + 163, 106, 70);
        using (var path = RoundedRectangle(bounds, 10))
        using (var fill = new SolidBrush(colors.Inset))
        using (var pen = new Pen(colors.Border))
        {
            graphics.FillPath(fill, path);
            graphics.DrawPath(pen, path);
        }

        DrawText(graphics, "可用重置", 10, FontStyle.Bold, colors.SecondaryText,
            new RectangleF(bounds.Left + 11, bounds.Top + 8, 84, 18), StringAlignment.Near);
        DrawText(graphics, snapshot is null ? "--" : snapshot.AvailableResetCount.ToString(), 28,
            FontStyle.Bold, colors.PrimaryText,
            new RectangleF(bounds.Left + 10, bounds.Top + 25, 58, 39), StringAlignment.Near);
        DrawText(graphics, "次", 11, FontStyle.Bold, colors.Accent,
            new RectangleF(bounds.Right - 29, bounds.Top + 38, 18, 20), StringAlignment.Far);
    }

    private static void DrawClock(
        Graphics graphics,
        Rectangle card,
        UsageSnapshot? snapshot,
        DateTimeOffset currentTime,
        ScreenPalette colors)
    {
        var reset = snapshot?.ResetDate?.ToLocalTime();
        var now = currentTime.ToLocalTime();
        var bottom = card.Bottom - 15;
        var resetText = reset is null ? "重置时间未知" : $"下次重置 {reset:M/d HH:mm}";
        DrawText(graphics, resetText, 8, FontStyle.Regular, colors.TertiaryText,
            new RectangleF(card.Left + 7, bottom - 94, 110, 15), StringAlignment.Center);
        DrawText(graphics, "当前时间", 9, FontStyle.Bold, colors.TertiaryText,
            new RectangleF(card.Left + 9, bottom - 76, 106, 17), StringAlignment.Center);
        DrawText(graphics, now.ToString("M月d日"), 17, FontStyle.Bold, colors.PrimaryText,
            new RectangleF(card.Left + 5, bottom - 57, 114, 24), StringAlignment.Center);
        DrawText(graphics, now.ToString("HH:mm"), 22, FontStyle.Bold, colors.Accent,
            new RectangleF(card.Left + 5, bottom - 32, 114, 30), StringAlignment.Center);
    }

    private static void DrawText(Graphics graphics, string text, float size, FontStyle style, Color color,
        RectangleF bounds, StringAlignment alignment)
    {
        using var font = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = alignment,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
