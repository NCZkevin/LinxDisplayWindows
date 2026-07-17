using System.Diagnostics;
using System.Text.Json;
using CodexLinxDisplay.Windows.Models;

namespace CodexLinxDisplay.Windows.Services;

internal sealed class CodexRateLimitClient
{
    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));

        using var process = StartCodex();
        try
        {
            await SendAsync(process, new
            {
                method = "initialize",
                id = 0,
                @params = new
                {
                    clientInfo = new
                    {
                        name = "codex_linx_display_windows",
                        title = "Codex Linx Display for Windows",
                        version = "0.2.0"
                    },
                    capabilities = new { experimentalApi = true }
                }
            }, timeout.Token);

            var didRequestRateLimits = false;
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                if (line is null)
                {
                    var error = await process.StandardError.ReadToEndAsync(timeout.Token);
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? "Codex 数据连接意外关闭。"
                        : $"Codex 启动失败：{error.Trim()}");
                }

                if (string.IsNullOrWhiteSpace(line)) continue;
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!TryGetId(root, out var id)) continue;

                if (id == 0 && !didRequestRateLimits)
                {
                    ThrowIfServerError(root);
                    didRequestRateLimits = true;
                    await SendAsync(process, new { method = "initialized", @params = new { } }, timeout.Token);
                    await SendAsync(process,
                        new { method = "account/rateLimits/read", id = 1, @params = (object?)null },
                        timeout.Token);
                    continue;
                }

                if (id != 1) continue;
                ThrowIfServerError(root);
                if (!root.TryGetProperty("result", out var result))
                    throw new InvalidOperationException("Codex 返回了无法识别的用量数据。");
                return ParseSnapshot(result);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("读取 Codex 用量超时。");
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(true); } catch { /* Process is already exiting. */ }
            }
        }
    }

    private static Process StartCodex()
    {
        var executable = ResolveExecutable();
        var isCommandScript = executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                              || executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            FileName = isCommandScript
                ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe"
                : executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (isCommandScript)
        {
            // cmd.exe requires one outer quote pair around a command whose executable is quoted.
            // ProcessStartInfo.ArgumentList escapes those quotes as literal characters, so use the
            // raw Arguments string for .cmd/.bat wrappers.
            startInfo.Arguments = $"/d /s /c \"\"{executable}\" app-server --stdio\"";
        }
        else
        {
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--stdio");
        }

        try
        {
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException("Codex 进程未能启动。");
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                $"Codex 启动失败：{error.Message}。可用 CODEX_CLI_PATH 指定 codex.exe。", error);
        }
    }

    private static string ResolveExecutable()
    {
        var configuredPath = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        var executableNames = OperatingSystem.IsWindows()
            ? new[] { "codex.exe", "codex.cmd", "codex.bat" }
            : new[] { "codex" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in executableNames)
            {
                var candidate = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var aliases = new[]
        {
            Path.Combine(localAppData, "Microsoft", "WindowsApps", "codex.exe"),
            Path.Combine(localAppData, "Programs", "Codex", "codex.exe")
        };
        var alias = aliases.FirstOrDefault(File.Exists);
        if (alias is not null) return alias;

        throw new FileNotFoundException(
            "未找到 Codex CLI。请安装 Codex，或把 CODEX_CLI_PATH 环境变量指向 codex.exe。");
    }

    private static async Task SendAsync(Process process, object message, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static bool TryGetId(JsonElement root, out int id)
    {
        id = default;
        return root.TryGetProperty("id", out var idElement)
               && idElement.ValueKind == JsonValueKind.Number
               && idElement.TryGetInt32(out id);
    }

    private static void ThrowIfServerError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind == JsonValueKind.Null) return;
        var message = error.TryGetProperty("message", out var value) ? value.GetString() : null;
        throw new InvalidOperationException($"Codex 返回错误：{message ?? "未知错误"}");
    }

    private static UsageSnapshot ParseSnapshot(JsonElement result)
    {
        JsonElement limits = default;
        var hasLimits = false;
        if (result.TryGetProperty("rateLimitsByLimitId", out var byId)
            && byId.ValueKind == JsonValueKind.Object
            && byId.TryGetProperty("codex", out limits))
        {
            hasLimits = true;
        }
        else if (result.TryGetProperty("rateLimits", out limits)
                 && limits.ValueKind == JsonValueKind.Object)
        {
            hasLimits = true;
        }

        if (!hasLimits) throw new InvalidOperationException("Codex 返回了无法识别的用量数据。");

        var windows = new List<JsonElement>();
        if (limits.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.Object)
            windows.Add(primary);
        if (limits.TryGetProperty("secondary", out var secondary) && secondary.ValueKind == JsonValueKind.Object)
            windows.Add(secondary);
        if (windows.Count == 0) throw new InvalidOperationException("Codex 返回了无法识别的用量数据。");

        var selected = windows.MaxBy(GetWindowMinutes);
        var usedPercent = selected.TryGetProperty("usedPercent", out var used)
            ? (int)Math.Round(used.GetDouble())
            : throw new InvalidOperationException("Codex 返回的用量数据缺少 usedPercent。");
        var windowMinutes = GetWindowMinutes(selected);

        DateTimeOffset? resetDate = null;
        if (selected.TryGetProperty("resetsAt", out var reset) && reset.TryGetInt64(out var resetSeconds))
            resetDate = DateTimeOffset.FromUnixTimeSeconds(resetSeconds);

        var resetCount = 0;
        if (result.TryGetProperty("rateLimitResetCredits", out var credits)
            && credits.ValueKind == JsonValueKind.Object
            && credits.TryGetProperty("availableCount", out var count)
            && count.TryGetInt32(out var parsedCount))
            resetCount = Math.Max(0, parsedCount);

        var planType = limits.TryGetProperty("planType", out var plan) ? plan.GetString() : null;
        return new UsageSnapshot(
            Math.Clamp(100 - usedPercent, 0, 100),
            resetDate,
            windowMinutes == 0 ? null : windowMinutes,
            resetCount,
            planType);
    }

    private static int GetWindowMinutes(JsonElement window) =>
        window.TryGetProperty("windowDurationMins", out var duration) && duration.TryGetInt32(out var minutes)
            ? minutes
            : 0;
}
