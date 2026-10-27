using System.Text.Json;

namespace KeyboardLayoutSwitcher;

public sealed class KeyboardRule
{
    public string Name { get; set; } = "";
    public string HardwareId { get; set; } = "";
    public LayoutRef Layout { get; set; } = new();
}

public sealed class AppConfig
{
    /// <summary>Layout to use when none of the assigned keyboards is connected.</summary>
    public LayoutRef? DefaultLayout { get; set; }

    /// <summary>Assigned keyboards. When several are connected, the first one in the list wins.</summary>
    public List<KeyboardRule> Keyboards { get; set; } = new();

    public bool ShowNotifications { get; set; } = true;

    /// <summary>Writes switch decisions to <see cref="Log.FilePath"/> to help find out why a layout did not change.</summary>
    public bool EnableLog { get; set; }

    /// <summary>
    /// Picks the layout for the connected keyboards: the first connected keyboard in the list
    /// whose layout is installed, otherwise the default layout. Rule is null for the default.
    /// </summary>
    public (KeyboardRule? Rule, InputLanguage? Layout) Pick(ISet<string> connectedIds)
    {
        var rule = PickRule(connectedIds, layout => Layouts.Resolve(layout) != null);
        return rule != null ? (rule, Layouts.Resolve(rule.Layout)) : (null, Layouts.Resolve(DefaultLayout));
    }

    /// <summary>The first connected keyboard in the list whose layout is installed, or null for the default layout.</summary>
    public KeyboardRule? PickRule(ISet<string> connectedIds, Func<LayoutRef, bool> isInstalled) =>
        Keyboards.FirstOrDefault(k => connectedIds.Contains(k.HardwareId) && isInstalled(k.Layout));

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardLayoutSwitcher");

    public static string FilePath => Path.Combine(FolderPath, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), JsonOptions) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            // Keep the unreadable file, so the next save does not overwrite the user's settings for good.
            var backup = Path.Combine(FolderPath, "config.bad.json");
            var kept = TryMove(FilePath, backup);
            MessageBox.Show($"Could not read {FilePath}:\n{ex.Message}\n\n"
                + (kept ? $"The file was saved as {backup}. " : "") + "Starting with empty settings.",
                "Keyboard Layout Switcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return new AppConfig();
    }

    private static bool TryMove(string source, string destination)
    {
        try
        {
            File.Move(source, destination, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(FolderPath);
        // Write a temporary file first, so a crash during the write cannot leave a half-written config.
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporary, FilePath, overwrite: true);
    }

    public AppConfig Clone() =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
}
