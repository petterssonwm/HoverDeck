namespace HoverDeck;

public sealed partial class MainForm
{
    private readonly NavLink arrangeDisplays = new("Arrange displays") { Name = "ArrangeDisplays" };
    private readonly Button applyDisplayChanges = Button("Apply to Windows", true), resetDisplayChanges = Button("Reset");
    private readonly Button fitDisplayView = Button("Fit view");
    private readonly Label displayModeStatus = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted };
    private readonly DarkComboBox editMonitor = new() { Width = 150 };
    private readonly NumericUpDown displayX = Number(-64000, 64000), displayY = Number(-64000, 64000);
    private readonly CheckBox primaryDisplay = new() { Text = "Primary display", AutoSize = true, ForeColor = Ink, Margin = new(16, 5, 0, 0) };
    private readonly Label displaySize = new() { AutoSize = true, ForeColor = Muted, Margin = new(16, 7, 0, 0) };
    private readonly Label displayValidation = new() { Dock = DockStyle.Fill, ForeColor = Muted, AutoEllipsis = true };
    private Control? clientSidebar, clientDetails, displayDetails;
    private List<MonitorInfo> displayBaseline = new();
    private bool displayUiSync, displayApplying;

    private void ConfigureDisplayEditor(TableLayoutPanel editor, Control sidebar, Control details)
    {
        clientSidebar = sidebar; clientDetails = details;
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new(0, 0, 0, 8) };
        toolbar.ColumnStyles.Add(new(SizeType.Absolute, 98)); toolbar.ColumnStyles.Add(new(SizeType.Percent, 100)); toolbar.ColumnStyles.Add(new(SizeType.Absolute, 158)); toolbar.ColumnStyles.Add(new(SizeType.Absolute, 82));
        fitDisplayView.Dock = applyDisplayChanges.Dock = resetDisplayChanges.Dock = DockStyle.Fill; displayModeStatus.Margin = new(10, 0, 0, 0);
        toolbar.Controls.Add(fitDisplayView, 0, 0); toolbar.Controls.Add(displayModeStatus, 1, 0); toolbar.Controls.Add(applyDisplayChanges, 2, 0); toolbar.Controls.Add(resetDisplayChanges, 3, 0);
        arrangeDisplays.AccessibleName = "Arrange Windows displays";
        applyDisplayChanges.Visible = resetDisplayChanges.Visible = fitDisplayView.Visible = false;
        displayModeStatus.Text = "Editing client positions";
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new(16, 12, 16, 10), ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        panel.Paint += (_, e) => { using var edge = new Pen(AppTheme.Line); e.Graphics.DrawRectangle(edge, 0, 0, panel.Width - 1, panel.Height - 1); };
        panel.RowStyles.Add(new(SizeType.Absolute, 38)); panel.RowStyles.Add(new(SizeType.Absolute, 38)); panel.RowStyles.Add(new(SizeType.Percent, 100));
        var selected = Flow(); selected.Controls.Add(Label("Selected display")); selected.Controls.Add(editMonitor); selected.Controls.Add(primaryDisplay); panel.Controls.Add(selected, 0, 0);
        var position = Flow(); position.Controls.Add(Label("X")); displayX.AccessibleName = "Display X"; position.Controls.Add(new FieldBox(displayX, 110)); position.Controls.Add(Label("Y", 12)); displayY.AccessibleName = "Display Y"; position.Controls.Add(new FieldBox(displayY, 110)); position.Controls.Add(displaySize); panel.Controls.Add(position, 0, 1);
        panel.Controls.Add(displayValidation, 0, 2);
        displayDetails = panel; panel.Visible = false;
        var host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new(0, 12, 0, 0) }; host.Controls.Add(details); host.Controls.Add(panel);
        editor.Controls.Clear(); editor.RowStyles.Clear(); editor.RowCount = 3;
        editor.RowStyles.Add(new(SizeType.Absolute, 0)); editor.RowStyles.Add(new(SizeType.Percent, 100)); editor.RowStyles.Add(new(SizeType.Absolute, ClientDetailsHeight)); toolbar.Visible = false;
        editor.Controls.Add(toolbar, 0, 0); editor.Controls.Add(canvas, 0, 1); editor.Controls.Add(host, 0, 2);
        arrangeDisplays.CheckedChanged += (_, _) =>
        {
            if (displayUiSync) return;
            bool editing = arrangeDisplays.Checked;
            canvas.DisplayEditMode = editing; sidebar.Visible = !editing; details.Visible = !editing; panel.Visible = editing;
            mainArea.ColumnStyles[0].Width = editing ? 0 : 270; clientsLink.Checked = !editing;
            toolbar.Visible = editing; editor.RowStyles[0].Height = editing ? 42 : 0; editor.RowStyles[2].Height = ClientDetailsHeight;
            sharedSettingsMenu.Enabled = !editing;
            if (mainMenu.Items[0] is ToolStripMenuItem layoutMenu) layoutMenu.Enabled = !editing;
            resetDisplayChanges.Visible = fitDisplayView.Visible = editing; applyDisplayChanges.Visible = editing && !preview; // preview mode never changes Windows settings
            start.Enabled = !editing && !busy;
            if (editing) { RefreshDisplayGeometry(); ResetDisplayDraft(); }
            else { canvas.Monitors = monitors; displayModeStatus.Text = "Editing client positions"; RefreshClients(); }
            canvas.Invalidate();
        };
        resetDisplayChanges.Click += (_, _) => { RefreshDisplayGeometry(); ResetDisplayDraft(); };
        fitDisplayView.Click += (_, _) => canvas.FitView();
        editMonitor.SelectedIndexChanged += (_, _) => { if (!displayUiSync && editMonitor.SelectedIndex >= 0) { canvas.SelectedMonitor = editMonitor.SelectedIndex; UpdateDisplayDetails(); canvas.Invalidate(); } };
        canvas.MonitorSelectionChanged += () => { UpdateDisplayDetails(); };
        canvas.MonitorsEdited += () => { UpdateDisplayDetails(); };
        foreach (var input in new[] { displayX, displayY }) input.ValueChanged += (_, _) =>
        {
            if (displayUiSync || !arrangeDisplays.Checked || canvas.SelectedMonitor < 0 || canvas.SelectedMonitor >= canvas.Monitors.Count) return;
            int i = canvas.SelectedMonitor; canvas.Monitors[i] = DisplayArrangement.Move(canvas.Monitors[i], new((int)displayX.Value, (int)displayY.Value));
            UpdateDisplayDetails(); canvas.Invalidate();
        };
        primaryDisplay.CheckedChanged += (_, _) =>
        {
            if (displayUiSync) return;
            // Windows always needs one primary display, so the current one cannot be unticked;
            // pick another display and tick it there instead.
            if (!primaryDisplay.Checked) { displayUiSync = true; primaryDisplay.Checked = true; displayUiSync = false; return; }
            for (int i = 0; i < canvas.Monitors.Count; i++) canvas.Monitors[i] = canvas.Monitors[i] with { Primary = i == canvas.SelectedMonitor };
            UpdateDisplayDetails(); canvas.Invalidate();
        };
        applyDisplayChanges.Click += async (_, _) => await ApplyDisplayArrangement();
    }
    private void IdentifyDisplays() { foreach (var (monitor, index) in Native.Monitors().Select((m, i) => (m, i))) new DisplayIdentifyForm(monitor, LayoutCanvas.DisplayNumber(monitor, index)).Show(this); }
    private void ResetDisplayDraft()
    {
        displayBaseline = monitors.ToList(); canvas.Monitors = monitors.ToList(); canvas.FitView();
        displayUiSync = true; editMonitor.Items.Clear();
        for (int i = 0; i < monitors.Count; i++) editMonitor.Items.Add("Display " + LayoutCanvas.DisplayNumber(monitors[i], i));
        canvas.SelectedMonitor = Math.Clamp(canvas.SelectedMonitor, 0, monitors.Count - 1);
        editMonitor.SelectedIndex = canvas.SelectedMonitor; displayUiSync = false;
        UpdateDisplayDetails(); canvas.Invalidate();
    }
    private void UpdateDisplayDetails()
    {
        if (!arrangeDisplays.Checked || canvas.Monitors.Count == 0) return;
        var m = canvas.Monitors[Math.Clamp(canvas.SelectedMonitor, 0, canvas.Monitors.Count - 1)];
        displayUiSync = true;
        editMonitor.SelectedIndex = canvas.SelectedMonitor;
        displayX.Value = Math.Clamp(m.Bounds.X, -64000, 64000); displayY.Value = Math.Clamp(m.Bounds.Y, -64000, 64000);
        primaryDisplay.Checked = m.Primary;
        displaySize.Text = $"{m.Bounds.Width} × {m.Bounds.Height} px";
        displayUiSync = false;
        var error = DisplayArrangement.Validate(canvas.Monitors);
        bool dirty = error is not null || !DisplayArrangement.SamePositions(DisplayArrangement.Normalize(canvas.Monitors), displayBaseline);
        applyDisplayChanges.Enabled = dirty && error is null && !displayApplying && !preview;
        resetDisplayChanges.Enabled = dirty && !displayApplying;
        displayModeStatus.Text = preview ? "Preview mode · changes here are not sent to Windows" : dirty ? "Changes not applied yet" : "Matches your current Windows arrangement";
        // Only problems are spelled out; the toolbar already says whether there are unapplied changes.
        displayValidation.Text = error ?? ""; displayValidation.ForeColor = Color.FromArgb(237, 185, 109);
        status.Text = "Arranging Windows displays";
    }
    private async Task ApplyDisplayArrangement()
    {
        if (preview || displayApplying || busy) return;
        var plan = DisplayArrangement.Normalize(canvas.Monitors);
        if (DisplayArrangement.Validate(plan) is { } error) { Log(error); return; }
        displayApplying = true; hover?.Suspend(true); mainMenu.Enabled = mainArea.Enabled = false; status.Text = "Applying Windows display positions…";
        try
        {
            using var session = await DisplayChangeSession.Begin(plan, displayBaseline);
            RefreshDisplayGeometry();
            var primary = Native.Monitors().First(m => m.Primary).WorkArea;
            Location = new(Math.Clamp(Left, primary.Left, Math.Max(primary.Left, primary.Right - Width)), Math.Clamp(Top, primary.Top, Math.Max(primary.Top, primary.Bottom - Height)));
            using var confirm = new DisplayKeepForm(session.Deadline, primary);
            bool keep = confirm.ShowDialog(this) == DialogResult.OK && session.Keep();
            if (!keep) session.Revert();
            Log(keep ? "Windows display arrangement kept." : "Original Windows display arrangement restored.");
        }
        catch (Exception ex) { Log("Display change failed: " + ex.Message); MessageBox.Show(this, ex.Message, "Display arrangement", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        finally
        {
            displayApplying = false; mainMenu.Enabled = mainArea.Enabled = true; hover?.Suspend(false);
            RefreshDisplayGeometry(); ResetDisplayDraft();
            start.Enabled = !arrangeDisplays.Checked;
        }
    }
}

internal sealed class DisplayKeepForm : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    public DisplayKeepForm(DateTimeOffset deadline, Rectangle screen)
    {
        Text = "Keep this display arrangement?"; ClientSize = new(470, 170); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.Manual; Location = new(screen.X + (screen.Width - Width) / 2, screen.Y + (screen.Height - Height) / 2); TopMost = true;
        Font = new("Segoe UI", 11); BackColor = AppTheme.Surface; ForeColor = AppTheme.Ink;
        var message = new Label { Bounds = new(22, 20, 425, 64) }; Controls.Add(message);
        var keep = new Button { Text = "Keep changes", Bounds = new(166, 112, 132, 36), DialogResult = DialogResult.OK }; AppTheme.Primary(keep);
        var revert = new Button { Text = "Revert", Bounds = new(312, 112, 132, 36), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = AppTheme.ButtonFace, ForeColor = AppTheme.Ink }; revert.FlatAppearance.BorderColor = AppTheme.ButtonBorder;
        Controls.AddRange(new Control[] { keep, revert }); AcceptButton = keep; CancelButton = revert;
        void Tick() { int remaining = Math.Max(0, (int)Math.Ceiling((deadline - DateTimeOffset.UtcNow).TotalSeconds)); message.Text = $"Keep this monitor arrangement?\nReverting automatically in {remaining} seconds."; if (remaining == 0) { DialogResult = DialogResult.Cancel; Close(); } }
        timer.Tick += (_, _) => Tick(); Tick(); timer.Start();
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { timer.Dispose(); base.OnFormClosed(e); }
}

internal sealed class DisplayIdentifyForm : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2500 };
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000080; return p; } }
    public DisplayIdentifyForm(MonitorInfo monitor, string number)
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; Size = new(170, 110); StartPosition = FormStartPosition.Manual;
        Location = new(monitor.WorkArea.X + (monitor.WorkArea.Width - Width) / 2, monitor.WorkArea.Y + (monitor.WorkArea.Height - Height) / 2);
        BackColor = AppTheme.Surface; ForeColor = AppTheme.Blue;
        Controls.Add(new Label { Text = number, Font = new("Segoe UI", 42, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
        timer.Tick += (_, _) => Close(); timer.Start();
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { timer.Dispose(); base.OnFormClosed(e); }
}
