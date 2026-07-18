using System.Security.Cryptography;
using CodexLinxDisplay.Windows.Models;
using CodexLinxDisplay.Windows.Services;

namespace CodexLinxDisplay.Windows;

internal sealed class MainForm : Form
{
    private static readonly DisplayMode[] ModeOrder =
        [DisplayMode.Codex, DisplayMode.Pomodoro, DisplayMode.SystemMonitor, DisplayMode.CustomImage];

    private readonly SettingsStore _settingsStore = new();
    private readonly CodexRateLimitClient _codexClient = new();
    private readonly ImageApiClient _imageClient = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = 1_000 };
    private readonly NotifyIcon _notifyIcon = new();
    private readonly Icon _applicationIcon;
    private readonly PomodoroService _pomodoroService;
    private readonly SystemMonitorService _systemMonitor = new();

    private readonly ComboBox _modeCombo = new();
    private readonly TextBox _endpointText = new();
    private readonly ComboBox _intervalCombo = new();
    private readonly ComboBox _themeCombo = new();
    private readonly NumericUpDown _safeAreaInput = new();
    private readonly TrackBar _qualitySlider = new();
    private readonly Label _qualityValue = new();
    private readonly CheckBox _startupCheck = new();
    private readonly Button _chooseImageButton = new();
    private readonly Label _customImageName = new();
    private readonly Button _refreshButton = new();
    private readonly Button _pushButton = new();
    private readonly Label _statusValue = new();
    private readonly Label _lastRefreshValue = new();
    private readonly Label _lastUploadValue = new();
    private readonly Label _errorValue = new();
    private readonly PictureBox _preview = new();
    private readonly Label _previewCaption = new();
    private readonly Panel _pomodoroPanel = new();
    private readonly Panel _systemPanel = new();
    private GroupBox? _modeOptionsGroup;
    private readonly TextBox _pomodoroTaskText = new();
    private readonly NumericUpDown _focusMinutesInput = new();
    private readonly NumericUpDown _shortBreakInput = new();
    private readonly NumericUpDown _longBreakInput = new();
    private readonly Button _pomodoroStartButton = new();
    private readonly Button _pomodoroSkipButton = new();
    private readonly Button _pomodoroResetButton = new();
    private readonly Label _pomodoroStatus = new();
    private readonly ComboBox _systemIntervalCombo = new();
    private readonly Label _systemStatus = new();

    private AppSettings _settings;
    private UsageSnapshot? _snapshot;
    private SystemSnapshot _systemSnapshot = SystemSnapshot.Empty;
    private Bitmap? _customSource;
    private string? _lastUploadedHash;
    private string? _lastRenderedMinute;
    private DateTimeOffset _lastDynamicUploadAt = DateTimeOffset.MinValue;
    private bool _allowExit;
    private bool _suppressEvents;

    public MainForm(bool testMode = false, DisplayMode? initialMode = null)
    {
        _settings = _settingsStore.Load();
        if (initialMode is not null) _settings.DisplayMode = initialMode.Value;
        _pomodoroService = new PomodoroService(_settingsStore.LoadPomodoro());
        _systemSnapshot = _systemMonitor.Sample();
        _applicationIcon = LoadApplicationIcon();
        Text = "Codex 屏显 for Linx68";
        Icon = _applicationIcon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(960, 680);
        Size = new Size(1_100, 820);
        Font = new Font("Microsoft YaHei UI", 9F);

        _suppressEvents = true;
        BuildInterface();
        LoadSettingsIntoControls();
        LoadCustomImage();
        UpdateModeControls();
        UpdatePreview();

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("打开设置", null, (_, _) => ShowFromTray());
        trayMenu.Items.Add("立即推送", null, async (_, _) => await PushCurrentAsync(true));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => ExitApplication());
        _notifyIcon.Icon = _applicationIcon;
        _notifyIcon.Text = "Codex 屏显";
        _notifyIcon.Visible = !testMode;
        _notifyIcon.ContextMenuStrip = trayMenu;
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();

        _refreshTimer.Tick += async (_, _) => await SynchronizeCodexAsync(upload: true, forceUpload: false);
        _clockTimer.Tick += async (_, _) => await RefreshDynamicContentAsync();
        FormClosing += HandleFormClosing;
        if (!testMode)
            Shown += HandleShown;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _clockTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _applicationIcon.Dispose();
            _imageClient.Dispose();
            _syncLock.Dispose();
            _customSource?.Dispose();
            _preview.Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 2,
            RowCount = 1,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        Controls.Add(root);

        var settingsHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(0, 0, 14, 0),
            Margin = Padding.Empty
        };
        root.Controls.Add(settingsHost, 0, 0);

        var settingsStack = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        settingsStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settingsHost.Controls.Add(settingsStack);
        settingsStack.Controls.Add(BuildDisplaySection(), 0, 0);
        settingsStack.Controls.Add(BuildModeOptionsSection(), 0, 1);
        settingsStack.Controls.Add(BuildEndpointSection(), 0, 2);
        settingsStack.Controls.Add(BuildRefreshSection(), 0, 3);
        settingsStack.Controls.Add(BuildLayoutSection(), 0, 4);
        settingsStack.Controls.Add(BuildStatusSection(), 0, 5);

        var previewPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(10, 0, 0, 0)
        };
        previewPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(previewPanel, 1, 0);
        var previewTitle = new Label
        {
            Text = "键盘预览",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 10)
        };
        previewPanel.Controls.Add(previewTitle, 0, 0);
        _preview.Size = new Size(ScreenImageRenderer.Width, ScreenImageRenderer.Height);
        _preview.SizeMode = PictureBoxSizeMode.CenterImage;
        _preview.BackColor = Color.FromArgb(8, 11, 18);
        _preview.BorderStyle = BorderStyle.FixedSingle;
        _preview.Anchor = AnchorStyles.None;
        _preview.Margin = new Padding(0);
        previewPanel.Controls.Add(_preview, 0, 1);
        _previewCaption.AutoSize = true;
        _previewCaption.Dock = DockStyle.Fill;
        _previewCaption.TextAlign = ContentAlignment.TopCenter;
        _previewCaption.ForeColor = SystemColors.GrayText;
        _previewCaption.Margin = new Padding(0, 10, 0, 0);
        previewPanel.Controls.Add(_previewCaption, 0, 2);
    }

    private GroupBox BuildDisplaySection()
    {
        var grid = NewGrid(2);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.Controls.Add(NewLabel("显示模式"), 0, 0);
        _modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeCombo.Items.AddRange(["Codex 用量", "番茄钟", "系统监控", "自定义图片"]);
        _modeCombo.Dock = DockStyle.Fill;
        _modeCombo.SelectedIndexChanged += async (_, _) => await ChangeModeAsync();
        grid.Controls.Add(_modeCombo, 1, 0);

        _chooseImageButton.Text = "选择图片…";
        _chooseImageButton.AutoSize = true;
        _chooseImageButton.Anchor = AnchorStyles.Left;
        _chooseImageButton.Click += async (_, _) => await ChooseCustomImageAsync();
        grid.Controls.Add(_chooseImageButton, 0, 1);
        _customImageName.ForeColor = SystemColors.GrayText;
        _customImageName.AutoEllipsis = true;
        _customImageName.Dock = DockStyle.Fill;
        _customImageName.TextAlign = ContentAlignment.MiddleLeft;
        grid.Controls.Add(_customImageName, 1, 1);
        return NewGroup("显示内容", grid);
    }

    private GroupBox BuildModeOptionsSection()
    {
        var pomodoroGrid = NewGrid(2);
        pomodoroGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        pomodoroGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pomodoroGrid.Controls.Add(NewLabel("当前任务"), 0, 0);
        _pomodoroTaskText.Dock = DockStyle.Fill;
        _pomodoroTaskText.MaxLength = 24;
        _pomodoroTaskText.Leave += (_, _) => SavePomodoroFromControls();
        pomodoroGrid.Controls.Add(_pomodoroTaskText, 1, 0);

        pomodoroGrid.Controls.Add(NewLabel("时长（分钟）"), 0, 1);
        var durationFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = Padding.Empty
        };
        ConfigureMinuteInput(_focusMinutesInput, 1, 120);
        ConfigureMinuteInput(_shortBreakInput, 1, 60);
        ConfigureMinuteInput(_longBreakInput, 1, 120);
        durationFlow.Controls.Add(NewLabel("专注"));
        durationFlow.Controls.Add(_focusMinutesInput);
        durationFlow.Controls.Add(NewLabel("短休"));
        durationFlow.Controls.Add(_shortBreakInput);
        durationFlow.Controls.Add(NewLabel("长休"));
        durationFlow.Controls.Add(_longBreakInput);
        pomodoroGrid.Controls.Add(durationFlow, 1, 1);

        var actionFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = Padding.Empty
        };
        _pomodoroStartButton.AutoSize = true;
        _pomodoroStartButton.Click += async (_, _) => await TogglePomodoroAsync();
        _pomodoroSkipButton.Text = "跳过";
        _pomodoroSkipButton.AutoSize = true;
        _pomodoroSkipButton.Click += async (_, _) => await SkipPomodoroAsync();
        _pomodoroResetButton.Text = "重置";
        _pomodoroResetButton.AutoSize = true;
        _pomodoroResetButton.Click += async (_, _) => await ResetPomodoroAsync();
        actionFlow.Controls.Add(_pomodoroStartButton);
        actionFlow.Controls.Add(_pomodoroSkipButton);
        actionFlow.Controls.Add(_pomodoroResetButton);
        pomodoroGrid.Controls.Add(actionFlow, 0, 2);
        pomodoroGrid.SetColumnSpan(actionFlow, 2);
        _pomodoroStatus.AutoSize = true;
        _pomodoroStatus.Dock = DockStyle.Fill;
        _pomodoroStatus.ForeColor = SystemColors.GrayText;
        pomodoroGrid.Controls.Add(_pomodoroStatus, 0, 3);
        pomodoroGrid.SetColumnSpan(_pomodoroStatus, 2);
        _pomodoroPanel.AutoSize = true;
        _pomodoroPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _pomodoroPanel.Dock = DockStyle.Top;
        _pomodoroPanel.Controls.Add(pomodoroGrid);

        var systemGrid = NewGrid(2);
        systemGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        systemGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        systemGrid.Controls.Add(NewLabel("推送间隔"), 0, 0);
        _systemIntervalCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _systemIntervalCombo.Items.AddRange(["2 秒", "5 秒", "10 秒", "30 秒"]);
        _systemIntervalCombo.Dock = DockStyle.Fill;
        _systemIntervalCombo.SelectedIndexChanged += (_, _) => SaveSettingsFromControls();
        systemGrid.Controls.Add(_systemIntervalCombo, 1, 0);
        _systemStatus.AutoSize = true;
        _systemStatus.Dock = DockStyle.Fill;
        _systemStatus.ForeColor = SystemColors.GrayText;
        systemGrid.Controls.Add(_systemStatus, 0, 1);
        systemGrid.SetColumnSpan(_systemStatus, 2);
        _systemPanel.AutoSize = true;
        _systemPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _systemPanel.Dock = DockStyle.Top;
        _systemPanel.Controls.Add(systemGrid);

        var host = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty
        };
        host.Controls.Add(_pomodoroPanel, 0, 0);
        host.Controls.Add(_systemPanel, 0, 1);
        _modeOptionsGroup = NewGroup("模式设置", host);
        return _modeOptionsGroup;
    }

    private void ConfigureMinuteInput(NumericUpDown input, int minimum, int maximum)
    {
        input.Minimum = minimum;
        input.Maximum = maximum;
        input.Width = 64;
        input.ValueChanged += (_, _) => SavePomodoroFromControls();
    }

    private GroupBox BuildEndpointSection()
    {
        var grid = NewGrid(3);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.Controls.Add(NewLabel("图像 API"), 0, 0);
        _endpointText.Dock = DockStyle.Fill;
        _endpointText.PlaceholderText = "http://键盘地址/image/upload";
        _endpointText.Leave += (_, _) => SaveSettingsFromControls();
        grid.Controls.Add(_endpointText, 1, 0);
        grid.SetColumnSpan(_endpointText, 2);

        var requestLabel = NewLabel("POST · image/jpeg");
        requestLabel.ForeColor = SystemColors.GrayText;
        grid.Controls.Add(requestLabel, 0, 1);
        grid.SetColumnSpan(requestLabel, 2);
        _pushButton.Text = "立即推送当前内容";
        _pushButton.AutoSize = true;
        _pushButton.Anchor = AnchorStyles.Right;
        _pushButton.Click += async (_, _) => await PushCurrentAsync(true);
        grid.Controls.Add(_pushButton, 2, 1);
        return NewGroup("设备接口", grid);
    }

    private GroupBox BuildRefreshSection()
    {
        var grid = NewGrid(3);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.Controls.Add(NewLabel("刷新间隔"), 0, 0);
        _intervalCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _intervalCombo.Items.AddRange(["1 分钟", "5 分钟", "10 分钟", "30 分钟"]);
        _intervalCombo.Dock = DockStyle.Fill;
        _intervalCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            SaveSettingsFromControls();
            RestartTimer();
        };
        grid.Controls.Add(_intervalCombo, 1, 0);
        _refreshButton.Text = "只刷新 Codex";
        _refreshButton.AutoSize = true;
        _refreshButton.Anchor = AnchorStyles.Right;
        _refreshButton.Click += async (_, _) => await SynchronizeCodexAsync(false, false);
        grid.Controls.Add(_refreshButton, 2, 0);

        _startupCheck.Text = "登录 Windows 时自动启动（后台运行）";
        _startupCheck.AutoSize = true;
        _startupCheck.Anchor = AnchorStyles.Left;
        _startupCheck.CheckedChanged += (_, _) => ChangeStartupSetting();
        grid.Controls.Add(_startupCheck, 0, 1);
        grid.SetColumnSpan(_startupCheck, 3);
        return NewGroup("后台刷新", grid);
    }

    private GroupBox BuildLayoutSection()
    {
        var grid = NewGrid(3);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.Controls.Add(NewLabel("卡片主题"), 0, 0);
        _themeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _themeCombo.Items.AddRange(Enum.GetValues<CardTheme>()
            .Select(ScreenThemes.DisplayName)
            .Cast<object>()
            .ToArray());
        _themeCombo.Dock = DockStyle.Fill;
        _themeCombo.SelectedIndexChanged += (_, _) => HandleRenderSettingChanged();
        grid.Controls.Add(_themeCombo, 1, 0);
        grid.SetColumnSpan(_themeCombo, 2);

        grid.Controls.Add(NewLabel("顶部安全区"), 0, 1);
        _safeAreaInput.Minimum = 44;
        _safeAreaInput.Maximum = 80;
        _safeAreaInput.Width = 80;
        _safeAreaInput.Anchor = AnchorStyles.Left;
        _safeAreaInput.ValueChanged += (_, _) => HandleRenderSettingChanged();
        grid.Controls.Add(_safeAreaInput, 1, 1);
        grid.Controls.Add(NewLabel("px"), 2, 1);

        grid.Controls.Add(NewLabel("JPEG 质量"), 0, 2);
        _qualitySlider.Minimum = 50;
        _qualitySlider.Maximum = 100;
        _qualitySlider.TickFrequency = 10;
        _qualitySlider.Dock = DockStyle.Fill;
        _qualitySlider.AutoSize = true;
        _qualitySlider.ValueChanged += (_, _) => HandleRenderSettingChanged();
        grid.Controls.Add(_qualitySlider, 1, 2);
        _qualityValue.AutoSize = true;
        _qualityValue.TextAlign = ContentAlignment.MiddleRight;
        _qualityValue.Anchor = AnchorStyles.Right;
        grid.Controls.Add(_qualityValue, 2, 2);
        return NewGroup("屏幕布局", grid);
    }

    private GroupBox BuildStatusSection()
    {
        var grid = NewGrid(2);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.Controls.Add(NewLabel("状态"), 0, 0);
        _statusValue.Dock = DockStyle.Fill;
        _statusValue.TextAlign = ContentAlignment.MiddleLeft;
        grid.Controls.Add(_statusValue, 1, 0);
        grid.Controls.Add(NewLabel("上次 Codex 刷新"), 0, 1);
        _lastRefreshValue.Dock = DockStyle.Fill;
        _lastRefreshValue.TextAlign = ContentAlignment.MiddleLeft;
        grid.Controls.Add(_lastRefreshValue, 1, 1);
        grid.Controls.Add(NewLabel("上次推送"), 0, 2);
        _lastUploadValue.Dock = DockStyle.Fill;
        _lastUploadValue.TextAlign = ContentAlignment.MiddleLeft;
        grid.Controls.Add(_lastUploadValue, 1, 2);
        _errorValue.ForeColor = Color.Firebrick;
        _errorValue.AutoEllipsis = true;
        _errorValue.AutoSize = true;
        _errorValue.Dock = DockStyle.Fill;
        grid.Controls.Add(_errorValue, 0, 3);
        grid.SetColumnSpan(_errorValue, 2);
        return NewGroup("运行状态", grid);
    }

    private static GroupBox NewGroup(string title, Control content)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 0, 0, 12)
        };
        content.Dock = DockStyle.Top;
        group.Controls.Add(content);
        return group;
    }

    private static TableLayoutPanel NewGrid(int columns) => new()
    {
        ColumnCount = columns,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Top,
        Padding = Padding.Empty,
        Margin = Padding.Empty,
        GrowStyle = TableLayoutPanelGrowStyle.AddRows
    };

    private static Label NewLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(3, 6, 3, 6)
    };

    private void LoadSettingsIntoControls()
    {
        _suppressEvents = true;
        _modeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(ModeOrder, _settings.DisplayMode));
        _endpointText.Text = _settings.Endpoint;
        _intervalCombo.SelectedIndex = _settings.RefreshIntervalSeconds switch
        {
            60 => 0,
            600 => 2,
            1800 => 3,
            _ => 1
        };
        _safeAreaInput.Value = _settings.SafeAreaHeight;
        _themeCombo.SelectedIndex = (int)_settings.CardTheme;
        _qualitySlider.Value = _settings.JpegQuality;
        _qualityValue.Text = $"{_settings.JpegQuality}%";
        _systemIntervalCombo.SelectedIndex = _settings.SystemMonitorUploadIntervalSeconds switch
        {
            2 => 0,
            10 => 2,
            30 => 3,
            _ => 1
        };
        _pomodoroTaskText.Text = _pomodoroService.State.TaskName;
        _focusMinutesInput.Value = _pomodoroService.State.FocusMinutes;
        _shortBreakInput.Value = _pomodoroService.State.ShortBreakMinutes;
        _longBreakInput.Value = _pomodoroService.State.LongBreakMinutes;
        _startupCheck.Checked = StartupManager.IsEnabled();
        _customImageName.Text = _settings.CustomImageName ?? "尚未选择图片";
        _statusValue.Text = "等待首次同步";
        _lastRefreshValue.Text = "尚未刷新";
        _lastUploadValue.Text = "尚未推送";
        _suppressEvents = false;
        UpdatePomodoroControls(DateTimeOffset.Now);
        UpdateSystemStatus();
    }

    private void LoadCustomImage()
    {
        if (string.IsNullOrWhiteSpace(_settings.CustomImagePath)
            || !File.Exists(_settings.CustomImagePath)) return;
        try
        {
            _customSource = ScreenImageRenderer.LoadDetached(_settings.CustomImagePath);
        }
        catch (Exception error)
        {
            SetStatus("图片读取失败", error.Message);
        }
    }

    private async void HandleShown(object? sender, EventArgs e)
    {
        if (Environment.GetCommandLineArgs().Any(value => value.Equals("--background", StringComparison.OrdinalIgnoreCase)))
            Hide();
        RestartTimer();
        _clockTimer.Start();
        await PushCurrentAsync(forceUpload: true);
    }

    private async Task ChangeModeAsync()
    {
        if (_suppressEvents) return;
        _settings.DisplayMode = _modeCombo.SelectedIndex >= 0 && _modeCombo.SelectedIndex < ModeOrder.Length
            ? ModeOrder[_modeCombo.SelectedIndex]
            : DisplayMode.Codex;
        var expectedMode = _settings.DisplayMode;
        _lastUploadedHash = null;
        _lastDynamicUploadAt = DateTimeOffset.MinValue;
        SaveSettingsFromControls();
        UpdateModeControls();
        UpdatePreview();
        RestartTimer();

        // A previous mode may still be waiting for Codex or the keyboard. Wait for it to finish so
        // switching modes always performs the promised immediate push.
        await _syncLock.WaitAsync();
        _syncLock.Release();
        if (_settings.DisplayMode != expectedMode) return;

        await PushCurrentAsync(forceUpload: true);
    }

    private async Task ChooseCustomImageAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择要显示在键盘上的图片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件|*.*",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var selected = ScreenImageRenderer.LoadDetached(dialog.FileName);
            Directory.CreateDirectory(_settingsStore.DataDirectory);
            selected.Save(_settingsStore.CustomImagePath, System.Drawing.Imaging.ImageFormat.Png);
            _customSource?.Dispose();
            _customSource = new Bitmap(selected);
            _settings.CustomImagePath = _settingsStore.CustomImagePath;
            _settings.CustomImageName = Path.GetFileName(dialog.FileName);
            _customImageName.Text = _settings.CustomImageName;
            _lastUploadedHash = null;

            if (_settings.DisplayMode != DisplayMode.CustomImage)
            {
                _modeCombo.SelectedIndex = Array.IndexOf(ModeOrder, DisplayMode.CustomImage);
                return;
            }

            SaveSettingsFromControls();
            UpdatePreview();
            await PushCustomImageAsync(forceUpload: true);
        }
        catch (Exception error)
        {
            SetStatus("图片读取失败", $"无法读取所选图片：{error.Message}");
        }
    }

    private async Task PushCurrentAsync(bool forceUpload)
    {
        switch (_settings.DisplayMode)
        {
            case DisplayMode.Codex:
                await SynchronizeCodexAsync(upload: true, forceUpload);
                break;
            case DisplayMode.CustomImage:
                await PushCustomImageAsync(forceUpload);
                break;
            case DisplayMode.Pomodoro:
            case DisplayMode.SystemMonitor:
                await PushStatusModeAsync(forceUpload);
                break;
        }
    }

    private async Task SynchronizeCodexAsync(bool upload, bool forceUpload)
    {
        if (_settings.DisplayMode != DisplayMode.Codex) return;
        if (!await _syncLock.WaitAsync(0)) return;
        SetBusy(true);
        SetStatus(upload ? "正在同步并推送…" : "正在读取 Codex…");
        try
        {
            var latest = await _codexClient.FetchAsync();
            if (_settings.DisplayMode != DisplayMode.Codex) return;
            _snapshot = latest;
            _lastRefreshValue.Text = FormatNow();
            UpdatePreview();
            if (!upload)
            {
                SetStatus("用量已刷新");
                return;
            }
            await UploadPreviewAsync(forceUpload, "数据未变化");
        }
        catch (Exception error)
        {
            SetStatus("同步失败", error.Message);
        }
        finally
        {
            SetBusy(false);
            _syncLock.Release();
        }
    }

    private async Task PushCustomImageAsync(bool forceUpload)
    {
        if (_settings.DisplayMode != DisplayMode.CustomImage) return;
        if (_customSource is null)
        {
            SetStatus("尚未选择图片", "请先选择要显示的图片。");
            return;
        }
        if (!await _syncLock.WaitAsync(0)) return;
        SetBusy(true);
        SetStatus("正在推送图片…");
        try
        {
            UpdatePreview();
            await UploadPreviewAsync(forceUpload, "图片未变化");
        }
        catch (Exception error)
        {
            SetStatus("图片推送失败", error.Message);
        }
        finally
        {
            SetBusy(false);
            _syncLock.Release();
        }
    }

    private async Task UploadPreviewAsync(bool forceUpload, string unchangedStatus)
    {
        if (_preview.Image is null) throw new InvalidOperationException("尚无可推送的图片。");
        var jpeg = ScreenImageRenderer.EncodeJpeg(_preview.Image, _settings.JpegQuality);
        var hash = Convert.ToHexString(SHA256.HashData(jpeg));
        if (!forceUpload && hash == _lastUploadedHash)
        {
            SetStatus(unchangedStatus);
            return;
        }

        var statusCode = await _imageClient.UploadAsync(jpeg, _settings.Endpoint);
        _lastUploadedHash = hash;
        _lastUploadValue.Text = FormatNow();
        SetStatus($"推送成功 · HTTP {statusCode}");
    }

    private async Task RefreshDynamicContentAsync()
    {
        var now = DateTimeOffset.Now;
        if (_settings.DisplayMode == DisplayMode.Codex)
        {
            if (_snapshot is null) return;
            var currentMinute = now.ToString("yyyyMMddHHmm");
            if (currentMinute == _lastRenderedMinute) return;
            await PushStatusOrClockAsync("时间未变化", "时钟推送失败");
            return;
        }

        if (_settings.DisplayMode == DisplayMode.Pomodoro)
        {
            var transitioned = _pomodoroService.Tick(now);
            if (transitioned) SavePomodoroState();
            UpdatePomodoroControls(now);
            UpdatePreview();
            if (transitioned || (now - _lastDynamicUploadAt).TotalSeconds >= 5)
                await PushStatusModeAsync(forceUpload: transitioned);
            return;
        }

        if (_settings.DisplayMode == DisplayMode.SystemMonitor)
        {
            try
            {
                _systemSnapshot = _systemMonitor.Sample(now);
                UpdateSystemStatus();
                UpdatePreview();
                if ((now - _lastDynamicUploadAt).TotalSeconds >= _settings.SystemMonitorUploadIntervalSeconds)
                    await PushStatusModeAsync(forceUpload: false);
            }
            catch (Exception error)
            {
                SetStatus("系统监控读取失败", error.Message);
            }
        }
    }

    private async Task PushStatusOrClockAsync(string unchangedStatus, string failureStatus)
    {
        if (!await _syncLock.WaitAsync(0)) return;
        try
        {
            UpdatePreview();
            await UploadPreviewAsync(forceUpload: false, unchangedStatus);
        }
        catch (Exception error)
        {
            SetStatus(failureStatus, error.Message);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task PushStatusModeAsync(bool forceUpload)
    {
        if (_settings.DisplayMode is not (DisplayMode.Pomodoro or DisplayMode.SystemMonitor)) return;
        if (!await _syncLock.WaitAsync(0)) return;
        SetBusy(true);
        try
        {
            if (_settings.DisplayMode == DisplayMode.Pomodoro)
            {
                if (_pomodoroService.Tick(DateTimeOffset.Now)) SavePomodoroState();
                UpdatePomodoroControls(DateTimeOffset.Now);
            }
            else if (_settings.DisplayMode == DisplayMode.SystemMonitor
                     && (DateTimeOffset.Now - _systemSnapshot.SampledAt).TotalMilliseconds >= 500)
            {
                _systemSnapshot = _systemMonitor.Sample();
                UpdateSystemStatus();
            }
            UpdatePreview();
            await UploadPreviewAsync(forceUpload, "画面未变化");
            _lastDynamicUploadAt = DateTimeOffset.Now;
        }
        catch (Exception error)
        {
            SetStatus("动态画面推送失败", error.Message);
        }
        finally
        {
            SetBusy(false);
            _syncLock.Release();
        }
    }

    private async Task TogglePomodoroAsync()
    {
        SavePomodoroFromControls();
        var now = DateTimeOffset.Now;
        if (_pomodoroService.State.Phase is PomodoroPhase.Focus or PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak)
            _pomodoroService.Pause(now);
        else
            _pomodoroService.StartOrResume(now);
        SavePomodoroState();
        UpdatePomodoroControls(now);
        UpdatePreview();
        await PushStatusModeAsync(forceUpload: true);
    }

    private async Task SkipPomodoroAsync()
    {
        SavePomodoroFromControls();
        _pomodoroService.Skip(DateTimeOffset.Now);
        SavePomodoroState();
        UpdatePomodoroControls(DateTimeOffset.Now);
        UpdatePreview();
        await PushStatusModeAsync(forceUpload: true);
    }

    private async Task ResetPomodoroAsync()
    {
        _pomodoroService.Reset();
        SavePomodoroState();
        UpdatePomodoroControls(DateTimeOffset.Now);
        UpdatePreview();
        await PushStatusModeAsync(forceUpload: true);
    }

    private void UpdatePreview()
    {
        try
        {
            Bitmap next;
            if (_settings.DisplayMode == DisplayMode.CustomImage)
            {
                if (_customSource is null)
                {
                    _preview.Image?.Dispose();
                    _preview.Image = null;
                    _previewCaption.Text = "自定义图片 · 尚未选择";
                    return;
                }
                next = ScreenImageRenderer.RenderCustomImage(_customSource, _settings.SafeAreaHeight);
                _previewCaption.Text = $"自定义图片\r\n顶部 {_settings.SafeAreaHeight}px 留空";
            }
            else if (_settings.DisplayMode == DisplayMode.Pomodoro)
            {
                next = StatusCardRenderer.RenderPomodoro(
                    _pomodoroService.GetSnapshot(DateTimeOffset.Now),
                    _settings.SafeAreaHeight,
                    DateTimeOffset.Now,
                    _settings.CardTheme);
                _previewCaption.Text = $"番茄钟 · {ScreenThemes.DisplayName(_settings.CardTheme)}\r\n顶部 {_settings.SafeAreaHeight}px 留空";
            }
            else if (_settings.DisplayMode == DisplayMode.SystemMonitor)
            {
                next = StatusCardRenderer.RenderSystem(_systemSnapshot, _settings.SafeAreaHeight,
                    _settings.CardTheme);
                _previewCaption.Text = $"系统监控 · {ScreenThemes.DisplayName(_settings.CardTheme)}\r\n顶部 {_settings.SafeAreaHeight}px 留空";
            }
            else
            {
                var now = DateTimeOffset.Now;
                next = ScreenImageRenderer.RenderUsage(_snapshot ?? UsageSnapshot.Sample,
                    _settings.SafeAreaHeight, now, _settings.CardTheme);
                _lastRenderedMinute = now.ToString("yyyyMMddHHmm");
                _previewCaption.Text = $"Codex 用量 · {ScreenThemes.DisplayName(_settings.CardTheme)}\r\n顶部 {_settings.SafeAreaHeight}px 留空";
            }

            var previous = _preview.Image;
            _preview.Image = next;
            previous?.Dispose();
        }
        catch (Exception error)
        {
            SetStatus("预览生成失败", error.Message);
        }
    }

    private void HandleRenderSettingChanged()
    {
        if (_suppressEvents) return;
        _qualityValue.Text = $"{_qualitySlider.Value}%";
        SaveSettingsFromControls();
        _lastUploadedHash = null;
        UpdatePreview();
    }

    private void SaveSettingsFromControls()
    {
        if (_suppressEvents) return;
        _settings.Endpoint = _endpointText.Text.Trim();
        _settings.RefreshIntervalSeconds = _intervalCombo.SelectedIndex switch
        {
            0 => 60,
            2 => 600,
            3 => 1800,
            _ => 300
        };
        _settings.SafeAreaHeight = (int)_safeAreaInput.Value;
        _settings.JpegQuality = _qualitySlider.Value;
        _settings.CardTheme = _themeCombo.SelectedIndex is >= 0 and <= 3
            ? (CardTheme)_themeCombo.SelectedIndex
            : CardTheme.DeepSpace;
        _settings.SystemMonitorUploadIntervalSeconds = _systemIntervalCombo.SelectedIndex switch
        {
            0 => 2,
            2 => 10,
            3 => 30,
            _ => 5
        };
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception error)
        {
            SetStatus("设置保存失败", error.Message);
        }
    }

    private void ChangeStartupSetting()
    {
        if (_suppressEvents) return;
        try
        {
            StartupManager.SetEnabled(_startupCheck.Checked);
            SetStatus(_startupCheck.Checked ? "已启用登录时启动" : "已关闭登录时启动");
        }
        catch (Exception error)
        {
            _suppressEvents = true;
            _startupCheck.Checked = StartupManager.IsEnabled();
            _suppressEvents = false;
            SetStatus("开机启动设置失败", error.Message);
        }
    }

    private void SavePomodoroFromControls()
    {
        if (_suppressEvents) return;
        _pomodoroService.State.TaskName = string.IsNullOrWhiteSpace(_pomodoroTaskText.Text)
            ? "专注工作"
            : _pomodoroTaskText.Text.Trim();
        _pomodoroService.State.FocusMinutes = (int)_focusMinutesInput.Value;
        _pomodoroService.State.ShortBreakMinutes = (int)_shortBreakInput.Value;
        _pomodoroService.State.LongBreakMinutes = (int)_longBreakInput.Value;
        SavePomodoroState();
        UpdatePomodoroControls(DateTimeOffset.Now);
        if (_settings.DisplayMode == DisplayMode.Pomodoro) UpdatePreview();
    }

    private void SavePomodoroState()
    {
        try
        {
            _settingsStore.SavePomodoro(_pomodoroService.State);
        }
        catch (Exception error)
        {
            SetStatus("番茄钟保存失败", error.Message);
        }
    }

    private void UpdatePomodoroControls(DateTimeOffset now)
    {
        var snapshot = _pomodoroService.GetSnapshot(now);
        _pomodoroStartButton.Text = snapshot.IsRunning ? "暂停" : snapshot.IsPaused ? "继续" : "开始";
        var phase = snapshot.Phase switch
        {
            PomodoroPhase.Focus => "专注中",
            PomodoroPhase.ShortBreak => "短休息",
            PomodoroPhase.LongBreak => "长休息",
            PomodoroPhase.Paused => "已暂停",
            _ => "准备开始"
        };
        var seconds = Math.Max(0, (int)Math.Ceiling(snapshot.Remaining.TotalSeconds));
        _pomodoroStatus.Text = $"{phase} · {seconds / 60:00}:{seconds % 60:00} · 已完成 {snapshot.CompletedFocusSessions} 个番茄";
    }

    private void UpdateSystemStatus()
    {
        _systemStatus.Text = $"CPU {_systemSnapshot.CpuPercent:0}% · 内存 {_systemSnapshot.MemoryPercent:0}% · "
            + $"↓ {StatusCardRenderer.FormatRate(_systemSnapshot.DownloadBytesPerSecond)} · "
            + $"↑ {StatusCardRenderer.FormatRate(_systemSnapshot.UploadBytesPerSecond)}";
    }

    private void UpdateModeControls()
    {
        var isCodex = _settings.DisplayMode == DisplayMode.Codex;
        var isCustom = _settings.DisplayMode == DisplayMode.CustomImage;
        var isPomodoro = _settings.DisplayMode == DisplayMode.Pomodoro;
        var isSystem = _settings.DisplayMode == DisplayMode.SystemMonitor;
        _chooseImageButton.Visible = isCustom;
        _customImageName.Visible = isCustom;
        _chooseImageButton.Enabled = isCustom;
        _customImageName.Enabled = isCustom;
        _pomodoroPanel.Visible = isPomodoro;
        _systemPanel.Visible = isSystem;
        if (_modeOptionsGroup is not null)
            _modeOptionsGroup.Visible = isPomodoro || isSystem;
        _intervalCombo.Enabled = isCodex;
        _refreshButton.Enabled = isCodex;
        _themeCombo.Enabled = !isCustom;
    }

    private void RestartTimer()
    {
        _refreshTimer.Stop();
        if (_settings.DisplayMode != DisplayMode.Codex) return;
        _refreshTimer.Interval = checked(_settings.RefreshIntervalSeconds * 1000);
        _refreshTimer.Start();
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _pushButton.Enabled = !busy;
        _refreshButton.Enabled = !busy && _settings.DisplayMode == DisplayMode.Codex;
        _chooseImageButton.Enabled = !busy && _settings.DisplayMode == DisplayMode.CustomImage;
        _pomodoroStartButton.Enabled = !busy;
        _pomodoroSkipButton.Enabled = !busy;
        _pomodoroResetButton.Enabled = !busy;
    }

    private void SetStatus(string status, string? error = null)
    {
        _statusValue.Text = status;
        _errorValue.Text = error ?? string.Empty;
        var trayText = $"Codex 屏显 · {status}";
        _notifyIcon.Text = trayText.Length > 63 ? trayText[..63] : trayText;
    }

    private static string FormatNow() => DateTime.Now.ToString("M月d日 HH:mm:ss");

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowExit) return;
        e.Cancel = true;
        Hide();
        _notifyIcon.ShowBalloonTip(1200, "Codex 屏显仍在运行",
            "双击托盘图标可重新打开设置。", ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _allowExit = true;
        Close();
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath)
                ?? (Icon)SystemIcons.Application.Clone();
        }
        catch
        {
            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
