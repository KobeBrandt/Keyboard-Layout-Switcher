using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Win32;

namespace KeyboardLayoutSwitcher;

/// <summary>
/// Lives in the notification area. Picks the layout from the connected keyboards and applies it
/// when a keyboard is connected or disconnected, and again after unlock or resume from sleep.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private AppConfig _config;
    private readonly DeviceWatcher _watcher = new();
    private readonly NotifyIcon _tray = new();
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 800 };
    // Bluetooth keyboards stay present in Windows when they disconnect, so no device event fires.
    // Polling their connection state every 2 seconds catches that.
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };
    private SettingsForm? _settings;
    private string? _iconCode;

    // Layout chosen by the last evaluation. Device events only switch when this changes,
    // so a manual Win+Space switch survives unrelated devices (like a mouse) being plugged in.
    private IntPtr _lastTarget;

    // Set when the foreground window refused the switch (it runs as administrator).
    // The layout is applied again as soon as another window comes to the front.
    private bool _retryOnForeground;
    private readonly Native.WinEventProc _foregroundProc;
    private readonly IntPtr _foregroundHook;

    public TrayApp()
    {
        _config = AppConfig.Load();
        Log.Enabled = _config.EnableLog;
        Log.Write("Started");
        try
        {
            Startup.RepairPath();
        }
        catch (Exception ex)
        {
            Log.Write($"Could not repair the start with Windows path: {ex.Message}");
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Apply now", null, (_, _) => Evaluate(force: true, notify: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowSettings();
        _tray.Visible = true;

        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Evaluate(force: false, notify: true);
        };
        _watcher.DevicesChanged += (_, _) =>
        {
            Log.Write("Device change");
            _debounce.Stop();
            _debounce.Start();
        };
        _poll.Tick += (_, _) => Evaluate(force: false, notify: true);
        _poll.Start();

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Keep the delegate in a field so the garbage collector does not free it while Windows still calls it.
        _foregroundProc = OnForegroundChanged;
        _foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);

        Evaluate(force: true, notify: false);

        if (_config.DefaultLayout == null && _config.Keyboards.Count == 0)
        {
            // First run: start from the layout in use now and let the user assign keyboards.
            _config.DefaultLayout = Layouts.ToRef(InputLanguage.CurrentInputLanguage);
            _config.Save();
            ShowSettings();
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon
            or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        {
            Log.Write($"Session switch: {e.Reason}");
            ReapplySoon();
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Log.Write("Resume from sleep");
            ReapplySoon();
        }
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (!_retryOnForeground)
            return;
        Log.Write("Foreground window changed; applying the layout that was refused before");
        Evaluate(force: true, notify: false);
    }

    /// <summary>
    /// Windows resets the layout while the lock screen closes, so apply twice:
    /// once shortly after unlock, and once more after the desktop has settled.
    /// </summary>
    private void ReapplySoon()
    {
        foreach (var delay in new[] { 1000, 4000 })
        {
            var timer = new System.Windows.Forms.Timer { Interval = delay };
            timer.Tick += (_, _) =>
            {
                timer.Dispose();
                Evaluate(force: true, notify: false);
            };
            timer.Start();
        }
    }

    private void Evaluate(bool force, bool notify)
    {
        var connected = Keyboards.GetConnected().Select(k => k.HardwareId).ToHashSet();
        var (rule, target) = _config.Pick(connected);

        if (target == null)
        {
            UpdateTray(null, "No layout configured");
            return;
        }

        var reason = rule != null ? $"{rule.Name} connected" : "Default layout";
        var changed = target.Handle != _lastTarget;
        _lastTarget = target.Handle;

        if (force || changed)
        {
            _retryOnForeground = !Layouts.Activate(target);
            Log.Write($"Applied {Layouts.Describe(target)} ({reason}; force: {force}, changed: {changed})"
                + (_retryOnForeground ? "; the foreground window refused it, will retry when it changes" : ""));
            if (notify && changed && _config.ShowNotifications)
                _tray.ShowBalloonTip(2000, Layouts.Describe(target), reason, ToolTipIcon.None);
        }
        UpdateTray(target, reason);
    }

    private void UpdateTray(InputLanguage? language, string reason)
    {
        var layoutText = language != null ? Layouts.Describe(language) : "—";
        _status.Text = $"{layoutText} · {reason}";

        var tooltip = $"Keyboard Layout Switcher\n{layoutText}";
        _tray.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;

        // The poll timer calls this every few seconds; only redraw the icon when the code changes.
        var code = language != null ? Layouts.ShortCode(language) : "?";
        if (code == _iconCode)
            return;
        _iconCode = code;
        var oldIcon = _tray.Icon;
        // Draw at the size the notification area uses, so the icon stays sharp at any display scaling.
        _tray.Icon = CreateIcon(code, SystemInformation.SmallIconSize.Width);
        oldIcon?.Dispose();
    }

    /// <summary>Draws a small key cap with the layout's country code, e.g. "US" or "BE".</summary>
    internal static Icon CreateIcon(string text, int size = 32)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            // Draw in a 32 x 32 grid and scale it to the requested size.
            g.ScaleTransform(size / 32f, size / 32f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var path = new GraphicsPath();
            const int r = 7;
            path.AddArc(1, 1, r * 2, r * 2, 180, 90);
            path.AddArc(30 - r * 2, 1, r * 2, r * 2, 270, 90);
            path.AddArc(30 - r * 2, 30 - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(1, 30 - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            using var fill = new SolidBrush(Color.FromArgb(0x25, 0x63, 0xEB));
            g.FillPath(fill, path);

            var fontSize = text.Length switch { <= 1 => 16f, 2 => 13f, _ => 10f };
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, Brushes.White, new RectangleF(0, 1, 32, 32), format);
        }
        // Clone so the returned icon owns its handle and Dispose frees it.
        var handle = bitmap.GetHicon();
        using var temporary = Icon.FromHandle(handle);
        var icon = (Icon)temporary.Clone();
        Native.DestroyIcon(handle);
        return icon;
    }

    private void ShowSettings()
    {
        if (_settings != null)
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsForm(_config, _watcher);
        try
        {
            if (_settings.ShowDialog() == DialogResult.OK)
            {
                _config = _settings.Config;
                Log.Enabled = _config.EnableLog;
                try
                {
                    _config.Save();
                    Startup.IsEnabled = _settings.StartWithWindows;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not save settings:\n{ex.Message}", "Keyboard Layout Switcher",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                Evaluate(force: true, notify: true);
            }
        }
        finally
        {
            _settings.Dispose();
            _settings = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            if (_foregroundHook != IntPtr.Zero)
                Native.UnhookWinEvent(_foregroundHook);
            _tray.Visible = false;
            _tray.Dispose();
            _debounce.Dispose();
            _poll.Dispose();
            _watcher.Dispose();
            _tray.Icon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
