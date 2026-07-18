using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CodexLinxDisplay.Core;
using CodexLinxDisplay.Rendering;

namespace CodexLinxDisplay.Desktop;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SettingsStore _store = new();
    private readonly CodexRateLimitClient _codex = new();
    private readonly ImageApiClient _imageApi = new();
    private readonly ISystemMonitorService _systemMonitor = PlatformServiceFactory.CreateSystemMonitor();
    private readonly IStartupManager _startup = PlatformServiceFactory.CreateStartupManager();
    private readonly PomodoroService _pomodoro;
    private readonly DispatcherTimer _timer;
    private AppSettings _settings;
    private UsageSnapshot _usage = UsageSnapshot.Sample;
    private SystemSnapshot _system = SystemSnapshot.Empty;
    private Bitmap? _previewImage;
    private string _status = "正在初始化…";
    private string _lastRefresh = "最后刷新：尚未刷新";
    private string _lastPush = "最后推送：尚未推送";
    private bool _busy;
    private DateTimeOffset _lastCodexRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPushAttempt = DateTimeOffset.MinValue;
    private string? _lastUploadedHash;

    public MainViewModel()
    {
        _settings = _store.Load();
        _pomodoro = new PomodoroService(_store.LoadPomodoro());
        try { _settings.StartWithSystem = _startup.IsEnabled(); } catch { }

        Modes =
        [
            new("Codex 用量", DisplayMode.Codex),
            new("番茄钟", DisplayMode.Pomodoro),
            new("系统监控", DisplayMode.SystemMonitor),
            new("自定义图片", DisplayMode.CustomImage)
        ];
        Themes =
        [
            new("深空薄荷", CardTheme.DeepSpace),
            new("明亮极简", CardTheme.MinimalLight),
            new("霓虹紫", CardTheme.NeonPurple),
            new("琥珀终端", CardTheme.AmberTerminal)
        ];
        RefreshIntervals =
        [
            new("1 分钟", 60),
            new("5 分钟", 300),
            new("10 分钟", 600),
            new("30 分钟", 1800)
        ];

        RefreshCommand = new AsyncCommand(() => RefreshCodexAsync(upload: false, forceUpload: false));
        PushCommand = new AsyncCommand(PushCurrentAsync);
        PomodoroToggleCommand = new AsyncCommand(TogglePomodoroAsync);
        PomodoroSkipCommand = new AsyncCommand(SkipPomodoroAsync);
        PomodoroResetCommand = new AsyncCommand(ResetPomodoroAsync);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
        RenderPreview();
        _ = ActivateModeAsync();
    }

    public IReadOnlyList<Choice<DisplayMode>> Modes { get; }
    public IReadOnlyList<Choice<CardTheme>> Themes { get; }
    public IReadOnlyList<Choice<int>> RefreshIntervals { get; }
    public ICommand RefreshCommand { get; }
    public ICommand PushCommand { get; }
    public ICommand PomodoroToggleCommand { get; }
    public ICommand PomodoroSkipCommand { get; }
    public ICommand PomodoroResetCommand { get; }

    public Choice<DisplayMode> SelectedMode
    {
        get => Modes.First(x => x.Value == _settings.DisplayMode);
        set
        {
            if (_settings.DisplayMode == value.Value) return;
            _settings.DisplayMode = value.Value;
            SaveSettings();
            RaiseModeProperties();
            _ = ActivateModeAsync();
        }
    }

    public Choice<CardTheme> SelectedTheme
    {
        get => Themes.First(x => x.Value == _settings.CardTheme);
        set
        {
            if (_settings.CardTheme == value.Value) return;
            _settings.CardTheme = value.Value;
            SaveSettings();
            OnPropertyChanged();
            RenderPreview();
        }
    }

    public string Endpoint
    {
        get => _settings.Endpoint;
        set { if (_settings.Endpoint == value) return; _settings.Endpoint = value; SaveSettings(); OnPropertyChanged(); }
    }

    public Choice<int> SelectedRefreshInterval
    {
        get => RefreshIntervals.First(x => x.Value == _settings.CodexRefreshSeconds);
        set
        {
            if (_settings.CodexRefreshSeconds == value.Value) return;
            _settings.CodexRefreshSeconds = value.Value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    public string CodexCliPath
    {
        get => _settings.CodexCliPath ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_settings.CodexCliPath == normalized) return;
            _settings.CodexCliPath = normalized;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    public int SafeAreaHeight
    {
        get => _settings.SafeAreaHeight;
        set { value = Math.Clamp(value, 44, 80); if (_settings.SafeAreaHeight == value) return; _settings.SafeAreaHeight = value; SaveSettings(); OnPropertyChanged(); RenderPreview(); }
    }

    public int JpegQuality
    {
        get => _settings.JpegQuality;
        set
        {
            value = Math.Clamp(value, 50, 100);
            if (_settings.JpegQuality == value) return;
            _settings.JpegQuality = value;
            SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(JpegQualityText));
            RenderPreview();
        }
    }

    public int DynamicUploadSeconds
    {
        get => _settings.DynamicUploadSeconds;
        set { value = Math.Clamp(value, 2, 60); if (_settings.DynamicUploadSeconds == value) return; _settings.DynamicUploadSeconds = value; SaveSettings(); OnPropertyChanged(); }
    }

    public bool StartWithSystem
    {
        get => _settings.StartWithSystem;
        set
        {
            if (_settings.StartWithSystem == value) return;
            try
            {
                _startup.SetEnabled(value);
                _settings.StartWithSystem = value;
                SaveSettings();
                Status = value ? "已启用登录时自动启动" : "已关闭登录时自动启动";
                OnPropertyChanged();
            }
            catch (Exception error) { Status = error.Message; }
        }
    }

    public string TaskName
    {
        get => _pomodoro.State.TaskName;
        set { if (_pomodoro.State.TaskName == value) return; _pomodoro.State.TaskName = value; SavePomodoro(); OnPropertyChanged(); RenderPreview(); }
    }

    public int FocusMinutes
    {
        get => _pomodoro.State.FocusMinutes;
        set { value = Math.Clamp(value, 1, 120); if (_pomodoro.State.FocusMinutes == value) return; _pomodoro.State.FocusMinutes = value; SavePomodoro(); OnPropertyChanged(); RenderPreview(); }
    }

    public int ShortBreakMinutes
    {
        get => _pomodoro.State.ShortBreakMinutes;
        set { value = Math.Clamp(value, 1, 60); if (_pomodoro.State.ShortBreakMinutes == value) return; _pomodoro.State.ShortBreakMinutes = value; SavePomodoro(); OnPropertyChanged(); }
    }

    public int LongBreakMinutes
    {
        get => _pomodoro.State.LongBreakMinutes;
        set { value = Math.Clamp(value, 1, 120); if (_pomodoro.State.LongBreakMinutes == value) return; _pomodoro.State.LongBreakMinutes = value; SavePomodoro(); OnPropertyChanged(); }
    }

    public Bitmap? PreviewImage { get => _previewImage; private set => Set(ref _previewImage, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string LastRefresh { get => _lastRefresh; private set => Set(ref _lastRefresh, value); }
    public string LastPush { get => _lastPush; private set => Set(ref _lastPush, value); }
    public string CustomImageName => string.IsNullOrWhiteSpace(_settings.CustomImageName) ? "尚未选择图片" : _settings.CustomImageName;
    public string JpegQualityText => $"{JpegQuality}%";
    public bool IsCodexMode => _settings.DisplayMode == DisplayMode.Codex;
    public bool IsPomodoroMode => _settings.DisplayMode == DisplayMode.Pomodoro;
    public bool IsSystemMode => _settings.DisplayMode == DisplayMode.SystemMonitor;
    public bool IsCustomMode => _settings.DisplayMode == DisplayMode.CustomImage;
    public bool IsDynamicMode => IsPomodoroMode || IsSystemMode;
    public string PomodoroAction
    {
        get
        {
            var snapshot = _pomodoro.GetSnapshot(DateTimeOffset.Now);
            return snapshot.IsRunning ? "暂停" : snapshot.IsPaused ? "继续" : "开始";
        }
    }
    public string PomodoroStatus
    {
        get
        {
            var snapshot = _pomodoro.GetSnapshot(DateTimeOffset.Now);
            var phase = snapshot.Phase switch
            {
                PomodoroPhase.Focus => "专注中",
                PomodoroPhase.ShortBreak => "短休息",
                PomodoroPhase.LongBreak => "长休息",
                PomodoroPhase.Paused => "已暂停",
                _ => "尚未开始"
            };
            return $"{phase} · {snapshot.Remaining:mm\\:ss} · 已完成 {snapshot.CompletedFocusSessions} 轮";
        }
    }
    public string SystemStatus =>
        $"CPU {_system.CpuPercent:0}% · 内存 {_system.MemoryPercent:0}% · ↓ {ScreenRenderer.FormatRate(_system.DownloadBytesPerSecond)} · ↑ {ScreenRenderer.FormatRate(_system.UploadBytesPerSecond)}";

    public async Task SetCustomImageAsync(string path)
    {
        _store.SaveCustomImage(path);
        _settings.CustomImagePath = _store.CustomImagePath;
        _settings.CustomImageName = Path.GetFileName(path);
        _settings.DisplayMode = DisplayMode.CustomImage;
        SaveSettings();
        OnPropertyChanged(nameof(CustomImageName));
        OnPropertyChanged(nameof(SelectedMode));
        RaiseModeProperties();
        RenderPreview();
        await PushAsync(true);
    }

    private async Task ActivateModeAsync()
    {
        RenderPreview();
        if (_settings.DisplayMode == DisplayMode.Codex)
            await RefreshCodexAsync(upload: true, forceUpload: false);
        else if (_settings.DisplayMode == DisplayMode.SystemMonitor)
        {
            _systemMonitor.Sample();
            await Task.Delay(150);
            _system = _systemMonitor.Sample();
            OnPropertyChanged(nameof(SystemStatus));
            RenderPreview();
            await PushAsync(true);
        }
        else if (_settings.DisplayMode == DisplayMode.Pomodoro)
            await PushAsync(true);
        else if (_settings.DisplayMode == DisplayMode.CustomImage && File.Exists(_settings.CustomImagePath))
            await PushAsync(true);
    }

    private async Task RefreshCodexAsync(bool upload, bool forceUpload)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            Status = upload ? "正在刷新并推送…" : "正在刷新…";
            if (_settings.DisplayMode == DisplayMode.Codex)
            {
                _lastCodexRefresh = DateTimeOffset.Now;
                _usage = await _codex.FetchAsync(_settings.CodexCliPath);
                _lastCodexRefresh = DateTimeOffset.Now;
            }
            else if (_settings.DisplayMode == DisplayMode.SystemMonitor)
            {
                _system = _systemMonitor.Sample();
                OnPropertyChanged(nameof(SystemStatus));
            }
            RenderPreview();
            LastRefresh = $"最后刷新：{DateTime.Now:M月d日 HH:mm:ss}";
            if (upload)
                await UploadRenderedAsync(forceUpload);
            else
                Status = "数据已刷新";
        }
        catch (Exception error)
        {
            RenderPreview();
            Status = error.Message;
        }
        finally { _busy = false; }
    }

    private async Task PushCurrentAsync()
    {
        if (_settings.DisplayMode == DisplayMode.Codex)
            await RefreshCodexAsync(upload: true, forceUpload: true);
        else
            await PushAsync(force: true);
    }

    private async Task PushAsync(bool force)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await UploadRenderedAsync(force);
        }
        catch (Exception error) { Status = error.Message; }
        finally { _busy = false; }
    }

    private async Task UploadRenderedAsync(bool force)
    {
        var jpeg = RenderBytes();
        var hash = Convert.ToHexString(SHA256.HashData(jpeg));
        _lastPushAttempt = DateTimeOffset.Now;
        if (!force && hash == _lastUploadedHash)
        {
            Status = "画面未变化，无需重复推送";
            return;
        }

        Status = "正在推送到键盘…";
        var statusCode = await _imageApi.UploadAsync(jpeg, _settings.Endpoint);
        _lastUploadedHash = hash;
        LastPush = $"最后推送：{DateTime.Now:M月d日 HH:mm:ss}";
        Status = $"推送成功（HTTP {statusCode}）";
    }

    private async Task TickAsync()
    {
        var now = DateTimeOffset.Now;
        if (_pomodoro.Tick(now)) SavePomodoro();

        if (_settings.DisplayMode == DisplayMode.Codex)
        {
            var action = AutomaticSyncPlanner.ForCodex(
                now, _lastCodexRefresh, _lastPushAttempt, _settings.CodexRefreshSeconds);
            if (action == AutomaticSyncAction.RefreshAndPush)
                await RefreshCodexAsync(upload: true, forceUpload: false);
            else if (action == AutomaticSyncAction.Push)
                await PushAsync(force: false);
            else
                RenderPreview();
            return;
        }

        if (_settings.DisplayMode == DisplayMode.SystemMonitor)
        {
            _system = _systemMonitor.Sample(now);
            OnPropertyChanged(nameof(SystemStatus));
        }
        if (_settings.DisplayMode is DisplayMode.Pomodoro or DisplayMode.SystemMonitor)
        {
            if (_settings.DisplayMode == DisplayMode.Pomodoro)
            {
                OnPropertyChanged(nameof(PomodoroAction));
                OnPropertyChanged(nameof(PomodoroStatus));
            }
            RenderPreview();
            if (now - _lastPushAttempt >= TimeSpan.FromSeconds(_settings.DynamicUploadSeconds))
                await PushAsync(false);
        }
    }

    private async Task TogglePomodoroAsync()
    {
        var now = DateTimeOffset.Now;
        var snapshot = _pomodoro.GetSnapshot(now);
        if (snapshot.IsRunning) _pomodoro.Pause(now); else _pomodoro.StartOrResume(now);
        SavePomodoro();
        OnPropertyChanged(nameof(PomodoroAction));
        OnPropertyChanged(nameof(PomodoroStatus));
        RenderPreview();
        await PushAsync(true);
    }

    private async Task SkipPomodoroAsync()
    {
        _pomodoro.Skip(DateTimeOffset.Now);
        SavePomodoro();
        OnPropertyChanged(nameof(PomodoroAction));
        OnPropertyChanged(nameof(PomodoroStatus));
        RenderPreview();
        await PushAsync(true);
    }

    private async Task ResetPomodoroAsync()
    {
        _pomodoro.Reset();
        SavePomodoro();
        OnPropertyChanged(nameof(PomodoroAction));
        OnPropertyChanged(nameof(PomodoroStatus));
        RenderPreview();
        await PushAsync(true);
    }

    private void RenderPreview()
    {
        try
        {
            var bytes = RenderBytes();
            using var stream = new MemoryStream(bytes);
            var next = new Bitmap(stream);
            var old = PreviewImage;
            PreviewImage = next;
            old?.Dispose();
        }
        catch (Exception error) { Status = error.Message; }
    }

    private byte[] RenderBytes() => _settings.DisplayMode switch
    {
        DisplayMode.Pomodoro => ScreenRenderer.RenderPomodoro(_pomodoro.GetSnapshot(DateTimeOffset.Now), _settings),
        DisplayMode.SystemMonitor => ScreenRenderer.RenderSystem(_system, _settings),
        DisplayMode.CustomImage when File.Exists(_settings.CustomImagePath) => ScreenRenderer.RenderCustomImage(_settings.CustomImagePath!, _settings),
        DisplayMode.CustomImage => throw new InvalidOperationException("请先选择一张自定义图片。"),
        _ => ScreenRenderer.RenderUsage(_usage, _settings)
    };

    private void SaveSettings() => _store.Save(_settings);
    private void SavePomodoro() => _store.SavePomodoro(_pomodoro.State);
    private void RaiseModeProperties()
    {
        OnPropertyChanged(nameof(SelectedMode));
        OnPropertyChanged(nameof(IsCodexMode));
        OnPropertyChanged(nameof(IsPomodoroMode));
        OnPropertyChanged(nameof(IsSystemMode));
        OnPropertyChanged(nameof(IsCustomMode));
        OnPropertyChanged(nameof(IsDynamicMode));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose()
    {
        _timer.Stop();
        _imageApi.Dispose();
        _previewImage?.Dispose();
    }
}
