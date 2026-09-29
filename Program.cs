namespace KeyboardLayoutSwitcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\KeyboardLayoutSwitcher.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Keyboard Layout Switcher is already running. Look for its icon in the notification area.",
                "Keyboard Layout Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
