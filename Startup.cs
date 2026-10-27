using Microsoft.Win32;

namespace KeyboardLayoutSwitcher;

/// <summary>Adds or removes the app from HKCU\...\Run so it starts when the user signs in.</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeyboardLayoutSwitcher";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value
                && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(ValueName, Command);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// When start with Windows is on but points to another path (the exe was moved),
    /// points it to this exe again.
    /// </summary>
    public static void RepairPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string value && !string.Equals(value, Command, StringComparison.OrdinalIgnoreCase))
            key.SetValue(ValueName, Command);
    }
}
