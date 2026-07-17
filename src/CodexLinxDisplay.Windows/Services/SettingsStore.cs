using System.Text.Json;
using CodexLinxDisplay.Windows.Models;

namespace CodexLinxDisplay.Windows.Services;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CodexLinxDisplay");

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
}
