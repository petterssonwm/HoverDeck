using System.Drawing.Drawing2D;

namespace HoverDeck;

public sealed partial class MainForm
{
    private readonly MenuStrip mainMenu = new() { Name = "WorkspaceMenu", Dock = DockStyle.Fill, BackColor = AppTheme.Bar, ForeColor = Ink, Padding = new(12, 0, 0, 0), LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow };
    private readonly Label selectedClientTitle = new() { AutoSize = true, Font = new("Segoe UI", 12.5f, FontStyle.Bold), ForeColor = Ink, Margin = new(10, 4, 0, 0) };
    private readonly Label layoutTitle = new() { Dock = DockStyle.Fill, Font = new("Segoe UI", 21f, FontStyle.Bold), ForeColor = Ink, BackColor = Color.Transparent, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
    private readonly ToolStripMenuItem sharedSettingsMenu = new("SETTINGS  ▾") { Name = "SettingsMenu" };
    private readonly NavLink clientsLink = new("Clients") { Name = "ClientsView", AutoCheck = false, Checked = true }, identifyLink = new("Identify") { Name = "IdentifyDisplays", AutoCheck = false };
    private const int TitleBarHeight = 44;
    private readonly Panel titleBar = new() { Name = "TitleBar", Dock = DockStyle.Top, Height = TitleBarHeight, BackColor = AppTheme.Bar, Padding = new(20, 0, 0, 0) };
    private readonly LauncherLogo logo = new() { Dock = DockStyle.Left };
    private readonly FlowLayoutPanel titleRight = new() { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, BackColor = AppTheme.Bar, Margin = Padding.Empty, Padding = Padding.Empty };
    private readonly CaptionButton minimizeButton = new(CaptionAction.Minimize) { Height = TitleBarHeight, Margin = Padding.Empty }, maximizeButton = new(CaptionAction.Maximize) { Height = TitleBarHeight, Margin = Padding.Empty }, closeButton = new(CaptionAction.Close) { Height = TitleBarHeight, Margin = Padding.Empty };
    private readonly Panel selectedTile = new() { Size = new(26, 26), Margin = new(0, 5, 0, 0) };
    private TableLayoutPanel sidebar = null!, workspaceContent = null!;
    private readonly Panel launchArea = new() { Dock = DockStyle.Fill, Margin = new(18, 8, 18, 0) };
    private static readonly Font TileCaption = new("Segoe UI", 7.5f, FontStyle.Bold), TileNumber = new("Segoe UI", 10.5f, FontStyle.Bold), CardName = new("Segoe UI", 10.5f, FontStyle.Bold), CardDetail = new("Segoe UI", 8.5f), CardState = new("Segoe UI", 7f, FontStyle.Bold);
    private readonly ToolTip hints = new();

    private void BuildWorkspace()
    {
        // Body: client rail on the left, layout name / map / client details on the right.
        mainArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Background };
        mainArea.ColumnStyles.Add(new(SizeType.Absolute, 270)); mainArea.ColumnStyles.Add(new(SizeType.Percent, 100)); mainArea.RowStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(mainArea); BuildTitleBar(); Controls.Add(titleBar);

        sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = AppTheme.Rail, Padding = new(0, 18, 1, 20), Margin = Padding.Empty };
        foreach (int height in new[] { 22, 50, 36 }) sidebar.RowStyles.Add(new(SizeType.Absolute, height));
        sidebar.RowStyles.Add(new(SizeType.Percent, 100)); sidebar.RowStyles.Add(new(SizeType.Absolute, 0)); sidebar.RowStyles.Add(new(SizeType.Absolute, 62));
        sidebar.Paint += (_, e) => { using var line = new Pen(AppTheme.Line); e.Graphics.DrawLine(line, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height); };
        sidebar.Controls.Add(new CapsLabel("Layout") { Margin = new(18, 4, 0, 0) }, 0, 0);
        profiles.Margin = new(18, 0, 18, 0); profiles.AccessibleName = "Layout"; sidebar.Controls.Add(profiles, 0, 1);
        var clientsRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(18, 0, 18, 0) }; clientsRow.RowStyles.Add(new(SizeType.Percent, 100));
        clientsRow.ColumnStyles.Add(new(SizeType.Percent, 100)); clientsRow.ColumnStyles.Add(new(SizeType.AutoSize));
        clientsRow.Controls.Add(new CapsLabel("Clients") { Anchor = AnchorStyles.Left }, 0, 0); clientsRow.Controls.Add(Stepper(), 1, 0); sidebar.Controls.Add(clientsRow, 0, 2);
        slots.Margin = Padding.Empty; sidebar.Controls.Add(slots, 0, 3);
        status.Dock = DockStyle.Fill; status.Margin = new(18, 0, 18, 0); status.TextAlign = ContentAlignment.BottomLeft; sidebar.Controls.Add(status, 0, 4);
        start.Dock = cancel.Dock = DockStyle.Fill; cancel.Text = "Cancel"; cancel.Font = new("Segoe UI", 10.5f, FontStyle.Bold); cancel.Visible = cancel.Enabled = false;
        launchArea.Controls.Add(start); launchArea.Controls.Add(cancel); sidebar.Controls.Add(launchArea, 0, 5);
        slots.DrawMode = DrawMode.OwnerDrawFixed; slots.ItemHeight = 62; slots.DrawItem += DrawClientItem; mainArea.Controls.Add(sidebar, 0, 0);

        var content = workspaceContent = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new(28, 0, 28, 20), Margin = Padding.Empty, BackColor = Background };
        content.RowStyles.Add(new(SizeType.Absolute, 64)); content.RowStyles.Add(new(SizeType.Percent, 100)); content.RowStyles.Add(new(SizeType.Absolute, 0));
        content.Paint += PaintHeaderGlow; content.Resize += (_, _) => content.Invalidate(new Rectangle(0, 0, content.Width, 180));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
        header.ColumnStyles.Add(new(SizeType.Percent, 100)); header.ColumnStyles.Add(new(SizeType.AutoSize)); header.RowStyles.Add(new(SizeType.Percent, 100));
        header.Controls.Add(layoutTitle, 0, 0);
        var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty, BackColor = Color.Transparent };
        clientsLink.Margin = Padding.Empty; links.Controls.AddRange(new Control[] { clientsLink, arrangeDisplays, identifyLink }); header.Controls.Add(links, 1, 0); content.Controls.Add(header, 0, 0);
        clientsLink.Click += (_, _) => arrangeDisplays.Checked = false; identifyLink.Click += (_, _) => IdentifyDisplays();
        hints.SetToolTip(arrangeDisplays, "Preview and change the Windows monitor arrangement."); hints.SetToolTip(identifyLink, "Show each display's number on that screen.");
        hints.SetToolTip(fitDisplayView, "Zoom the map so every display fits."); hints.SetToolTip(resetDisplayChanges, "Throw away changes you have not applied and reload the Windows arrangement.");
        hints.SetToolTip(applyDisplayChanges, "Change the Windows monitor positions and primary display to match the map. Keep or revert within 20 seconds.");
        mainArea.Controls.Add(content, 1, 0);

        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = Padding.Empty, BackColor = Background };
        canvas.Monitors = monitors; canvas.Layout = layout;
        var details = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new(16, 12, 16, 10), ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        details.RowStyles.Add(new(SizeType.Absolute, 40)); details.RowStyles.Add(new(SizeType.Percent, 100));
        details.Paint += (_, e) => { using var edge = new Pen(AppTheme.Line); e.Graphics.DrawRectangle(edge, 0, 0, details.Width - 1, details.Height - 1); };
        var selected = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty }; selected.RowStyles.Add(new(SizeType.Percent, 100));
        selected.ColumnStyles.Add(new(SizeType.Percent, 100)); foreach (int width in new[] { 128, 120, 124 }) selected.ColumnStyles.Add(new(SizeType.Absolute, width));
        var identity = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        selectedTile.Paint += (_, e) => { if (CurrentSlot is not null) AppTheme.DrawTile(e.Graphics, new Rectangle(0, 0, selectedTile.Width - 1, selectedTile.Height - 1), LayoutCanvas.SlotColors[canvas.Selected % LayoutCanvas.SlotColors.Length], $"{canvas.Selected + 1:00}", TileCaption); };
        launchSummary.AutoSize = true; launchSummary.BackColor = AppTheme.BlueWash; launchSummary.ForeColor = AppTheme.BlueText; launchSummary.Font = new("Segoe UI", 7.5f, FontStyle.Bold); launchSummary.Padding = new(5, 3, 5, 3); launchSummary.Margin = new(10, 9, 0, 0);
        identity.Controls.AddRange(new Control[] { selectedTile, selectedClientTitle, launchSummary }); selected.Controls.Add(identity, 0, 0);
        targetMonitor.Dock = DockStyle.Fill; targetMonitor.Margin = new(0, 2, 8, 6); targetMonitor.AccessibleName = "Target display"; selected.Controls.Add(targetMonitor, 1, 0);
        var fitMenu = new ContextMenuStrip { Renderer = new WorkspaceMenuRenderer() };
        foreach (var preset in new[] { ("Fit screen", ClientFit.Screen), ("Left half", ClientFit.LeftHalf), ("Right half", ClientFit.RightHalf) }) fitMenu.Items.Add(preset.Item1, null, (_, _) => Tile(preset.Item2));
        var fit = Button("Fit client  ▾"); fit.Name = "ClientFitMenu"; fit.Dock = DockStyle.Fill; fit.Margin = new(0, 2, 8, 6); fit.Click += (_, _) => fitMenu.Show(fit, 0, fit.Height); selected.Controls.Add(fit, 2, 0);
        var edit = Button("Edit client…"); edit.Name = "EditClient"; edit.Dock = DockStyle.Fill; edit.Margin = new(0, 2, 0, 6); selected.Controls.Add(edit, 3, 0); details.Controls.Add(selected, 0, 0);
        var fields = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        windowMode.Items.AddRange(new object[] { "Windowed", "Fullscreen windowed" }); windowMode.AccessibleName = "Client window mode"; windowMode.Width = 190;
        fields.Controls.Add(FieldGroup("Window mode", windowMode));
        layer.Name = "ClientLayer"; layer.AccessibleName = "Client layer"; layer.TextAlign = HorizontalAlignment.Center; fields.Controls.Add(FieldGroup("Layer", new FieldBox(layer, 64)));
        foreach (var pair in new[] { ("X", posX, 80), ("Y", posY, 80), ("Width", sizeW, 84), ("Height", sizeH, 84) }) { pair.Item2.AccessibleName = "Client " + pair.Item1; fields.Controls.Add(FieldGroup(pair.Item1, new FieldBox(pair.Item2, pair.Item3))); }
        // Apply positions sits under Edit client and only appears while an open window is away from its spot on the map.
        var fieldsRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty }; fieldsRow.RowStyles.Add(new(SizeType.Percent, 100));
        fieldsRow.ColumnStyles.Add(new(SizeType.Percent, 100)); fieldsRow.ColumnStyles.Add(new(SizeType.Absolute, 124));
        apply.Name = "ApplyPositions"; apply.Size = new(124, 30); apply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; apply.Margin = new(0, 0, 0, 11); apply.Visible = false; AppTheme.Primary(apply);
        fieldsRow.Controls.Add(fields, 0, 0); fieldsRow.Controls.Add(apply, 1, 0); details.Controls.Add(fieldsRow, 0, 1);
        hints.SetToolTip(layer, "Where clients overlap, a higher layer stays in front of a lower one, even when you click or hover over the client behind it. Clients on the same layer behave like normal windows. 1 is the back.");
        hints.SetToolTip(windowMode, "Windowed leaves room for the taskbar. Fullscreen windowed covers the whole display. Changing it refits this client; half-screen presets are kept. WoW's own graphics mode is set inside the game.");
        ConfigureDisplayEditor(editor, sidebar, details); content.Controls.Add(editor, 0, 1);
        var logs = new Panel { Dock = DockStyle.Fill, Padding = new(12), BackColor = Surface, Margin = new(0, 12, 0, 0), Visible = false };
        logs.Controls.Add(logBox); content.Controls.Add(logs, 0, 2);
        activityLog.CheckedChanged += (_, _) => { logs.Visible = activityLog.Checked; content.RowStyles[2].Height = activityLog.Checked ? 137 : 0; };
        start.Text = "Start clients"; hints.SetToolTip(start, "Open missing clients and move every open client into place. Already assigned clients are reused.");
        hints.SetToolTip(apply, "Move the clients that are already open to their positions on the map. Nothing is launched or logged in.");
        Theme(this); slots.BackColor = AppTheme.Rail; logBox.BackColor = Surface; UpdateHotkeyLabel(); delay.Value = settings.DelayMs;
        var trayMenu = new ContextMenuStrip { Renderer = new WorkspaceMenuRenderer() }; trayMenu.Items.Add("Open HoverDeck", null, (_, _) => RestoreWindow()); trayMenu.Items.Add("Toggle WoW hover focus", null, (_, _) => toggle.Checked = !toggle.Checked); trayMenu.Items.Add(new ToolStripSeparator()); trayMenu.Items.Add("Exit and restore Windows settings", null, (_, _) => Close());
        tray = new NotifyIcon { Icon = Icon, Text = "HoverDeck · hover focus off", ContextMenuStrip = trayMenu, Visible = !preview }; tray.DoubleClick += (_, _) => RestoreWindow();
        Resize += (_, _) => { maximizeButton.Invalidate(); if (!preview && WindowState == FormWindowState.Minimized && settings.MinimizeToTray) Hide(); };
        toggle.CheckedChanged += (_, _) => { hoverOn = toggle.Checked; hover?.Enable(hoverOn); toggle.Text = "Hover focus " + (hoverOn ? "on" : "off"); tray.Text = "HoverDeck · hover focus " + (hoverOn ? "on" : "off"); Log(hoverOn ? $"WoW hover focus enabled · {settings.DelayMs} ms." : "WoW hover focus paused."); };
        delay.ValueChanged += (_, _) => { settings.DelayMs = (int)delay.Value; hover?.SetDelay(settings.DelayMs); SaveSoon(); };
        hotkey.Click += (_, _) => ChooseHotkey();
        profiles.SelectedIndexChanged += (_, _) => { if (!syncing && profiles.SelectedItem is Layout chosen) { layout = chosen; settings.ActiveLayout = chosen.Name; launcher.Bindings.Clear(); BindLayout(); SaveSoon(); } };
        count.ValueChanged += (_, _) => { if (syncing) return; while (layout.Slots.Count < count.Value) layout.Slots.Add(DefaultSlot(layout, layout.Slots.Count)); while (layout.Slots.Count > count.Value) layout.Slots.RemoveAt(layout.Slots.Count - 1); BindSlots(Math.Min(canvas.Selected, layout.Slots.Count - 1)); SaveSoon(); };
        // Typed numbers commit when you click elsewhere or switch apps, not only on Enter.
        canvas.MouseDown += (_, _) => CommitTypedNumbers(); slots.MouseDown += (_, _) => CommitTypedNumbers(); Deactivate += (_, _) => CommitTypedNumbers();
        slots.SelectedIndexChanged += (_, _) => { if (!syncing && slots.SelectedIndex >= 0) { canvas.Selected = slots.SelectedIndex; UpdateSlotDetails(); canvas.Invalidate(); } };
        var clientContext = new ContextMenuStrip { Name = "ClientContext", Renderer = new WorkspaceMenuRenderer(), ShowImageMargin = false, Font = new("Segoe UI", 10f) };
        var editItem = new ToolStripMenuItem("Edit client…") { Padding = new(8, 4, 8, 4) }; editItem.Click += async (_, _) => await ConfigureClientLogin();
        var deleteItem = new ToolStripMenuItem("Delete client") { Name = "DeleteClientMenu", Padding = new(8, 4, 8, 4), ForeColor = Color.FromArgb(255, 128, 118) };
        deleteItem.Click += (_, _) => { if (CurrentSlot is { } s) DeleteClient(s); };
        clientContext.Items.AddRange(new ToolStripItem[] { editItem, new ToolStripSeparator(), deleteItem });
        clientContext.Opening += (_, e) => { e.Cancel = busy || CurrentSlot is null; deleteItem.Enabled = layout.Slots.Count > 1; };
        slots.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            int index = slots.IndexFromPoint(e.Location); if (index < 0) return;
            slots.SelectedIndex = index; clientContext.Show(slots, e.Location);
        };
        canvas.ClientContextRequested += location => clientContext.Show(canvas, location);
        slots.DoubleClick += async (_, _) => await ConfigureClientLogin(); edit.Click += async (_, _) => await ConfigureClientLogin();
        canvas.SelectionChanged += () => { slots.SelectedIndex = canvas.Selected; UpdateSlotDetails(); };
        canvas.LayoutEdited += () => { UpdateSlotDetails(); UpdateApplyButton(); SaveSoon(); };
        windowMode.SelectedIndexChanged += (_, _) =>
        {
            if (syncing || windowMode.SelectedIndex < 0 || CurrentSlot is not { } slot || targetMonitor.SelectedIndex < 0) return;
            ClientPlacement.ChangeMode(slot, monitors[targetMonitor.SelectedIndex], (ClientWindowMode)windowMode.SelectedIndex);
            UpdateSlotDetails(); canvas.Invalidate(); SaveSoon();
        };
        layer.ValueChanged += (_, _) => { if (!syncing && CurrentSlot is { } s) { s.Layer = (int)layer.Value; canvas.Invalidate(); RefreshClients(); SaveSoon(); } };
        foreach (var input in new[] { posX, posY, sizeW, sizeH }) input.ValueChanged += (_, _) => { if (!syncing && CurrentSlot is { } s) { s.SetBounds(new((int)posX.Value, (int)posY.Value, (int)sizeW.Value, (int)sizeH.Value)); canvas.Invalidate(); UpdateApplyButton(); SaveSoon(); } };
        start.ShowArrow = true; start.ArrowClick += () => { start.Focus(); clientMenu.Show(start, new Point(0, -4), ToolStripDropDownDirection.AboveRight); };
        clientMenu.MinimumSize = new(start.Width, 0); start.Resize += (_, _) => clientMenu.MinimumSize = new(start.Width, 0);
        closeClients.Click += async (_, _) => await CloseClients(false); restartClients.Click += async (_, _) => await CloseClients(true);
        clientMenu.Items.AddRange(new ToolStripItem[] { closeClients, restartClients });
        clientMenu.Opening += (_, _) => { bool any = canvas.Assigned.Count > 0; closeClients.Enabled = restartClients.Enabled = any && !busy; };
        start.Click += async (_, _) => await RunLayout(true); apply.Click += async (_, _) => await RunLayout(false); cancel.Click += (_, _) => launchCancellation?.Cancel();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveNow(); }; refresh.Tick += (_, _) => { RefreshDisplayGeometry(); RefreshClients(); };
        HookHitTesting(this);
    }
    private readonly ToolStripMenuItem activityLog = new("Activity log") { CheckOnClick = true };
    private void BuildTitleBar()
    {
        var layoutMenu = new ToolStripMenuItem("LAYOUT  ▾");
        layoutMenu.DropDownItems.Add("New…", null, (_, _) => SaveAs(true));
        layoutMenu.DropDownItems.Add("Save a copy…", null, (_, _) => SaveAs(false));
        layoutMenu.DropDownItems.Add(new ToolStripSeparator());
        layoutMenu.DropDownItems.Add("Capture open clients", null, (_, _) => CaptureWindows());
        layoutMenu.DropDownItems.Add("Direct-launch defaults…", null, (_, _) => EditDirectDefaults());
        layoutMenu.DropDownItems.Add(new ToolStripSeparator()); layoutMenu.DropDownItems.Add("Exit", null, (_, _) => Close());
        sharedSettingsMenu.DropDownItems.Add("WoW folders…", null, async (_, _) => await OpenSharedSettings(async () =>
        {
            await RefreshWoWLibrary(); using var dialog = new WoWFoldersForm(settings.WoWLibrary);
            if (dialog.ShowDialog(this) == DialogResult.OK) SaveNow();
        }));
        sharedSettingsMenu.DropDownItems.Add("Battle.net connections…", null, async (_, _) => await OpenSharedSettings(() =>
        {
            using var dialog = new BattleNetConnectionsForm(settings, SaveNow, ""); dialog.ShowDialog(this); return Task.CompletedTask;
        }));
        sharedSettingsMenu.DropDownItems.Add(new ToolStripSeparator());
        sharedSettingsMenu.DropDownItems.Add("Hover focus…", null, (_, _) => ShowHoverPreferences());
        var trayMinimize = new ToolStripMenuItem("Minimize to notification area") { Name = "MinimizeToTray", CheckOnClick = true, Checked = settings.MinimizeToTray, ToolTipText = "On: minimizing hides HoverDeck next to the clock. Off: it minimizes to the taskbar like a normal window." };
        trayMinimize.CheckedChanged += (_, _) => { settings.MinimizeToTray = trayMinimize.Checked; SaveSoon(); };
        sharedSettingsMenu.DropDownItems.Add(trayMinimize);
        sharedSettingsMenu.DropDownItems.Add(new ToolStripSeparator());
        sharedSettingsMenu.DropDownItems.Add("Check for updates…", null, async (_, _) => await CheckForUpdates(true));
        var viewMenu = new ToolStripMenuItem("VIEW  ▾"); viewMenu.DropDownItems.Add(activityLog);
        mainMenu.Items.AddRange(new ToolStripItem[] { layoutMenu, sharedSettingsMenu, viewMenu });
        foreach (ToolStripMenuItem item in mainMenu.Items) { item.Font = new("Segoe UI", 9f, FontStyle.Bold); item.Padding = new(8, 0, 8, 0); item.Margin = new(0, 0, 12, 0); }
        mainMenu.Renderer = new WorkspaceMenuRenderer(); MainMenuStrip = mainMenu;
        // Hovered or open menus get the launcher's blue underline along the bar's bottom edge.
        mainMenu.Paint += (_, e) =>
        {
            foreach (ToolStripItem item in mainMenu.Items)
                if (item.Selected || item.Pressed) { using var line = new SolidBrush(AppTheme.Blue); e.Graphics.FillRectangle(line, item.Bounds.X, mainMenu.Height - 3, item.Bounds.Width, 2); }
        };
        titleBar.Paint += (_, e) => { using var line = new Pen(AppTheme.Line); e.Graphics.DrawLine(line, 0, titleBar.Height - 1, titleBar.Width, titleBar.Height - 1); };
        minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        maximizeButton.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        closeButton.Click += (_, _) => Close();
        toggle.AccessibleName = "Toggle WoW hover focus"; toggle.Height = 30; toggle.Margin = new(0, (TitleBarHeight - 30) / 2, 14, 0);
        openCount.Margin = new(0, (TitleBarHeight - 15) / 2, 16, 0); openCount.BackColor = AppTheme.Bar; openCount.Font = new("Segoe UI", 9f);
        var windowButtons = new Panel { Size = new(139, TitleBarHeight), Margin = new(0), BackColor = AppTheme.Bar };
        closeButton.Dock = maximizeButton.Dock = minimizeButton.Dock = DockStyle.Left;
        windowButtons.Controls.AddRange(new Control[] { closeButton, maximizeButton, minimizeButton });
        windowButtons.Paint += (_, e) => { using var line = new Pen(AppTheme.Line); e.Graphics.DrawLine(line, 0, 0, 0, windowButtons.Height); };
        titleRight.Controls.AddRange(new Control[] { windowButtons, toggle, openCount });
        titleBar.Controls.Add(mainMenu); titleBar.Controls.Add(logo); titleBar.Controls.Add(titleRight);
    }
    private Control Stepper()
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        Button Step(string text, int change)
        {
            var b = Button(text); b.Size = new(24, 24); b.Margin = Padding.Empty; b.Padding = Padding.Empty; b.Font = new("Segoe UI", 9f, FontStyle.Bold); b.AccessibleName = change > 0 ? "Add a client" : "Remove a client";
            b.Click += (_, _) => count.Value = Math.Clamp(count.Value + change, count.Minimum, count.Maximum); return b;
        }
        count.TextAlign = HorizontalAlignment.Center; count.AccessibleName = "Number of clients";
        var box = new FieldBox(count, 36) { Height = 24, Padding = new(2, 4, 2, 0) };
        row.Controls.AddRange(new Control[] { Step("−", -1), box, Step("+", 1) });
        return row;
    }
    private static Control FieldGroup(string caption, Control field)
    {
        var group = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new(0, 0, 14, 0) };
        field.Margin = new(0, 4, 0, 0); group.Controls.Add(new CapsLabel(caption) { Tracking = 1.1f, Margin = Padding.Empty }); group.Controls.Add(field);
        return group;
    }
    private void PaintHeaderGlow(object? sender, PaintEventArgs e)
    {
        if (sender is not Control c || c.Width < 10) return;
        // A soft blue glow behind the layout name, like a launcher game page.
        using var path = new GraphicsPath(); var ellipse = new RectangleF(-c.Width * .15f, -230, c.Width * .9f, 400); path.AddEllipse(ellipse);
        using var glow = new PathGradientBrush(path) { CenterColor = Color.FromArgb(18, 50, 88), SurroundColors = new[] { Background }, CenterPoint = new PointF(c.Width * .3f, -40) };
        e.Graphics.SetClip(new Rectangle(0, 0, c.Width, 170)); e.Graphics.FillPath(glow, path); e.Graphics.ResetClip();
    }
    private int openSignature = -1;
    private void DrawClientItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= slots.Items.Count) return;
        var g = e.Graphics; var r = e.Bounds; var slot = (Slot)slots.Items[e.Index]; bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(AppTheme.Rail)) g.FillRectangle(back, r);
        if (selected)
        {
            using var wash = new LinearGradientBrush(r, AppTheme.Selected, AppTheme.Rail, 0f); g.FillRectangle(wash, r);
            using var bar = new SolidBrush(AppTheme.Blue); g.FillRectangle(bar, r.X, r.Y + 6, 3, r.Height - 12);
        }
        var color = LayoutCanvas.SlotColors[e.Index % LayoutCanvas.SlotColors.Length];
        AppTheme.DrawTile(g, new Rectangle(r.X + 18, r.Y + 10, 42, 42), color, $"{e.Index + 1:00}", TileNumber);
        bool open = canvas.Assigned.ContainsKey(slot.Id); string state = open ? "OPEN" : "PLANNED";
        var stateSize = AppTheme.MeasureTracked(state, CardState, .8f);
        AppTheme.DrawTracked(g, state, CardState, open ? AppTheme.Online : Color.FromArgb(92, 99, 112), new Rectangle(r.Right - 18 - stateSize.Width, r.Y, stateSize.Width + 2, r.Height), .8f);
        int textWidth = r.Width - 72 - stateSize.Width - 30;
        TextRenderer.DrawText(g, slot.Name, CardName, new Rectangle(r.X + 72, r.Y + 11, textWidth, 22), Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        string subtitle = slot.LaunchMethod == ClientLaunchMethod.BattleNet ? slot.BattleNet?.AccountLabel ?? "Battle.net" : "Direct / private server";
        TextRenderer.DrawText(g, subtitle, CardDetail, new Rectangle(r.X + 72, r.Y + 33, textWidth, 18), Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
    private readonly ContextMenuStrip clientMenu = new() { Name = "ClientActions", Renderer = new WorkspaceMenuRenderer(), ShowImageMargin = false, Font = new("Segoe UI", 10f) };
    private readonly ToolStripMenuItem closeClients = new("Close clients") { Name = "CloseClients", Padding = new(8, 6, 8, 6), ToolTipText = "Close the open clients of this layout, like clicking each window's X." },
        restartClients = new("Restart clients") { Name = "RestartClients", Padding = new(8, 6, 8, 6), ToolTipText = "Close the open clients, then start the layout again." };
    private async Task CloseClients(bool restart)
    {
        if (busy || displayApplying || arrangeDisplays.Checked) return;
        var open = launcher.Assign(layout, launcher.Scan(layout));
        var names = layout.Slots.Where(s => open.ContainsKey(s.Id)).Select(s => s.Name).ToList();
        if (names.Count == 0) { Log("No open clients to close."); return; }
        string action = restart ? "Restart" : "Close", list = string.Join(", ", names);
        if (MessageBox.Show(this, $"{action} {names.Count} open client{(names.Count == 1 ? "" : "s")}?\n\n{list}\n\nEach game closes as if you clicked its window's X.", $"{action} clients", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        busy = true; launchCancellation = new(); hover?.Suspend(true); SetWorkspaceEnabled(false); start.Visible = apply.Visible = false; cancel.Visible = cancel.Enabled = true; mainMenu.Enabled = false;
        SetStatus(restart ? "Restarting · closing clients…" : "Closing clients…");
        List<Slot> stillOpen = new(); bool cancelled = false;
        try { stillOpen = await launcher.Close(layout, Log, launchCancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; Log("Stopped waiting for clients to close."); }
        finally { busy = false; SetWorkspaceEnabled(true); start.Visible = true; cancel.Visible = cancel.Enabled = false; mainMenu.Enabled = true; SetStatus(""); hover?.Suspend(false); launchCancellation.Dispose(); launchCancellation = null; RefreshClients(); }
        if (stillOpen.Count > 0)
        {
            string left = string.Join(", ", stillOpen.Select(s => s.Name));
            Log($"Still running after 30 seconds: {left}.");
            MessageBox.Show(this, $"{left} did not close within 30 seconds. The game may be waiting for you (for example a logout timer or a prompt). Close it yourself{(restart ? ", then press Start clients" : "")}.", $"{action} clients", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (restart && !cancelled) await RunLayout(true);
    }
    // Reading NumericUpDown.Value parses any pending typed text and raises ValueChanged.
    private void CommitTypedNumbers() { foreach (var number in new[] { layer, posX, posY, sizeW, sizeH, count }) _ = number.Value; }
    private void UpdateApplyButton()
    {
        int moved = layout.Slots.Count(s => canvas.Assigned.TryGetValue(s.Id, out var w) && !w.Minimized && w.Bounds != s.Bounds);
        apply.Visible = moved > 0 && !busy && !displayApplying && !arrangeDisplays.Checked;
    }
    private void SetStatus(string text)
    {
        status.Text = text; sidebar.RowStyles[4].Height = text.Length == 0 ? 0 : 44;
    }
    // While clients launch, everything except the progress text and Cancel is locked.
    private void SetWorkspaceEnabled(bool enabled)
    {
        workspaceContent.Enabled = enabled;
        foreach (Control c in sidebar.Controls) if (c != launchArea && c != status) c.Enabled = enabled;
    }
    private async Task RefreshWoWLibrary()
    {
        var library = settings.WoWLibrary;
        var roots = WoWInstallationScanner.LibraryRoots(library, settings.Layouts.SelectMany(l => l.Slots).Where(s => s.LaunchMethod == ClientLaunchMethod.BattleNet).Select(s => s.BattleNet?.Executable ?? ""));
        var discovery = await Task.Run(() => WoWInstallationScanner.Scan(roots));
        library.Folders = roots; library.Installations = discovery.Installations; library.RootFoldersInitialized = true; SaveNow();
    }
    private async Task OpenSharedSettings(Func<Task> action)
    {
        if (busy || displayApplying || arrangeDisplays.Checked) return;
        busy = true; mainMenu.Enabled = mainArea.Enabled = start.Enabled = false; hover?.Suspend(true);
        try { await action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Settings"); }
        finally { busy = false; mainMenu.Enabled = mainArea.Enabled = start.Enabled = true; hover?.Suspend(false); UpdateSlotDetails(); RefreshClients(); }
    }
    private void ShowHoverPreferences()
    {
        if (busy || displayApplying) return;
        using var dialog = new Form { Text = "Hover focus settings", ClientSize = new(520, 310), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, BackColor = Surface, ForeColor = Ink, Font = Font };
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        body.Controls.Add(new Label { Text = "Focus follows the mouse over your configured WoW clients.\nUse the main toggle or your shortcut to turn it on and off.", Width = 475, Height = 55 });
        var timing = new FlowLayoutPanel { Width = 475, Height = 40 }; timing.Controls.Add(Label("Delay (milliseconds)")); timing.Controls.Add(delay); body.Controls.Add(timing);
        var shortcut = new FlowLayoutPanel { Width = 475, Height = 40 }; shortcut.Controls.Add(Label("Toggle shortcut")); hotkey.Width = 210; shortcut.Controls.Add(hotkey); body.Controls.Add(shortcut);
        body.Controls.Add(new Label { Text = "Changes save immediately. Your previous Windows hover settings return when HoverDeck exits.", Width = 475, Height = 42, ForeColor = Muted, Font = new("Segoe UI", 9) });
        var done = Button("Done"); done.DialogResult = DialogResult.OK; body.Controls.Add(done); dialog.Controls.Add(body); Theme(dialog);
        dialog.ShowDialog(this); timing.Controls.Remove(delay); shortcut.Controls.Remove(hotkey);
    }
    private void EditDirectDefaults()
    {
        if (busy || displayApplying || arrangeDisplays.Checked) return;
        using var dialog = new Form { Text = "Direct-launch defaults — " + layout.Name, ClientSize = new(640, 290), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, BackColor = Surface, ForeColor = Ink, Font = Font };
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        body.Controls.Add(new Label { Text = "Used by direct/private-server clients that inherit this layout's default.\nBattle.net versions are configured in Settings → WoW folders.", Width = 595, Height = 52 });
        body.Controls.Add(Label("Default game executable")); var row = new FlowLayoutPanel { Width = 600, Height = 38 };
        var path = new TextBox { Text = layout.Executable, Width = 475 }; var browse = Button("Browse…"); browse.Width = 105; row.Controls.AddRange(new Control[] { path, browse }); body.Controls.Add(row);
        browse.Click += (_, _) => { var chosen = PickExe(path.Text); if (chosen is not null) path.Text = chosen; };
        body.Controls.Add(Label("Launch arguments (all direct clients in this layout)")); var args = new TextBox { Text = layout.Arguments, Width = 590 }; body.Controls.Add(args);
        var actions = new FlowLayoutPanel { Width = 595, Height = 42, Margin = new(0, 15, 0, 0) }; var save = Button("Save"); var cancelEdit = Button("Cancel"); cancelEdit.DialogResult = DialogResult.Cancel; actions.Controls.AddRange(new Control[] { save, cancelEdit }); body.Controls.Add(actions);
        save.Click += (_, _) => { if (path.Text.Trim().Length > 0 && !File.Exists(path.Text.Trim().Trim('"'))) { MessageBox.Show(dialog, "Choose an existing game executable."); return; } dialog.DialogResult = DialogResult.OK; };
        dialog.Controls.Add(body); dialog.AcceptButton = save; dialog.CancelButton = cancelEdit; Theme(dialog);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        layout.Executable = path.Text.Trim().Trim('"'); layout.Arguments = args.Text; launcher.Bindings.Clear(); SaveNow(); RefreshClients(); UpdateSlotDetails();
    }
    private sealed class WorkspaceMenuRenderer : ToolStripProfessionalRenderer
    {
        public WorkspaceMenuRenderer() : base(new WorkspaceColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            bool top = !e.Item.IsOnDropDown;
            e.TextColor = !e.Item.Enabled ? AppTheme.Caption : top && !(e.Item.Selected || e.Item.Pressed) ? Color.FromArgb(154, 161, 176) : top ? Color.White : Ink;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) { if (e.Item.IsOnDropDown) base.OnRenderMenuItemBackground(e); }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { if (e.ToolStrip is MenuStrip) { using var b = new SolidBrush(AppTheme.Bar); e.Graphics.FillRectangle(b, e.AffectedBounds); } else base.OnRenderToolStripBackground(e); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { if (e.ToolStrip is not MenuStrip) base.OnRenderToolStripBorder(e); }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = AppTheme.Muted; base.OnRenderArrow(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = new Rectangle(e.ImageRectangle.X + 2, e.ImageRectangle.Y + 2, e.ImageRectangle.Width - 4, e.ImageRectangle.Height - 4);
            using var fill = new SolidBrush(AppTheme.Blue); e.Graphics.FillRectangle(fill, r);
            TextRenderer.DrawText(e.Graphics, "✓", e.Item.Font, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
    private sealed class WorkspaceColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuItemSelected => AppTheme.Selected;
        public override Color MenuItemBorder => AppTheme.Selected;
        public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
        public override Color MenuItemPressedGradientBegin => Surface;
        public override Color MenuItemPressedGradientEnd => Surface;
        public override Color MenuStripGradientBegin => AppTheme.Bar;
        public override Color MenuStripGradientEnd => AppTheme.Bar;
        public override Color MenuBorder => AppTheme.ButtonBorder;
        public override Color SeparatorDark => AppTheme.Line;
        public override Color SeparatorLight => Surface;
    }
}
