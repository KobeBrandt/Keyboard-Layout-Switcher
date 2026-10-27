namespace KeyboardLayoutSwitcher;

/// <summary>How a keyboard layout is stored in the config file.</summary>
public sealed class LayoutRef
{
    public string Culture { get; set; } = "";
    public string LayoutName { get; set; } = "";
    public long Handle { get; set; }

    public override string ToString() => $"{LayoutName} ({Culture})";
}

internal static class Layouts
{
    public static List<InputLanguage> Installed() => InputLanguage.InstalledInputLanguages.Cast<InputLanguage>().ToList();

    public static string Describe(InputLanguage language) => $"{language.LayoutName} ({language.Culture.Name})";

    public static LayoutRef ToRef(InputLanguage language) => new()
    {
        Culture = language.Culture.Name,
        LayoutName = language.LayoutName,
        Handle = language.Handle.ToInt64(),
    };

    /// <summary>Finds the installed layout that matches a stored reference, or null when it was uninstalled.</summary>
    public static InputLanguage? Resolve(LayoutRef? layout)
    {
        if (layout == null)
            return null;
        var installed = Installed();
        return installed.FirstOrDefault(l => l.Handle.ToInt64() == layout.Handle)
            ?? installed.FirstOrDefault(l => l.Culture.Name == layout.Culture && l.LayoutName == layout.LayoutName);
    }

    /// <summary>
    /// Short text for the tray icon, e.g. "US" or "BE". When several installed layouts share a code
    /// (US and US Dvorak), the second one gets "US2", the third "US3", and so on.
    /// </summary>
    public static string ShortCode(InputLanguage language)
    {
        var installed = Installed();
        var index = installed.FindIndex(l => l.Handle == language.Handle);
        return index < 0
            ? CultureCode(language.Culture.Name)
            : ShortCodes(installed.Select(l => l.Culture.Name).ToList())[index];
    }

    /// <summary>Short codes for a list of culture names, numbered when codes repeat.</summary>
    public static List<string> ShortCodes(IReadOnlyList<string> cultureNames)
    {
        var seen = new Dictionary<string, int>();
        var result = new List<string>(cultureNames.Count);
        foreach (var name in cultureNames)
        {
            var code = CultureCode(name);
            var count = seen.GetValueOrDefault(code) + 1;
            seen[code] = count;
            result.Add(count == 1 ? code : $"{code}{count}");
        }
        return result;
    }

    /// <summary>The region part of a culture name, e.g. "BE" for "nl-BE", at most two letters.</summary>
    public static string CultureCode(string cultureName)
    {
        var dash = cultureName.LastIndexOf('-');
        var code = dash >= 0 ? cultureName[(dash + 1)..] : cultureName;
        return code.Length > 2 ? code[..2].ToUpperInvariant() : code.ToUpperInvariant();
    }

    /// <summary>Returns the layout that the foreground window uses right now.</summary>
    public static IntPtr Current()
    {
        var window = Native.GetForegroundWindow();
        var thread = window == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(window, IntPtr.Zero);
        return Native.GetKeyboardLayout(thread);
    }

    /// <summary>
    /// Switches the active layout and makes it the default for windows that open later.
    /// With the default Windows setting (one input method for all apps) this changes the layout everywhere.
    /// Returns false when the foreground window could not be switched, for example because it runs as administrator.
    /// </summary>
    public static bool Activate(InputLanguage language)
    {
        var hkl = language.Handle;
        Native.SystemParametersInfo(Native.SPI_SETDEFAULTINPUTLANG, 0, ref hkl, Native.SPIF_SENDCHANGE);

        var foreground = Native.GetForegroundWindow();
        var posted = foreground == IntPtr.Zero
            || Native.PostMessage(foreground, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);

        // Also switch this process, so the tray icon and settings window agree with the rest of the system.
        InputLanguage.CurrentInputLanguage = language;
        return posted;
    }
}
