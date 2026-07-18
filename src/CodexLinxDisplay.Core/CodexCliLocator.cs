namespace CodexLinxDisplay.Core;

internal static class CodexCliLocator
{
    public static string Resolve(string? applicationSetting = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = BuildCandidatePaths(
            applicationSetting,
            Environment.GetEnvironmentVariable("CODEX_CLI_PATH"),
            Environment.GetEnvironmentVariable("PATH"),
            home,
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS());

        foreach (var candidate in candidates.Concat(EnumerateVersionManagerPaths(home)))
        {
            if (File.Exists(candidate)) return candidate;
        }

        var guidance = OperatingSystem.IsMacOS()
            ? "请在“Codex CLI”一栏填写终端执行 which codex 返回的完整路径（例如 /opt/homebrew/bin/codex）。"
            : "请安装 Codex，或在“Codex CLI”一栏填写可执行文件的完整路径。";
        throw new FileNotFoundException($"未找到 Codex CLI。已检查应用设置、CODEX_CLI_PATH、PATH 和常见安装位置。{guidance}");
    }

    internal static IReadOnlyList<string> BuildCandidatePaths(
        string? applicationSetting,
        string? environmentSetting,
        string? path,
        string home,
        bool isWindows,
        bool isMacOS)
    {
        var names = isWindows
            ? new[] { "codex.exe", "codex.cmd", "codex.bat" }
            : new[] { "codex" };
        var candidates = new List<string>();

        AddConfiguredPath(candidates, applicationSetting, home);
        AddConfiguredPath(candidates, environmentSetting, home);
        foreach (var directory in (path ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
                candidates.Add(Path.Combine(directory.Trim('"'), name));
        }

        if (isWindows)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(localAppData, "Microsoft", "WindowsApps", "codex.exe"));
            candidates.Add(Path.Combine(localAppData, "Programs", "Codex", "codex.exe"));
        }
        else
        {
            candidates.Add("/opt/homebrew/bin/codex");
            candidates.Add("/usr/local/bin/codex");
            candidates.Add("/home/linuxbrew/.linuxbrew/bin/codex");
            candidates.Add(Path.Combine(home, ".local", "bin", "codex"));
            candidates.Add(Path.Combine(home, ".npm-global", "bin", "codex"));
            candidates.Add(Path.Combine(home, ".volta", "bin", "codex"));
            candidates.Add(Path.Combine(home, ".asdf", "shims", "codex"));
            candidates.Add(Path.Combine(home, ".local", "share", "mise", "shims", "codex"));
            candidates.Add(Path.Combine(home, ".local", "share", "pnpm", "codex"));

            if (isMacOS)
            {
                candidates.Add(Path.Combine(home, "Library", "pnpm", "codex"));
            }
        }

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddConfiguredPath(List<string> candidates, string? value, string home)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var path = value.Trim().Trim('"');
        if (path == "~") path = home;
        else if (path.StartsWith("~/", StringComparison.Ordinal))
            path = Path.Combine(home, path[2..]);
        candidates.Add(path);
    }

    private static IEnumerable<string> EnumerateVersionManagerPaths(string home)
    {
        if (string.IsNullOrWhiteSpace(home)) yield break;

        foreach (var candidate in EnumerateVersionDirectories(
                     Path.Combine(home, ".nvm", "versions", "node"),
                     version => Path.Combine(version, "bin", "codex")))
            yield return candidate;

        var fnmRoots = new[]
        {
            Path.Combine(home, ".local", "share", "fnm", "node-versions"),
            Path.Combine(home, "Library", "Application Support", "fnm", "node-versions")
        };
        foreach (var root in fnmRoots)
        {
            foreach (var candidate in EnumerateVersionDirectories(
                         root,
                         version => Path.Combine(version, "installation", "bin", "codex")))
                yield return candidate;
        }
    }

    private static IEnumerable<string> EnumerateVersionDirectories(
        string root,
        Func<string, string> candidateFactory)
    {
        if (!Directory.Exists(root)) yield break;
        string[] directories;
        try { directories = Directory.GetDirectories(root); }
        catch { yield break; }

        foreach (var directory in directories.OrderByDescending(GetLastWriteTimeUtcSafely))
            yield return candidateFactory(directory);
    }

    private static DateTime GetLastWriteTimeUtcSafely(string path)
    {
        try { return Directory.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }
}
