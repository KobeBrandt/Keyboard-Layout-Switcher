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
    // Set by the installer when it needs to replace or remove this exe.
    private readonly EventWaitHandle _exitSignal = new(false, EventResetMode.AutoReset, Installer.ExitEventName);
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
        _poll.Tick += (_, _) =>
        {
            if (_exitSignal.WaitOne(0))
            {
                Log.Write("Exit requested by the installer");
                ExitThread();
                return;
            }
            Evaluate(force: false, notify: true);
        };
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

    /// <summary>
    /// Draws a key cap with the layout's country code, e.g. "US" or "BE". A number at the end ("US2")
    /// is drawn as dots under the letters, because a third character is unreadable at 16 x 16.
    /// </summary>
    internal static Icon CreateIcon(string text, int size = 32)
    {
        var letters = text.TrimEnd("0123456789".ToCharArray());
        if (letters.Length == 0)
            letters = text;
        var variant = letters.Length < text.Length && int.TryParse(text[letters.Length..], out var number) ? number : 1;
        var dots = variant > 1 ? Math.Min(variant, 3) : 0;

        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // The tile fills the whole icon: at 16 x 16 every pixel counts.
            using var tile = RoundedRectangle(new RectangleF(0, 0, size, size), size * 0.22f);
            using var fill = new SolidBrush(Color.FromArgb(0x25, 0x63, 0xEB));
            g.FillPath(fill, tile);

            // Scale the letters to fill the tile instead of using a fixed font size,
            // and snap their height to whole pixels so the edges stay sharp.
            var boxWidth = size * 0.78f;
            var boxHeight = MathF.Round(size * (dots > 0 ? 0.46f : 0.56f));
            var top = MathF.Round(size * (dots > 0 ? 0.40f : 0.5f) - boxHeight / 2);
            using var glyphs = new GraphicsPath();
            using (var family = new FontFamily("Segoe UI"))
                glyphs.AddString(letters, family, (int)FontStyle.Bold, 100, PointF.Empty, StringFormat.GenericTypographic);
            var bounds = glyphs.GetBounds();
            var scale = Math.Min(boxWidth / bounds.Width, boxHeight / bounds.Height);
            using var transform = new Matrix();
            transform.Translate((size - bounds.Width * scale) / 2, top + (boxHeight - bounds.Height * scale) / 2);
            transform.Scale(scale, scale);
            transform.Translate(-bounds.X, -bounds.Y);
            glyphs.Transform(transform);
            g.FillPath(Brushes.White, glyphs);

            if (dots > 0)
            {
                var diameter = Math.Max(2f, MathF.Round(size * 0.12f));
                var gap = diameter;
                var x = MathF.Round((size - (dots * diameter + (dots - 1) * gap)) / 2);
                var y = MathF.Round(size * 0.80f - diameter / 2);
                for (var i = 0; i < dots; i++, x += diameter + gap)
                {
                    // Small dots look like blurry blobs when round; squares stay crisp.
                    if (diameter < 3)
                        g.FillRectangle(Brushes.White, x, y, diameter, diameter);
                    else
                        g.FillEllipse(Brushes.White, x, y, diameter, diameter);
                }
            }
        }
        // Clone so the returned icon owns its handle and Dispose frees it.
        var handle = bitmap.GetHicon();
        using var temporary = Icon.FromHandle(handle);
        var icon = (Icon)temporary.Clone();
        Native.DestroyIcon(handle);
        return icon;
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
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
            _exitSignal.Dispose();
            _watcher.Dispose();
            _tray.Icon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
