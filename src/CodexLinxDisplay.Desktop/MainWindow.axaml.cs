using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace CodexLinxDisplay.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void ChooseImageClick(object? sender, RoutedEventArgs args)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要显示的图片",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图片") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp", "*.bmp"] }
            ]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) await _viewModel.SetCustomImageAsync(path);
    }

    protected override void OnClosing(WindowClosingEventArgs args)
    {
        if (!App.IsExiting && App.MinimizeToTray)
        {
            args.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(args);
    }
}
