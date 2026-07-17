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
    private bool _allowExit;
    private bool _suppressEvents;

    public MainForm()
    {
        _settings = _settingsStore.Load();
        Text = "Codex 屏显 for Linx68";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(680, 650);
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
        _notifyIcon.Visible = true;
        _notifyIcon.ContextMenuStrip = trayMenu;
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();

        _refreshTimer.Tick += async (_, _) => await SynchronizeCodexAsync(upload: true, forceUpload: false);
        FormClosing += HandleFormClosing;
        Shown += HandleShown;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
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
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 1
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var settingsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 0, 12, 0)
        };
        root.Controls.Add(settingsPanel, 0, 0);

        settingsPanel.Controls.Add(BuildDisplaySection());
        settingsPanel.Controls.Add(BuildEndpointSection());
        settingsPanel.Controls.Add(BuildRefreshSection());
        settingsPanel.Controls.Add(BuildLayoutSection());
        settingsPanel.Controls.Add(BuildStatusSection());

        var previewPanel = new Panel { Dock = DockStyle.Fill };
        root.Controls.Add(previewPanel, 1, 0);
        var previewTitle = new Label
        {
            Text = "键盘预览",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(13, 4)
        };
        previewPanel.Controls.Add(previewTitle);
        _preview.Location = new Point(7, 34);
        _preview.Size = new Size(ScreenImageRenderer.Width, ScreenImageRenderer.Height);
        _preview.SizeMode = PictureBoxSizeMode.Normal;
        _preview.BackColor = Color.FromArgb(8, 11, 18);
        _preview.BorderStyle = BorderStyle.FixedSingle;
        previewPanel.Controls.Add(_preview);
        _previewCaption.Location = new Point(0, 473);
        _previewCaption.Size = new Size(158, 44);
        _previewCaption.TextAlign = ContentAlignment.TopCenter;
        _previewCaption.ForeColor = SystemColors.GrayText;
        previewPanel.Controls.Add(_previewCaption);
    }

    private GroupBox BuildDisplaySection()
    {
        var group = NewGroup("显示内容", 105);
        group.Controls.Add(NewLabel("显示模式", 12, 29, 72));
        _modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeCombo.Items.AddRange(["Codex 用量", "自定义图片"]);
        _modeCombo.SetBounds(92, 25, 337, 28);
        _modeCombo.SelectedIndexChanged += async (_, _) => await ChangeModeAsync();
        group.Controls.Add(_modeCombo);

        _chooseImageButton.Text = "选择图片…";
        _chooseImageButton.SetBounds(12, 63, 92, 28);
        _chooseImageButton.Click += async (_, _) => await ChooseCustomImageAsync();
        group.Controls.Add(_chooseImageButton);
        _customImageName.SetBounds(114, 67, 315, 22);
        _customImageName.ForeColor = SystemColors.GrayText;
        _customImageName.AutoEllipsis = true;
        group.Controls.Add(_customImageName);
        return group;
    }

    private GroupBox BuildEndpointSection()
    {
        var group = NewGroup("设备接口", 112);
        group.Controls.Add(NewLabel("图像 API", 12, 29, 72));
        _endpointText.SetBounds(92, 25, 337, 27);
        _endpointText.PlaceholderText = "http://键盘地址/image/upload";
        _endpointText.Leave += (_, _) => SaveSettingsFromControls();
        group.Controls.Add(_endpointText);

        var requestLabel = NewLabel("POST · image/jpeg", 92, 58, 180);
        requestLabel.ForeColor = SystemColors.GrayText;
        group.Controls.Add(requestLabel);
        _pushButton.Text = "立即推送当前内容";
        _pushButton.SetBounds(278, 61, 151, 30);
        _pushButton.Click += async (_, _) => await PushCurrentAsync(true);
        group.Controls.Add(_pushButton);
        return group;
    }

    private GroupBox BuildRefreshSection()
    {
        var group = NewGroup("后台刷新", 108);
        group.Controls.Add(NewLabel("刷新间隔", 12, 29, 72));
        _intervalCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _intervalCombo.Items.AddRange(["1 分钟", "5 分钟", "10 分钟", "30 分钟"]);
        _intervalCombo.SetBounds(92, 25, 150, 28);
        _intervalCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            SaveSettingsFromControls();
            RestartTimer();
        };
        group.Controls.Add(_intervalCombo);
        _refreshButton.Text = "只刷新 Codex";
        _refreshButton.SetBounds(278, 24, 151, 30);
        _refreshButton.Click += async (_, _) => await SynchronizeCodexAsync(false, false);
        group.Controls.Add(_refreshButton);

        _startupCheck.Text = "登录 Windows 时自动启动（后台运行）";
        _startupCheck.SetBounds(12, 67, 330, 25);
        _startupCheck.CheckedChanged += (_, _) => ChangeStartupSetting();
        group.Controls.Add(_startupCheck);
        return group;
    }

    private GroupBox BuildLayoutSection()
    {
        var group = NewGroup("屏幕布局", 112);
        group.Controls.Add(NewLabel("顶部安全区", 12, 29, 80));
        _safeAreaInput.Minimum = 44;
        _safeAreaInput.Maximum = 80;
        _safeAreaInput.SetBounds(103, 25, 72, 28);
        _safeAreaInput.ValueChanged += (_, _) => HandleRenderSettingChanged();
        group.Controls.Add(_safeAreaInput);
        group.Controls.Add(NewLabel("px", 179, 29, 25));

        group.Controls.Add(NewLabel("JPEG 质量", 12, 72, 80));
        _qualitySlider.Minimum = 50;
        _qualitySlider.Maximum = 100;
        _qualitySlider.TickFrequency = 10;
        _qualitySlider.SetBounds(92, 61, 280, 40);
        _qualitySlider.ValueChanged += (_, _) => HandleRenderSettingChanged();
        group.Controls.Add(_qualitySlider);
        _qualityValue.SetBounds(375, 71, 54, 22);
        _qualityValue.TextAlign = ContentAlignment.MiddleRight;
        group.Controls.Add(_qualityValue);
        return group;
    }

    private GroupBox BuildStatusSection()
    {
        var group = NewGroup("运行状态", 142);
        group.Controls.Add(NewLabel("状态", 12, 27, 98));
        _statusValue.SetBounds(116, 27, 313, 20);
        group.Controls.Add(_statusValue);
        group.Controls.Add(NewLabel("上次 Codex 刷新", 12, 52, 105));
        _lastRefreshValue.SetBounds(116, 52, 313, 20);
        group.Controls.Add(_lastRefreshValue);
        group.Controls.Add(NewLabel("上次推送", 12, 77, 98));
        _lastUploadValue.SetBounds(116, 77, 313, 20);
        group.Controls.Add(_lastUploadValue);
        _errorValue.SetBounds(12, 103, 417, 31);
        _errorValue.ForeColor = Color.Firebrick;
        _errorValue.AutoEllipsis = true;
        group.Controls.Add(_errorValue);
        return group;
    }

    private GroupBox NewGroup(string title, int height) => new()
    {
        Text = title,
        Width = 445,
        Height = height,
        Margin = new Padding(0, 0, 0, 9)
    };

    private static Label NewLabel(string text, int x, int y, int width) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(width, 22),
        TextAlign = ContentAlignment.MiddleLeft
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
                next = ScreenImageRenderer.RenderUsage(_snapshot ?? UsageSnapshot.Sample,
                    _settings.SafeAreaHeight);
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
