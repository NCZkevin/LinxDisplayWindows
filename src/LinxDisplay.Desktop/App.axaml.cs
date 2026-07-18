using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace LinxDisplay.Desktop;

public sealed partial class App : Application
{
    public static bool IsExiting { get; private set; }
    public static bool MinimizeToTray => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow();
            desktop.MainWindow = window;
            if (desktop.Args?.Contains("--background", StringComparer.OrdinalIgnoreCase) != true)
                window.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void TrayIconClicked(object? sender, EventArgs args) => ShowMainWindow();
    private void OpenWindowClick(object? sender, EventArgs args) => ShowMainWindow();

    private void PushClick(object? sender, EventArgs args)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is MainWindow window)
            window.PushCurrent();
    }

    private void ExitClick(object? sender, EventArgs args)
    {
        IsExiting = true;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void ShowMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } window) return;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
