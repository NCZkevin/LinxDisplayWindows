namespace CodexLinxDisplay.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(true, "Local\\CodexLinxDisplay.Windows", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Codex 屏显已经在运行。请在系统托盘中找到它。", "Codex 屏显",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
