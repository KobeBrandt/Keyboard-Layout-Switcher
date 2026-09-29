namespace KeyboardLayoutSwitcher;

internal sealed class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly DeviceWatcher _watcher;
    private readonly List<InputLanguage> _layouts = Layouts.Installed();

    // Sizes come from the content (AutoSize, Dock, Anchor) instead of fixed pixel widths,
    // so the window works at any display scaling.
    private readonly ComboBox _defaultLayout = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ListView _list = new()
    {
        View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
        Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable, ShowItemToolTips = true,
    };
    private readonly ComboBox _selectedLayout = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Enabled = false };
    private readonly Button _add = new() { Text = "Add keyboard…" };
    private readonly Button _rename = new() { Text = "Rename…" };
    private readonly Button _remove = new() { Text = "Remove" };
    private readonly Button _up = new() { Text = "Move up" };
    private readonly Button _down = new() { Text = "Move down" };
    private readonly CheckBox _notifications = new() { Text = "Show a notification when the layout changes", AutoSize = true };
    private readonly CheckBox _startup = new() { Text = "Start with Windows", AutoSize = true };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 2000 };

    private bool _updatingSelection;

    /// <summary>The edited settings. Only valid when the dialog returns OK.</summary>
    public AppConfig Config => _config;
    public bool StartWithWindows => _startup.Checked;

    public SettingsForm(AppConfig config, DeviceWatcher watcher)
    {
        _config = config.Clone();
        _watcher = watcher;

        SuspendLayout();
        Text = "Keyboard Layout Switcher – Settings";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 440);
        MinimumSize = new Size(520, 400);
        Padding = new Padding(12);
        Icon = TrayApp.CreateIcon("KB");

        _list.Columns.Add("Keyboard");
        _list.Columns.Add("Layout");
        _list.Columns.Add("Status");
        _list.Resize += (_, _) => FitColumns();

        foreach (var language in _layouts)
        {
            _defaultLayout.Items.Add(Layouts.Describe(language));
            _selectedLayout.Items.Add(Layouts.Describe(language));
        }

        var defaultLanguage = Layouts.Resolve(_config.DefaultLayout);
        _defaultLayout.SelectedIndex = defaultLanguage != null ? _layouts.IndexOf(defaultLanguage) : -1;
        _notifications.Checked = _config.ShowNotifications;
        _startup.Checked = Startup.IsEnabled;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 7; row++)
            root.RowStyles.Add(row == 2 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));

        // Row 0: default layout (label + combo that stretches).
        var defaultRow = TwoColumnRow("Default layout:", _defaultLayout);
        root.Controls.Add(defaultRow);
        root.Controls.Add(HintLabel("Used when none of the keyboards below is connected."));

        // Row 2: keyboard list with buttons on the right. The list takes all spare space.
        var middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 12, 0, 0) };
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        middle.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var sideButtons = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top, Margin = new Padding(8, 0, 0, 0) };
        sideButtons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var button in new[] { _add, _rename, _remove, _up, _down })
        {
            // Dock Fill in an AutoSize column gives every button the width of the widest one.
            button.AutoSize = true;
            button.Dock = DockStyle.Fill;
            button.Padding = new Padding(8, 2, 8, 2);
            sideButtons.Controls.Add(button);
        }
        middle.Controls.Add(_list, 0, 0);
        middle.Controls.Add(sideButtons, 1, 0);
        root.Controls.Add(middle);

        root.Controls.Add(HintLabel("When several keyboards are connected, the one highest in the list wins."));

        var layoutRow = TwoColumnRow("Layout for selected keyboard:", _selectedLayout);
        layoutRow.Margin = new Padding(0, 8, 0, 0);
        root.Controls.Add(layoutRow);

        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
        options.Controls.Add(_notifications);
        options.Controls.Add(_startup);
        root.Controls.Add(options);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(16, 2, 16, 2) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(16, 2, 16, 2) };
        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(ok);
        root.Controls.Add(bottom);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;

        _add.Click += (_, _) => AddKeyboard();
        _rename.Click += (_, _) => RenameSelected();
        _remove.Click += (_, _) => RemoveSelected();
        _up.Click += (_, _) => MoveSelected(-1);
        _down.Click += (_, _) => MoveSelected(1);
        _list.SelectedIndexChanged += (_, _) => UpdateSelection();
        _list.DoubleClick += (_, _) => RenameSelected();
        _selectedLayout.SelectedIndexChanged += (_, _) => ChangeSelectedLayout();
        ok.Click += (_, _) => SaveOptions();
        _watcher.DevicesChanged += OnDevicesChanged;
        // Bluetooth keyboards connect and disconnect without a device event, so poll the status column.
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();

        RefreshList();

        // Sizes above are in 96-DPI pixels; scale them to the display's scaling factor.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitColumns();
        // Opened from the tray, the window can end up behind other windows. Bring it to the front.
        Activate();
    }

    private static TableLayoutPanel TwoColumnRow(string label, Control control)
    {
        var row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) }, 0, 0);
        row.Controls.Add(control, 1, 0);
        return row;
    }

    private static Label HintLabel(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 4, 0, 0),
    };

    /// <summary>Spreads the columns over the list width so there is no horizontal scroll bar.</summary>
    private void FitColumns()
    {
        var width = _list.ClientSize.Width;
        if (width <= 0 || _list.Columns.Count < 3)
            return;
        _list.Columns[0].Width = width * 35 / 100;
        _list.Columns[1].Width = width * 43 / 100;
        _list.Columns[2].Width = width - _list.Columns[0].Width - _list.Columns[1].Width;
    }

    private void UpdateStatus()
    {
        var connected = Keyboards.GetConnected().Select(k => k.HardwareId).ToHashSet();
        for (var i = 0; i < _list.Items.Count && i < _config.Keyboards.Count; i++)
        {
            var status = connected.Contains(_config.Keyboards[i].HardwareId) ? "Connected" : "Not connected";
            if (_list.Items[i].SubItems[2].Text != status)
                _list.Items[i].SubItems[2].Text = status;
        }
    }

    private int SelectedIndex => _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;

    private void RefreshList(int select = -1)
    {
        var connected = Keyboards.GetConnected().Select(k => k.HardwareId).ToHashSet();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var rule in _config.Keyboards)
        {
            var language = Layouts.Resolve(rule.Layout);
            var item = new ListViewItem(rule.Name);
            item.SubItems.Add(language != null ? Layouts.Describe(language) : $"{rule.Layout} – not installed");
            item.SubItems.Add(connected.Contains(rule.HardwareId) ? "Connected" : "Not connected");
            item.ToolTipText = rule.HardwareId;
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        if (select >= 0 && select < _list.Items.Count)
            _list.Items[select].Selected = true;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var index = SelectedIndex;
        var hasSelection = index >= 0;
        _rename.Enabled = _remove.Enabled = _selectedLayout.Enabled = hasSelection;
        _up.Enabled = index > 0;
        _down.Enabled = hasSelection && index < _config.Keyboards.Count - 1;

        _updatingSelection = true;
        var language = hasSelection ? Layouts.Resolve(_config.Keyboards[index].Layout) : null;
        _selectedLayout.SelectedIndex = language != null ? _layouts.IndexOf(language) : -1;
        _updatingSelection = false;
    }

    private void AddKeyboard()
    {
        var assigned = _config.Keyboards.Select(k => k.HardwareId).ToHashSet();
        using var dialog = new AddKeyboardForm(_watcher, assigned);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result != null)
        {
            _config.Keyboards.Add(dialog.Result);
            RefreshList(_config.Keyboards.Count - 1);
        }
    }

    private void RenameSelected()
    {
        var index = SelectedIndex;
        if (index < 0)
            return;
        var name = Prompt("Rename keyboard", "Name:", _config.Keyboards[index].Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _config.Keyboards[index].Name = name.Trim();
            RefreshList(index);
        }
    }

    private void RemoveSelected()
    {
        var index = SelectedIndex;
        if (index < 0)
            return;
        _config.Keyboards.RemoveAt(index);
        RefreshList(Math.Min(index, _config.Keyboards.Count - 1));
    }

    private void MoveSelected(int offset)
    {
        var index = SelectedIndex;
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _config.Keyboards.Count)
            return;
        (_config.Keyboards[index], _config.Keyboards[target]) = (_config.Keyboards[target], _config.Keyboards[index]);
        RefreshList(target);
    }

    private void ChangeSelectedLayout()
    {
        var index = SelectedIndex;
        if (_updatingSelection || index < 0 || _selectedLayout.SelectedIndex < 0)
            return;
        _config.Keyboards[index].Layout = Layouts.ToRef(_layouts[_selectedLayout.SelectedIndex]);
        RefreshList(index);
    }

    private void SaveOptions()
    {
        if (_defaultLayout.SelectedIndex >= 0)
            _config.DefaultLayout = Layouts.ToRef(_layouts[_defaultLayout.SelectedIndex]);
        _config.ShowNotifications = _notifications.Checked;
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed)
            BeginInvoke(() => RefreshList(SelectedIndex));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _watcher.DevicesChanged -= OnDevicesChanged;
        _statusTimer.Dispose();
        Icon?.Dispose();
        base.OnFormClosed(e);
    }

    private string? Prompt(string title, string label, string value)
    {
        using var form = new Form();
        form.SuspendLayout();
        form.Text = title;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = form.MinimizeBox = form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.CenterParent;
        form.AutoSize = true;
        form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        form.Padding = new Padding(12);
        var box = new TextBox { Text = value, Width = 300 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        panel.Controls.Add(new Label { Text = label, AutoSize = true });
        panel.Controls.Add(box);
        panel.Controls.Add(buttons);
        form.Controls.Add(panel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.ResumeLayout(false);
        form.PerformLayout();
        return form.ShowDialog(this) == DialogResult.OK ? box.Text : null;
    }
}
