namespace LinxDisplay.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(true, "Local\\LinxDisplay.Windows", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("LinxDisplay 已经在运行。请在系统托盘中找到它。", "LinxDisplay",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
