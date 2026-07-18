using System.Text.Json;

namespace LinxDisplay.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsStore() : this(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)) { }

    internal SettingsStore(string applicationDataDirectory)
    {
        DataDirectory = Path.Combine(applicationDataDirectory, "LinxDisplay");
        LegacyDataDirectory = Path.Combine(applicationDataDirectory, "CodexLinxDisplay");
        MigrateLegacyDataDirectory();
    }

    public string DataDirectory { get; }
    internal string LegacyDataDirectory { get; }
    public string SettingsPath => Path.Combine(DataDirectory, "settings-v2.json");
    public string LegacySettingsPath => Path.Combine(DataDirectory, "settings.json");
    public string PomodoroPath => Path.Combine(DataDirectory, "pomodoro.json");
    public string CustomImagePath => Path.Combine(DataDirectory, "custom-image.png");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                var migrated = LoadLegacySettings();
                if (migrated is not null)
                {
                    NormalizeCustomImagePath(migrated);
                    Save(migrated);
                }
                return migrated ?? new AppSettings();
            }
            var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                        ?? new AppSettings();
            value.SafeAreaHeight = Math.Clamp(value.SafeAreaHeight, 44, 80);
            value.JpegQuality = Math.Clamp(value.JpegQuality, 50, 100);
            value.CodexRefreshSeconds = value.CodexRefreshSeconds is 60 or 300 or 600 or 1800
                ? value.CodexRefreshSeconds : 300;
            value.DynamicUploadSeconds = Math.Clamp(value.DynamicUploadSeconds, 2, 60);
            if (!Enum.IsDefined(value.DisplayMode)) value.DisplayMode = DisplayMode.Codex;
            if (!Enum.IsDefined(value.CardTheme)) value.CardTheme = CardTheme.DeepSpace;
            if (NormalizeCustomImagePath(value)) Save(value);
            return value;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public PomodoroState LoadPomodoro() => LoadJson(PomodoroPath, new PomodoroState());

    public void Save(AppSettings settings) => SaveJson(SettingsPath, settings);
    public void SavePomodoro(PomodoroState state) => SaveJson(PomodoroPath, state);

    public void SaveCustomImage(string sourcePath)
    {
        Directory.CreateDirectory(DataDirectory);
        File.Copy(sourcePath, CustomImagePath, true);
    }

    private void MigrateLegacyDataDirectory()
    {
        if (!Directory.Exists(LegacyDataDirectory)) return;
        Directory.CreateDirectory(DataDirectory);
        foreach (var name in new[] { "settings-v2.json", "settings.json", "pomodoro.json", "custom-image.png" })
        {
            var source = Path.Combine(LegacyDataDirectory, name);
            var destination = Path.Combine(DataDirectory, name);
            if (File.Exists(source) && !File.Exists(destination)) File.Copy(source, destination);
        }
    }

    private bool NormalizeCustomImagePath(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.CustomImagePath) || !File.Exists(CustomImagePath)) return false;
        var legacyCustomImage = Path.Combine(LegacyDataDirectory, "custom-image.png");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!Path.GetFullPath(settings.CustomImagePath).Equals(Path.GetFullPath(legacyCustomImage), comparison))
            return false;
        settings.CustomImagePath = CustomImagePath;
        return true;
    }

    private AppSettings? LoadLegacySettings()
    {
        if (!File.Exists(LegacySettingsPath)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(LegacySettingsPath));
            var root = document.RootElement;
            var mode = root.TryGetProperty("DisplayMode", out var displayMode)
                ? displayMode.GetInt32() switch
                {
                    1 => DisplayMode.CustomImage,
                    2 => DisplayMode.Pomodoro,
                    3 => DisplayMode.SystemMonitor,
                    _ => DisplayMode.Codex
                }
                : DisplayMode.Codex;
            return new AppSettings
            {
                Endpoint = GetString(root, "Endpoint") ?? new AppSettings().Endpoint,
                CodexRefreshSeconds = GetInt32(root, "RefreshIntervalSeconds", 300),
                DynamicUploadSeconds = GetInt32(root, "SystemMonitorUploadIntervalSeconds", 5),
                SafeAreaHeight = Math.Clamp(GetInt32(root, "SafeAreaHeight", 56), 44, 80),
                JpegQuality = Math.Clamp(GetInt32(root, "JpegQuality", 90), 50, 100),
                DisplayMode = mode,
                CardTheme = (CardTheme)Math.Clamp(GetInt32(root, "CardTheme", 0), 0, 3),
                CustomImagePath = GetString(root, "CustomImagePath"),
                CustomImageName = GetString(root, "CustomImageName")
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt32(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;

    private static T LoadJson<T>(string path, T fallback)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? fallback
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static void SaveJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, true);
    }
}
