using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LinxDisplay.Windows;
using LinxDisplay.Windows.Models;
using LinxDisplay.Windows.Services;

await TestRendererAsync();
TestThemes();
TestPomodoro();
TestSystemMonitor();
await TestImageApiAsync();

var snapshotArgument = args.FirstOrDefault(value =>
    value.StartsWith("--ui-snapshot=", StringComparison.OrdinalIgnoreCase));
if (snapshotArgument is not null)
{
    var outputPath = snapshotArgument[(snapshotArgument.IndexOf('=') + 1)..];
    TestUserInterface(outputPath, DisplayMode.Codex);
    var directory = Path.GetDirectoryName(outputPath) ?? ".";
    var name = Path.GetFileNameWithoutExtension(outputPath);
    TestUserInterface(Path.Combine(directory, name + "-pomodoro.png"), DisplayMode.Pomodoro);
    TestUserInterface(Path.Combine(directory, name + "-system.png"), DisplayMode.SystemMonitor);
}

var galleryArgument = args.FirstOrDefault(value =>
    value.StartsWith("--theme-gallery=", StringComparison.OrdinalIgnoreCase));
if (galleryArgument is not null)
    SaveThemeGallery(galleryArgument[(galleryArgument.IndexOf('=') + 1)..]);

if (args.Contains("--codex", StringComparer.OrdinalIgnoreCase))
{
    var snapshot = await new CodexRateLimitClient().FetchAsync();
    Assert(snapshot.RemainingPercent is >= 0 and <= 100, "Codex remaining percent is invalid.");
    Console.WriteLine($"Codex protocol: OK ({snapshot.WindowTitle} {snapshot.RemainingPercent}%)");
}

Console.WriteLine("All Windows smoke tests passed.");

static Task TestRendererAsync()
{
    var renderedAt = new DateTimeOffset(2026, 7, 18, 2, 14, 0, TimeSpan.FromHours(8));
    using var usage = ScreenImageRenderer.RenderUsage(UsageSnapshot.Sample, 56, renderedAt);
    Assert(usage.Width == 142 && usage.Height == 428, "Usage image dimensions are invalid.");
    var usageJpeg = ScreenImageRenderer.EncodeJpeg(usage, 90);
    Assert(usageJpeg.Length is > 0 and <= ScreenImageRenderer.MaximumFileSize,
        "Usage JPEG size is invalid.");
    using (var stream = new MemoryStream(usageJpeg))
    using (var decoded = Image.FromStream(stream))
        Assert(decoded.RawFormat.Guid == ImageFormat.Jpeg.Guid, "Usage image is not JPEG.");

    using var nextMinute = ScreenImageRenderer.RenderUsage(
        UsageSnapshot.Sample, 56, renderedAt.AddMinutes(1));
    var nextMinuteJpeg = ScreenImageRenderer.EncodeJpeg(nextMinute, 90);
    Assert(!usageJpeg.SequenceEqual(nextMinuteJpeg), "Current time did not change the rendered card.");

    using var source = new Bitmap(640, 360);
    using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.DodgerBlue);
    using var custom = ScreenImageRenderer.RenderCustomImage(source, 56);
    Assert(custom.GetPixel(10, 10).ToArgb() == Color.Black.ToArgb(), "Safe area is not black.");
    Assert(custom.GetPixel(71, 200).B > 150, "Custom image content was not rendered.");
    var customJpeg = ScreenImageRenderer.EncodeJpeg(custom, 90);
    Assert(customJpeg.Length is > 0 and <= ScreenImageRenderer.MaximumFileSize,
        "Custom JPEG size is invalid.");

    Console.WriteLine($"Renderer: OK (usage {usageJpeg.Length} bytes, custom {customJpeg.Length} bytes)");
    return Task.CompletedTask;
}

static void TestPomodoro()
{
    var state = new PomodoroState
    {
        TaskName = "写测试",
        FocusMinutes = 1,
        ShortBreakMinutes = 1,
        LongBreakMinutes = 2
    };
    var service = new PomodoroService(state);
    var startedAt = new DateTimeOffset(2026, 7, 18, 2, 14, 0, TimeSpan.FromHours(8));
    service.StartOrResume(startedAt);
    Assert(state.Phase == PomodoroPhase.Focus, "Pomodoro did not start a focus session.");
    service.Pause(startedAt.AddSeconds(10));
    Assert(state.Phase == PomodoroPhase.Paused && state.PausedRemainingSeconds == 50,
        "Pomodoro pause did not preserve remaining time.");
    service.StartOrResume(startedAt.AddSeconds(20));
    Assert(service.Tick(startedAt.AddSeconds(70)), "Pomodoro deadline did not advance the phase.");
    Assert(state.Phase == PomodoroPhase.ShortBreak && state.CompletedFocusSessions == 1,
        "Pomodoro did not start a short break.");

    var now = startedAt.AddSeconds(70);
    for (var completed = 2; completed <= 4; completed++)
    {
        service.Skip(now); // break -> focus
        service.Skip(now); // focus -> break
    }
    Assert(state.Phase == PomodoroPhase.LongBreak && state.CompletedFocusSessions == 4,
        "Pomodoro did not start a long break after four sessions.");

    using var card = StatusCardRenderer.RenderPomodoro(service.GetSnapshot(now), 56, now);
    var jpeg = ScreenImageRenderer.EncodeJpeg(card, 90);
    Assert(card.Width == 142 && card.Height == 428 && jpeg.Length > 0,
        "Pomodoro card is invalid.");
    Console.WriteLine($"Pomodoro: OK ({jpeg.Length} bytes)");
}

static void TestThemes()
{
    var renderedAt = new DateTimeOffset(2026, 7, 18, 2, 14, 0, TimeSpan.FromHours(8));
    var pomodoro = new PomodoroSnapshot(PomodoroPhase.Focus, PomodoroPhase.Focus, "设计新主题",
        TimeSpan.FromMinutes(18), TimeSpan.FromMinutes(25), 3, renderedAt.AddMinutes(18));
    var system = new SystemSnapshot(42, 63, 20UL * 1024 * 1024 * 1024,
        32UL * 1024 * 1024 * 1024, 2.4 * 1024 * 1024, 384 * 1024,
        TimeSpan.FromHours(31), renderedAt);
    var hashes = new HashSet<string>(StringComparer.Ordinal);

    foreach (var theme in Enum.GetValues<CardTheme>())
    {
        using var usage = ScreenImageRenderer.RenderUsage(UsageSnapshot.Sample, 56, renderedAt, theme);
        using var timer = StatusCardRenderer.RenderPomodoro(pomodoro, 56, renderedAt, theme);
        using var monitor = StatusCardRenderer.RenderSystem(system, 56, theme);
        foreach (var image in new[] { usage, timer, monitor })
        {
            Assert(image.Width == 142 && image.Height == 428, $"{theme} card dimensions are invalid.");
            var jpeg = ScreenImageRenderer.EncodeJpeg(image, 90);
            Assert(jpeg.Length is > 0 and <= ScreenImageRenderer.MaximumFileSize,
                $"{theme} JPEG size is invalid.");
        }
        hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            ScreenImageRenderer.EncodeJpeg(usage, 90))));
    }

    Assert(hashes.Count == Enum.GetValues<CardTheme>().Length,
        "Theme renders are not visually distinct.");
    var dark = ScreenThemes.Get(CardTheme.DeepSpace);
    var light = ScreenThemes.Get(CardTheme.MinimalLight);
    Assert(light.Background.GetBrightness() > dark.Background.GetBrightness(),
        "Light theme palette is not brighter than dark theme palette.");
    Console.WriteLine("Themes: OK (4 palettes, 12 cards)");
}

static void SaveThemeGallery(string outputPath)
{
    var renderedAt = new DateTimeOffset(2026, 7, 18, 2, 14, 0, TimeSpan.FromHours(8));
    var pomodoro = new PomodoroSnapshot(PomodoroPhase.Focus, PomodoroPhase.Focus, "设计新主题",
        TimeSpan.FromMinutes(18), TimeSpan.FromMinutes(25), 3, renderedAt.AddMinutes(18));
    var system = new SystemSnapshot(42, 63, 20UL * 1024 * 1024 * 1024,
        32UL * 1024 * 1024 * 1024, 2.4 * 1024 * 1024, 384 * 1024,
        TimeSpan.FromHours(31), renderedAt);
    var themes = Enum.GetValues<CardTheme>();
    using var gallery = new Bitmap(ScreenImageRenderer.Width * themes.Length, ScreenImageRenderer.Height * 3);
    using (var graphics = Graphics.FromImage(gallery))
    {
        for (var column = 0; column < themes.Length; column++)
        {
            var theme = themes[column];
            using var usage = ScreenImageRenderer.RenderUsage(UsageSnapshot.Sample, 56, renderedAt, theme);
            using var timer = StatusCardRenderer.RenderPomodoro(pomodoro, 56, renderedAt, theme);
            using var monitor = StatusCardRenderer.RenderSystem(system, 56, theme);
            graphics.DrawImageUnscaled(usage, column * ScreenImageRenderer.Width, 0);
            graphics.DrawImageUnscaled(timer, column * ScreenImageRenderer.Width, ScreenImageRenderer.Height);
            graphics.DrawImageUnscaled(monitor, column * ScreenImageRenderer.Width, ScreenImageRenderer.Height * 2);
        }
    }
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
    gallery.Save(outputPath, ImageFormat.Png);
    Console.WriteLine($"Theme gallery: OK ({outputPath})");
}

static void TestSystemMonitor()
{
    var monitor = new SystemMonitorService();
    _ = monitor.Sample();
    Thread.Sleep(80);
    var snapshot = monitor.Sample();
    Assert(snapshot.CpuPercent is >= 0 and <= 100, "CPU percentage is invalid.");
    Assert(snapshot.MemoryPercent is >= 0 and <= 100 && snapshot.TotalMemoryBytes > 0,
        "Memory sample is invalid.");
    Assert(snapshot.DownloadBytesPerSecond >= 0 && snapshot.UploadBytesPerSecond >= 0,
        "Network sample is invalid.");
    using var card = StatusCardRenderer.RenderSystem(snapshot, 56);
    var jpeg = ScreenImageRenderer.EncodeJpeg(card, 90);
    Assert(card.Width == 142 && card.Height == 428 && jpeg.Length > 0,
        "System monitor card is invalid.");
    Assert(StatusCardRenderer.FormatRate(1024 * 1024).Contains("MB/s"),
        "Network rate formatter is invalid.");
    Console.WriteLine($"System monitor: OK (CPU {snapshot.CpuPercent:0.0}%, RAM {snapshot.MemoryPercent:0.0}%, {jpeg.Length} bytes)");
}

static async Task TestImageApiAsync()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var receivedContentType = string.Empty;
    byte[] receivedBody = [];

    var server = Task.Run(async () =>
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var headerBytes = new List<byte>();
        while (headerBytes.Count < 16 * 1024)
        {
            var value = stream.ReadByte();
            if (value < 0) break;
            headerBytes.Add((byte)value);
            var count = headerBytes.Count;
            if (count >= 4 && headerBytes[count - 4] == '\r' && headerBytes[count - 3] == '\n'
                           && headerBytes[count - 2] == '\r' && headerBytes[count - 1] == '\n')
                break;
        }

        var headers = Encoding.ASCII.GetString(headerBytes.ToArray());
        var contentLength = 0;
        foreach (var line in headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator < 0) continue;
            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(value);
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                receivedContentType = value;
        }

        receivedBody = new byte[contentLength];
        var offset = 0;
        while (offset < receivedBody.Length)
        {
            var count = await stream.ReadAsync(receivedBody.AsMemory(offset));
            if (count == 0) break;
            offset += count;
        }

        var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK");
        await stream.WriteAsync(response);
    });

    byte[] payload = [0xFF, 0xD8, 0xFF, 0xD9];
    using var api = new ImageApiClient();
    var status = await api.UploadAsync(payload, $"http://127.0.0.1:{port}/image/upload");
    await server;
    Assert(status == 200, "Image API status is invalid.");
    Assert(receivedContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase),
        "Image API content type is invalid.");
    Assert(receivedBody.SequenceEqual(payload), "Image API payload is invalid.");
    Console.WriteLine("Image API: OK (raw image/jpeg POST)");
}

static void TestUserInterface(string outputPath, DisplayMode mode)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            using var form = new MainForm(testMode: true, initialMode: mode);
            form.Show();
            Application.DoEvents();
            form.PerformLayout();
            using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
            form.Hide();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            bitmap.Save(outputPath, ImageFormat.Png);
        }
        catch (Exception error)
        {
            failure = error;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw new InvalidOperationException("UI snapshot failed.", failure);
    Console.WriteLine($"User interface: OK ({outputPath})");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
