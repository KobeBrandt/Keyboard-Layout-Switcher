namespace KeyboardLayoutSwitcher;

/// <summary>
/// Step 1: the user presses a key on the keyboard to add, so we learn which device it is.
/// Step 2: the user names it and picks the layout it should get.
/// </summary>
internal sealed class AddKeyboardForm : Form
{
    private readonly DeviceWatcher _watcher;
    private readonly ISet<string> _assignedIds;

    private readonly Label _prompt = new() { AutoSize = false, Width = 400, Height = 60, TextAlign = ContentAlignment.MiddleCenter };
    private readonly TableLayoutPanel _details = new() { AutoSize = true, ColumnCount = 2, Visible = false, Padding = new Padding(8) };
    private readonly TextBox _name = new() { Width = 300 };
    private readonly ComboBox _layout = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _deviceInfo = new() { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(300, 0) };
    private readonly Button _ok = new() { Text = "Add", DialogResult = DialogResult.OK, Enabled = false, AutoSize = true };
    private readonly Button _cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, TabStop = false };
    private readonly List<InputLanguage> _layouts = Layouts.Installed();

    // Swallow key presses until the details step is ready, so the key used to identify
    // the keyboard does not click a button or type into the name box.
    private bool _swallowKeys = true;

    public KeyboardRule? Result { get; private set; }

    public AddKeyboardForm(DeviceWatcher watcher, ISet<string> assignedIds)
    {
        _watcher = watcher;
        _assignedIds = assignedIds;

        SuspendLayout();
        Text = "Add keyboard";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        CancelButton = _cancel;

        _prompt.Text = "Press any key on the keyboard you want to add…";
        _prompt.Font = new Font(Font.FontFamily, 11f);

        _details.Controls.Add(new Label { Text = "Name:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _details.Controls.Add(_name, 1, 0);
        _details.Controls.Add(new Label { Text = "Layout:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _details.Controls.Add(_layout, 1, 1);
        _details.Controls.Add(_deviceInfo, 1, 2);

        foreach (var language in _layouts)
            _layout.Items.Add(Layouts.Describe(language));

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_ok);

        var root = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        root.Controls.Add(_prompt);
        root.Controls.Add(_details);
        root.Controls.Add(buttons);
        Controls.Add(root);

        // Sizes above are in 96-DPI pixels; scale them to the display's scaling factor.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();

        _ok.Click += (_, _) => Result = BuildResult();
        _watcher.KeyFromDevice += OnKeyFromDevice;
        _watcher.StartCapture();
    }

    private void OnKeyFromDevice(object? sender, string devicePath)
    {
        _watcher.KeyFromDevice -= OnKeyFromDevice;
        _watcher.StopCapture();

        var hardwareId = Keyboards.ToHardwareId(devicePath);
        if (_assignedIds.Contains(hardwareId))
        {
            MessageBox.Show(this, "This keyboard is already in the list. Select it there to change its layout.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.Cancel;
            return;
        }

        _name.Text = Keyboards.TryGetProductName(devicePath) ?? Keyboards.DescribeHardwareId(hardwareId);
        _name.Tag = hardwareId;
        _deviceInfo.Text = hardwareId;

        var current = Layouts.Current();
        var currentIndex = _layouts.FindIndex(l => l.Handle == current);
        _layout.SelectedIndex = currentIndex >= 0 ? currentIndex : (_layouts.Count > 0 ? 0 : -1);

        _prompt.Text = "Keyboard detected. Choose the layout it should use:";
        _details.Visible = true;
        _ok.Enabled = _layouts.Count > 0;
        AcceptButton = _ok;

        // Wait until the key that identified the keyboard is fully handled before accepting input.
        var timer = new System.Windows.Forms.Timer { Interval = 300 };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            _swallowKeys = false;
            _name.Focus();
            _name.SelectAll();
        };
        timer.Start();
    }

    private KeyboardRule? BuildResult()
    {
        if (_layout.SelectedIndex < 0 || _name.Tag is not string hardwareId)
            return null;
        return new KeyboardRule
        {
            Name = string.IsNullOrWhiteSpace(_name.Text) ? Keyboards.DescribeHardwareId(hardwareId) : _name.Text.Trim(),
            HardwareId = hardwareId,
            Layout = Layouts.ToRef(_layouts[_layout.SelectedIndex]),
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        _swallowKeys || base.ProcessCmdKey(ref msg, keyData);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _watcher.KeyFromDevice -= OnKeyFromDevice;
        _watcher.StopCapture();
        base.OnFormClosed(e);
    }
}
