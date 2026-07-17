using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CodexLinxDisplay.Windows.Models;
using CodexLinxDisplay.Windows.Services;

await TestRendererAsync();
await TestImageApiAsync();

if (args.Contains("--codex", StringComparer.OrdinalIgnoreCase))
{
    var snapshot = await new CodexRateLimitClient().FetchAsync();
    Assert(snapshot.RemainingPercent is >= 0 and <= 100, "Codex remaining percent is invalid.");
    Console.WriteLine($"Codex protocol: OK ({snapshot.WindowTitle} {snapshot.RemainingPercent}%)");
}

Console.WriteLine("All Windows smoke tests passed.");

static Task TestRendererAsync()
{
    using var usage = ScreenImageRenderer.RenderUsage(UsageSnapshot.Sample, 56);
    Assert(usage.Width == 142 && usage.Height == 428, "Usage image dimensions are invalid.");
    var usageJpeg = ScreenImageRenderer.EncodeJpeg(usage, 90);
    Assert(usageJpeg.Length is > 0 and <= ScreenImageRenderer.MaximumFileSize,
        "Usage JPEG size is invalid.");
    using (var stream = new MemoryStream(usageJpeg))
    using (var decoded = Image.FromStream(stream))
        Assert(decoded.RawFormat.Guid == ImageFormat.Jpeg.Guid, "Usage image is not JPEG.");

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

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
