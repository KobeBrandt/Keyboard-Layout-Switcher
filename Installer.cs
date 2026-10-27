using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace KeyboardLayoutSwitcher;

/// <summary>
/// Lets the single exe install itself for the current user, without admin rights:
/// a copy in %LOCALAPPDATA%\Programs, a Start menu shortcut and an entry in Settings > Apps.
/// </summary>
internal static class Installer
{
    private const string AppName = "Keyboard Layout Switcher";
    private const string ExeName = "KeyboardLayoutSwitcher.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyboardLayoutSwitcher";

    /// <summary>A running instance exits when this event is set, so its exe can be replaced or removed.</summary>
    public const string ExitEventName = @"Local\KeyboardLayoutSwitcher.Exit";

    public const string UninstallArgument = "--uninstall";

    public static string InstallFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KeyboardLayoutSwitcher");

    public static string InstalledExe => Path.Combine(InstallFolder, ExeName);

    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

    private static string CurrentExe => Environment.ProcessPath ?? throw new InvalidOperationException("Unknown exe path.");

    /// <summary>
    /// True for the published single-file exe started from anywhere but the install folder.
    /// Development builds (a dll next to the exe) never install themselves.
    /// </summary>
    public static bool ShouldOfferInstall()
    {
#pragma warning disable IL3000 // An empty location is exactly how a single-file exe is detected.
        var isSingleFile = string.IsNullOrEmpty(typeof(Installer).Assembly.Location);
#pragma warning restore IL3000
        return isSingleFile && !string.Equals(Path.GetFullPath(CurrentExe), InstalledExe, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Offers to install or update. Returns true when this process should exit
    /// (the installed copy was started, or the user cancelled); false to run from here.
    /// </summary>
    public static bool InstallOrUpdate()
    {
        var thisVersion = FileVersionInfo.GetVersionInfo(CurrentExe).FileVersion;
        var installedVersion = File.Exists(InstalledExe) ? FileVersionInfo.GetVersionInfo(InstalledExe).FileVersion : null;

        if (installedVersion != null && installedVersion == thisVersion)
        {
            // This version is already installed: start that copy instead.
            Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true });
            return true;
        }

        var fresh = installedVersion == null;
        var question = fresh
            ? $"Install {AppName} for your account?\n\n"
              + $"It is copied to {InstallFolder}, added to the Start menu and set to start with Windows. "
              + "You can remove it in Settings > Apps.\n\n"
              + "Choose No to run it from this location without installing."
            : $"Update the installed {AppName} from version {installedVersion} to {thisVersion}?\n\n"
              + "Choose No to run this version from this location without installing.";
        var answer = MessageBox.Show(question, AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel)
            return true;
        if (answer == DialogResult.No)
            return false;

        try
        {
            StopRunningInstances();
            Directory.CreateDirectory(InstallFolder);
            CopyWithRetry(CurrentExe, InstalledExe);
            CreateShortcut();
            WriteUninstallEntry();
            if (fresh)
                Startup.Register(InstalledExe);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not install {AppName}:\n{ex.Message}\n\nIt runs from this location instead.",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true });
        return true;
    }

    /// <summary>Runs from Settings > Apps: removes everything the install added.</summary>
    public static void Uninstall()
    {
        var deleteSettings = MessageBox.Show(
            $"Also delete your settings and log in {AppConfig.FolderPath}?\n\nChoose No to keep them for a later install.",
            $"Uninstall {AppName}", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

        var problems = new List<string>();
        void Try(Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                problems.Add(ex.Message);
            }
        }

        Try(StopRunningInstances);
        Try(() => Startup.IsEnabled = false);
        Try(() => File.Delete(ShortcutPath));
        Try(() => Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false));
        if (deleteSettings && Directory.Exists(AppConfig.FolderPath))
            Try(() => Directory.Delete(AppConfig.FolderPath, recursive: true));
        if (Directory.Exists(InstallFolder))
            Try(DeleteInstallFolderAfterExit);

        if (problems.Count == 0)
            MessageBox.Show($"{AppName} was removed.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        else
            MessageBox.Show($"{AppName} was removed, but some parts could not be cleaned up:\n\n" + string.Join("\n", problems),
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Asks every other running instance to exit, and ends it if it does not within a few seconds.</summary>
    private static void StopRunningInstances()
    {
        using var exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        var others = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName))
            .Where(p => p.Id != Environment.ProcessId).ToList();
        foreach (var process in others)
        {
            using (process)
            {
                exit.Set();
                // Versions before 1.1 do not listen for the event.
                if (!process.WaitForExit(5000))
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
            }
        }
        // Clear a signal nobody took, so the next instance does not exit right away.
        exit.Reset();
    }

    private static void CopyWithRetry(string source, string destination)
    {
        // The exe that just exited can stay locked for a moment.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(300);
            }
        }
    }

    private static void WriteUninstallEntry()
    {
        var version = typeof(Installer).Assembly.GetName().Version;
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", version?.ToString(3) ?? "");
        key.SetValue("Publisher", Application.CompanyName ?? "");
        key.SetValue("DisplayIcon", $"{InstalledExe},0");
        key.SetValue("InstallLocation", InstallFolder);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" {UninstallArgument}");
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("URLInfoAbout", "https://github.com/KobeBrandt/Keyboard-Layout-Switcher");
        key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    /// <summary>
    /// A running exe cannot delete itself, so a hidden command prompt removes the folder
    /// a few seconds after this process has exited.
    /// </summary>
    private static void DeleteInstallFolderAfterExit()
    {
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{InstallFolder}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        });
    }

    private static void CreateShortcut()
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(InstalledExe);
            link.SetWorkingDirectory(InstallFolder);
            link.SetDescription("Switches the keyboard layout based on which keyboards are connected.");
            link.SetIconLocation(InstalledExe, 0);
            ((IPersistFile)link).Save(ShortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxChars, IntPtr findData, int flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxChars);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxChars);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxChars);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxChars, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, int reserved);
        void Resolve(IntPtr hwnd, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
