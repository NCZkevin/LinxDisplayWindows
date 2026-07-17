using System.Security.Cryptography;
using CodexLinxDisplay.Windows.Models;
using CodexLinxDisplay.Windows.Services;

namespace CodexLinxDisplay.Windows;

internal sealed class MainForm : Form
{
    private readonly SettingsStore _settingsStore = new();
    private readonly CodexRateLimitClient _codexClient = new();
    private readonly ImageApiClient _imageClient = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = 1_000 };
    private readonly NotifyIcon _notifyIcon = new();

    private readonly ComboBox _modeCombo = new();
    private readonly TextBox _endpointText = new();
    private readonly ComboBox _intervalCombo = new();
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

    private AppSettings _settings;
    private UsageSnapshot? _snapshot;
    private Bitmap? _customSource;
    private string? _lastUploadedHash;
    private string? _lastRenderedMinute;
    private bool _allowExit;
    private bool _suppressEvents;

    public MainForm(bool testMode = false)
    {
        _settings = _settingsStore.Load();
        Text = "Codex 屏显 for Linx68";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(960, 680);
        Size = new Size(1_100, 820);
        Font = new Font("Microsoft YaHei UI", 9F);

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
        _notifyIcon.Icon = SystemIcons.Application;
        _notifyIcon.Text = "Codex 屏显";
        _notifyIcon.Visible = !testMode;
        _notifyIcon.ContextMenuStrip = trayMenu;
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();

        _refreshTimer.Tick += async (_, _) => await SynchronizeCodexAsync(upload: true, forceUpload: false);
        _clockTimer.Tick += async (_, _) => await RefreshClockIfNeededAsync();
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
            RowCount = 5,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        settingsStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settingsHost.Controls.Add(settingsStack);
        settingsStack.Controls.Add(BuildDisplaySection(), 0, 0);
        settingsStack.Controls.Add(BuildEndpointSection(), 0, 1);
        settingsStack.Controls.Add(BuildRefreshSection(), 0, 2);
        settingsStack.Controls.Add(BuildLayoutSection(), 0, 3);
        settingsStack.Controls.Add(BuildStatusSection(), 0, 4);

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
        _modeCombo.Items.AddRange(["Codex 用量", "自定义图片"]);
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
        grid.Controls.Add(NewLabel("顶部安全区"), 0, 0);
        _safeAreaInput.Minimum = 44;
        _safeAreaInput.Maximum = 80;
        _safeAreaInput.Width = 80;
        _safeAreaInput.Anchor = AnchorStyles.Left;
        _safeAreaInput.ValueChanged += (_, _) => HandleRenderSettingChanged();
        grid.Controls.Add(_safeAreaInput, 1, 0);
        grid.Controls.Add(NewLabel("px"), 2, 0);

        grid.Controls.Add(NewLabel("JPEG 质量"), 0, 1);
        _qualitySlider.Minimum = 50;
        _qualitySlider.Maximum = 100;
        _qualitySlider.TickFrequency = 10;
        _qualitySlider.Dock = DockStyle.Fill;
        _qualitySlider.AutoSize = true;
        _qualitySlider.ValueChanged += (_, _) => HandleRenderSettingChanged();
        grid.Controls.Add(_qualitySlider, 1, 1);
        _qualityValue.AutoSize = true;
        _qualityValue.TextAlign = ContentAlignment.MiddleRight;
        _qualityValue.Anchor = AnchorStyles.Right;
        grid.Controls.Add(_qualityValue, 2, 1);
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
        _modeCombo.SelectedIndex = _settings.DisplayMode == DisplayMode.Codex ? 0 : 1;
        _endpointText.Text = _settings.Endpoint;
        _intervalCombo.SelectedIndex = _settings.RefreshIntervalSeconds switch
        {
            60 => 0,
            600 => 2,
            1800 => 3,
            _ => 1
        };
        _safeAreaInput.Value = _settings.SafeAreaHeight;
        _qualitySlider.Value = _settings.JpegQuality;
        _qualityValue.Text = $"{_settings.JpegQuality}%";
        _startupCheck.Checked = StartupManager.IsEnabled();
        _customImageName.Text = _settings.CustomImageName ?? "尚未选择图片";
        _statusValue.Text = "等待首次同步";
        _lastRefreshValue.Text = "尚未刷新";
        _lastUploadValue.Text = "尚未推送";
        _suppressEvents = false;
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
        if (_settings.DisplayMode == DisplayMode.Codex)
            await SynchronizeCodexAsync(upload: true, forceUpload: true);
        else if (_customSource is not null)
            await PushCustomImageAsync(forceUpload: true);
        else
            SetStatus("请选择一张图片");
    }

    private async Task ChangeModeAsync()
    {
        if (_suppressEvents) return;
        _settings.DisplayMode = _modeCombo.SelectedIndex == 0 ? DisplayMode.Codex : DisplayMode.CustomImage;
        var expectedMode = _settings.DisplayMode;
        _lastUploadedHash = null;
        SaveSettingsFromControls();
        UpdateModeControls();
        UpdatePreview();
        RestartTimer();

        // A previous mode may still be waiting for Codex or the keyboard. Wait for it to finish so
        // switching modes always performs the promised immediate push.
        await _syncLock.WaitAsync();
        _syncLock.Release();
        if (_settings.DisplayMode != expectedMode) return;

        if (expectedMode == DisplayMode.Codex)
            await SynchronizeCodexAsync(upload: true, forceUpload: true);
        else if (_customSource is not null)
            await PushCustomImageAsync(forceUpload: true);
        else
            SetStatus("请选择一张图片");
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
                _modeCombo.SelectedIndex = 1;
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
        if (_settings.DisplayMode == DisplayMode.Codex)
            await SynchronizeCodexAsync(upload: true, forceUpload);
        else
            await PushCustomImageAsync(forceUpload);
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

    private async Task RefreshClockIfNeededAsync()
    {
        if (_settings.DisplayMode != DisplayMode.Codex || _snapshot is null) return;
        var currentMinute = DateTime.Now.ToString("yyyyMMddHHmm");
        if (currentMinute == _lastRenderedMinute) return;
        if (!await _syncLock.WaitAsync(0)) return;

        try
        {
            UpdatePreview();
            await UploadPreviewAsync(forceUpload: false, unchangedStatus: "时间未变化");
        }
        catch (Exception error)
        {
            SetStatus("时钟推送失败", error.Message);
        }
        finally
        {
            _syncLock.Release();
        }
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
            else
            {
                var now = DateTimeOffset.Now;
                next = ScreenImageRenderer.RenderUsage(_snapshot ?? UsageSnapshot.Sample,
                    _settings.SafeAreaHeight, now);
                _lastRenderedMinute = now.ToString("yyyyMMddHHmm");
                _previewCaption.Text = $"Codex 用量\r\n顶部 {_settings.SafeAreaHeight}px 留空";
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

    private void UpdateModeControls()
    {
        var isCodex = _settings.DisplayMode == DisplayMode.Codex;
        _chooseImageButton.Enabled = !isCodex;
        _customImageName.Enabled = !isCodex;
        _intervalCombo.Enabled = isCodex;
        _refreshButton.Enabled = isCodex;
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
}
