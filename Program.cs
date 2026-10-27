namespace KeyboardLayoutSwitcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Contains(Installer.UninstallArgument, StringComparer.OrdinalIgnoreCase))
        {
            Installer.Uninstall();
            return;
        }

        // Started from a download: offer to install, and hand over to the installed copy.
        if (Installer.ShouldOfferInstall() && Installer.InstallOrUpdate())
            return;

        using var mutex = new Mutex(true, @"Local\KeyboardLayoutSwitcher.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Keyboard Layout Switcher is already running. Look for its icon in the notification area.",
                "Keyboard Layout Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.Run(new TrayApp());
    }
}
