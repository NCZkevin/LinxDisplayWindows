using System.Text.Json;
using LinxDisplay.Windows.Models;

namespace LinxDisplay.Windows.Services;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsStore()
    {
        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        DataDirectory = Path.Combine(applicationData, "LinxDisplay");
        LegacyDataDirectory = Path.Combine(applicationData, "CodexLinxDisplay");
        MigrateLegacyDataDirectory();
    }

    public string DataDirectory { get; }
    private string LegacyDataDirectory { get; }

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public string PomodoroPath => Path.Combine(DataDirectory, "pomodoro.json");
    public string CustomImagePath => Path.Combine(DataDirectory, "custom-image.png");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                ?? new AppSettings();
            settings.SafeAreaHeight = Math.Clamp(settings.SafeAreaHeight, 44, 80);
            settings.JpegQuality = Math.Clamp(settings.JpegQuality, 50, 100);
            settings.RefreshIntervalSeconds = settings.RefreshIntervalSeconds is 60 or 300 or 600 or 1800
                ? settings.RefreshIntervalSeconds
                : 300;
            settings.SystemMonitorUploadIntervalSeconds = settings.SystemMonitorUploadIntervalSeconds is 2 or 5 or 10 or 30
                ? settings.SystemMonitorUploadIntervalSeconds
                : 5;
            if (!Enum.IsDefined(settings.CardTheme)) settings.CardTheme = CardTheme.DeepSpace;
            var legacyCustomImage = Path.Combine(LegacyDataDirectory, "custom-image.png");
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.IsNullOrWhiteSpace(settings.CustomImagePath)
                && File.Exists(CustomImagePath)
                && Path.GetFullPath(settings.CustomImagePath).Equals(Path.GetFullPath(legacyCustomImage), comparison))
                settings.CustomImagePath = CustomImagePath;
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, SettingsPath, true);
    }

    public PomodoroState LoadPomodoro()
    {
        try
        {
            if (!File.Exists(PomodoroPath)) return new PomodoroState();
            return JsonSerializer.Deserialize<PomodoroState>(File.ReadAllText(PomodoroPath), JsonOptions)
                ?? new PomodoroState();
        }
        catch
        {
            return new PomodoroState();
        }
    }

    public void SavePomodoro(PomodoroState state)
    {
        Directory.CreateDirectory(DataDirectory);
        var temporaryPath = PomodoroPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temporaryPath, PomodoroPath, true);
    }

    private void MigrateLegacyDataDirectory()
    {
        if (!Directory.Exists(LegacyDataDirectory)) return;
        Directory.CreateDirectory(DataDirectory);
        foreach (var name in new[] { "settings.json", "pomodoro.json", "custom-image.png" })
        {
            var source = Path.Combine(LegacyDataDirectory, name);
            var destination = Path.Combine(DataDirectory, name);
            if (File.Exists(source) && !File.Exists(destination)) File.Copy(source, destination);
        }
    }
}
