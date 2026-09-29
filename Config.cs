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
            MessageBox.Show($"Could not read {FilePath}:\n{ex.Message}\n\nStarting with empty settings.",
                "Keyboard Layout Switcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return new AppConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(FolderPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public AppConfig Clone() =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
}
