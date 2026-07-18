using System.Net.Http.Headers;

namespace LinxDisplay.Windows.Services;

internal sealed class ImageApiClient : IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<int> UploadAsync(byte[] jpeg, string endpoint, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("图像 API 地址无效。");

        using var content = new ByteArrayContent(jpeg);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        try
        {
            using var response = await _client.PostAsync(uri, content, cancellationToken);
            var responseText = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    string.IsNullOrEmpty(responseText)
                        ? $"图像 API 返回 HTTP {(int)response.StatusCode}。"
                        : $"图像 API 返回 HTTP {(int)response.StatusCode}：{responseText}",
                    null,
                    response.StatusCode);
            return (int)response.StatusCode;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("连接键盘超时，请检查局域网和设备地址。");
        }
        catch (HttpRequestException error) when (error.StatusCode is null)
        {
            throw new HttpRequestException("无法连接键盘，请确认设备在线且 API 地址正确。", error);
        }
    }

    public void Dispose() => _client.Dispose();
}
