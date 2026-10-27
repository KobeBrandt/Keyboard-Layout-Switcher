namespace KeyboardLayoutSwitcher;

/// <summary>Optional diagnostic log. Writes nothing unless enabled in Settings.</summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;

    public static bool Enabled { get; set; }

    public static string FilePath => Path.Combine(AppConfig.FolderPath, "log.txt");

    public static void Write(string message)
    {
        if (!Enabled)
            return;
        try
        {
            Directory.CreateDirectory(AppConfig.FolderPath);
            // Keep one older file, so the log cannot grow without limit.
            var file = new FileInfo(FilePath);
            if (file.Exists && file.Length > MaxBytes)
                File.Move(FilePath, Path.Combine(AppConfig.FolderPath, "log.old.txt"), overwrite: true);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Logging must never break switching.
        }
    }
}
