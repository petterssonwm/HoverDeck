using System.Text.Json;

namespace HoverDeck;

public sealed partial class MainForm : Form
{
    private static readonly Color Background = AppTheme.Background, Surface = AppTheme.Surface, Ink = AppTheme.Ink, Muted = AppTheme.Muted, Accent = AppTheme.Blue;
    private readonly Settings settings;
    private Layout layout = new();
    private List<MonitorInfo> monitors;
    private readonly DesktopState desktopState;
    private readonly Launcher launcher = new();
    private HoverService? hover;
    private readonly bool preview;
    private readonly LayoutCanvas canvas = new() { Dock = DockStyle.Fill };
    private readonly DarkComboBox profiles = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown count = Number(1, 32);
    private readonly ListBox slots = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None };
    private readonly PillToggle toggle = new() { Text = "Hover focus off" };
    private readonly Label launchSummary = new();
    private readonly NumericUpDown posX = Number(-64000, 64000), posY = Number(-64000, 64000), sizeW = Number(160, 32000), sizeH = Number(120, 32000);
    private readonly DarkComboBox targetMonitor = new() { Name = "TargetMonitor", Width = 125 };
    private readonly DarkComboBox windowMode = new() { Name = "ClientWindowMode", Width = 235 };
    private readonly NumericUpDown layer = Number(1, 9);
    private const int ClientDetailsHeight = 132;
    private readonly Label status = new() { AutoSize = false, ForeColor = Muted, Font = new("Segoe UI", 9) };
    private readonly Label openCount = new() { AutoSize = true, ForeColor = Muted };
    private readonly TextBox logBox = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical };
    private readonly BlueButton start = new("Start clients");
    private readonly Button apply = Button("Apply positions");
    private readonly Button cancel = Button("Cancel");
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 1500 }, saveTimer = new() { Interval = 650 };
    private NotifyIcon tray = null!;
    private TableLayoutPanel mainArea = null!;
    private CancellationTokenSource? launchCancellation;
    private bool syncing, busy, hotkeyRegistered, closing;
    private FormWindowState restoreState = FormWindowState.Normal;
    private string? lastHoverError;
    private string? lastCoverageError;
    private bool displaysReady, displayRefreshQueued;

    public MainForm(bool preview, Settings? previewSettings = null)
    {
        this.preview = preview;
        Text = "HoverDeck — WoW workspace"; FormBorderStyle = FormBorderStyle.None; Size = new(1330, 930); MinimumSize = new(1160, 760); StartPosition = FormStartPosition.CenterScreen;
        Font = new("Segoe UI", 10); BackColor = Background; ForeColor = Ink; AutoScaleMode = AutoScaleMode.Dpi;
        Icon = AppTheme.AppIcon;
        settings = previewSettings ?? Storage.Load(); desktopState = new DesktopState(); monitors = desktopState.Monitors;
        if (settings.Layouts.Count == 0)
        {
            var open = Native.Windows(discover: true).OrderBy(w => w.Bounds.X).ThenBy(w => w.Bounds.Y).ToList();
            var initial = new Layout { Name = open.Count > 0 ? "Current WoW setup" : "My WoW layout", Executable = open.FirstOrDefault()?.Executable ?? "" };
            foreach (var w in open.Take(32)) { var s = new Slot { Name = $"Client {initial.Slots.Count + 1}", Executable = Matching.SamePath(w.Executable, initial.Executable) ? "" : w.Executable }; s.SetBounds(w.Bounds); initial.Slots.Add(s); launcher.Bindings[s.Id] = w.Identity; }
            if (initial.Slots.Count == 0) initial.Slots.Add(DefaultSlot(0));
            settings.Layouts.Add(initial); settings.ActiveLayout = initial.Name;
        }
        layout = settings.Layouts.FirstOrDefault(l => l.Name == settings.ActiveLayout) ?? settings.Layouts[0];
        BuildWorkspace();
        BindProfiles(); BindMonitors(); BindLayout();
        Shown += (_, _) =>
        {
            displaysReady = true;
            if (!preview)
            {
                try { hover = new HoverService(settings.DelayMs); RegisterShortcut(settings.HotkeyModifiers, settings.HotkeyKey); }
                catch (Exception ex) { toggle.Enabled = false; Log("Hover focus unavailable: " + ex.Message); }
            }
            else { toggle.Enabled = false; Log("Preview mode · hover settings are untouched."); }
            RefreshClients(); refresh.Start(); SaveNow();
            if (!preview) BeginInvoke(async () => { await Task.Delay(4000); await CheckForUpdates(false); });
            Log("Ready. Select a client rectangle to edit its position. Start reuses open clients and fills missing slots.");
            Log("Window mode controls whether Fit client leaves room for the taskbar or uses the full display.");
        };
    }

    private Slot? CurrentSlot => canvas.Selected >= 0 && canvas.Selected < layout.Slots.Count ? layout.Slots[canvas.Selected] : null;
    private static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max, Width = 95, Height = 30, BorderStyle = BorderStyle.FixedSingle, ThousandsSeparator = false };
    private static Label Label(string text, int margin = 0) => new() { Text = text, AutoSize = true, ForeColor = Muted, Margin = new(margin, 6, 7, 0) };
    private static FlowLayoutPanel Flow() => new() { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
    private static Button Button(string text, bool primary = false)
    {
        var button = new Button { Text = text, Height = 33, Width = 130, FlatStyle = FlatStyle.Flat, BackColor = AppTheme.ButtonFace, ForeColor = Ink, Cursor = Cursors.Hand, Margin = new(0, 0, 7, 0), UseVisualStyleBackColor = false, Font = new("Segoe UI", 9.5f, FontStyle.Bold) };
        button.FlatAppearance.MouseOverBackColor = AppTheme.ButtonHover; if (primary) AppTheme.Primary(button);
        return button;
    }
    private static void Theme(Control parent)
    {
        if (parent is Form dialog and not MainForm) DialogFrame.Apply(dialog);
        foreach (Control c in parent.Controls)
        {
            if (c is TextBox or NumericUpDown or ComboBox or ListBox) { c.BackColor = AppTheme.Field; c.ForeColor = Ink; }
            if (c is Button b) { b.FlatAppearance.BorderColor = b.BackColor == Accent ? Accent : AppTheme.ButtonBorder; b.FlatAppearance.BorderSize = 1; }
            Theme(c);
        }
    }
    private Slot DefaultSlot(int index)
    {
        var screen = monitors.FirstOrDefault(m => m.Primary) ?? monitors.First();
        var r = screen.WorkArea; var s = new Slot { Name = $"Client {index + 1}" };
        s.SetBounds(new(r.X + (index % 2) * r.Width / 2, r.Y + (index / 2 % 2) * r.Height / 2, Math.Max(320, r.Width / 2), Math.Max(240, r.Height / 2))); return s;
    }
    private void BindProfiles()
    {
        syncing = true; profiles.Items.Clear(); profiles.Items.AddRange(settings.Layouts.Cast<object>().ToArray()); profiles.SelectedItem = layout; syncing = false;
    }
    private void BindLayout()
    {
        syncing = true; count.Value = Math.Clamp(layout.Slots.Count, 1, 32); canvas.Layout = layout; layoutTitle.Text = layout.Name; syncing = false;
        BindSlots(0); RefreshClients();
    }
    private void BindSlots(int selected)
    {
        syncing = true; slots.Items.Clear(); slots.Items.AddRange(layout.Slots.Cast<object>().ToArray()); canvas.Selected = Math.Clamp(selected, 0, Math.Max(0, layout.Slots.Count - 1)); if (slots.Items.Count > 0) slots.SelectedIndex = canvas.Selected; syncing = false; UpdateSlotDetails(); canvas.Invalidate();
    }
    private void BindMonitors()
    {
        targetMonitor.Items.Clear(); for (int i = 0; i < monitors.Count; i++) targetMonitor.Items.Add("Display " + LayoutCanvas.DisplayNumber(monitors[i], i)); if (targetMonitor.Items.Count > 0) targetMonitor.SelectedIndex = 0;
    }
    private void RefreshDisplayGeometry()
    {
        if (closing) return;
        try
        {
            if (!desktopState.Refresh()) return;
            string? selectedDevice = targetMonitor.SelectedIndex >= 0 && targetMonitor.SelectedIndex < monitors.Count ? monitors[targetMonitor.SelectedIndex].Name : null;
            monitors = desktopState.Monitors;
            if (!displayApplying)
            {
                if (arrangeDisplays.Checked) ResetDisplayDraft();
                else canvas.Monitors = monitors;
            }
            BindMonitors();
            int index = monitors.FindIndex(m => m.Name == selectedDevice);
            if (index >= 0) targetMonitor.SelectedIndex = index;
            canvas.Invalidate();
            if (busy) { launchCancellation?.Cancel(); Log("Windows display arrangement changed. Placement stopped; review the updated map before starting again."); }
            else if (!displayApplying) Log("Display map updated from Windows: positions, sizes and taskbar areas refreshed.");
        }
        catch (Exception ex) { Log("Could not refresh display geometry: " + ex.Message); }
    }
    private void UpdateSlotDetails()
    {
        if (CurrentSlot is not { } s) return;
        syncing = true; selectedClientTitle.Text = s.Name; selectedTile.Invalidate();
        windowMode.SelectedIndex = (int)s.WindowMode; layer.Value = Math.Clamp(s.Layer, 1, 9);
        posX.Value = Math.Clamp(s.X, -64000, 64000); posY.Value = Math.Clamp(s.Y, -64000, 64000);
        sizeW.Value = Math.Clamp(s.Width, 160, 32000); sizeH.Value = Math.Clamp(s.Height, 120, 32000);
        launchSummary.Text = (s.LaunchMethod == ClientLaunchMethod.BattleNet ? "Battle.net · " + (s.BattleNet?.AccountLabel ?? "Choose an account") : "Direct / private server").ToUpperInvariant();
        hints.SetToolTip(launchSummary, s.LaunchMethod == ClientLaunchMethod.BattleNet ? $"Battle.net · {(s.BattleNet?.ConnectionName.Length > 0 ? s.BattleNet.ConnectionName + " · " : "")}{s.BattleNet?.AccountLabel} · {s.BattleNet?.GameVersion}" : "Direct / private server" + (s.Login is null ? " · no saved login" : " · saved username/password"));
        int index = monitors.FindIndex(m => m.Bounds.Contains(new Point(s.X + s.Width / 2, s.Y + s.Height / 2))); if (index >= 0) targetMonitor.SelectedIndex = index;
        syncing = false;
    }
    private void Tile(ClientFit fit)
    {
        if (CurrentSlot is not { } s || targetMonitor.SelectedIndex < 0) return;
        s.SetBounds(ClientPlacement.Fit(monitors[targetMonitor.SelectedIndex], s.WindowMode, fit)); UpdateSlotDetails(); canvas.Invalidate(); SaveSoon();
    }
    private string? PickExe(string current)
    {
        using var picker = new OpenFileDialog { Title = "Select the WoW game executable", Filter = "Windows executables (*.exe)|*.exe", CheckFileExists = true, RestoreDirectory = true };
        if (File.Exists(current)) picker.FileName = current;
        return picker.ShowDialog(this) == DialogResult.OK ? picker.FileName : null;
    }
    private void SaveAs(bool blank)
    {
        var name = Prompt("Layout name", blank ? "New WoW layout" : layout.Name + " copy")?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (settings.Layouts.Any(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { ThemedMessageBox.Show(this, "A layout with that name already exists. Choose a different name.", "Layout name"); return; }
        var next = blank ? new Layout { Executable = layout.Executable } : JsonSerializer.Deserialize<Layout>(JsonSerializer.Serialize(layout))!;
        next.Name = name; foreach (var slot in next.Slots) { slot.Id = Guid.NewGuid().ToString("N"); slot.BattleNet?.ClearBinding(); }
        if (next.Slots.Count == 0) next.Slots.Add(DefaultSlot(0));
        settings.Layouts.Add(next); layout = next; settings.ActiveLayout = name; BindProfiles(); BindLayout(); SaveNow();
    }
    private string? Prompt(string title, string value)
    {
        using var dialog = new Form { Text = title, ClientSize = new(410, 115), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Surface, ForeColor = Ink, Font = Font };
        var text = new TextBox { Text = value, Left = 16, Top = 16, Width = 378 }; var ok = Button("Save"); ok.Location = new(264, 61); ok.DialogResult = DialogResult.OK; dialog.Controls.AddRange(new Control[] { text, ok }); dialog.AcceptButton = ok; Theme(dialog);
        return dialog.ShowDialog(this) == DialogResult.OK ? text.Text : null;
    }
    private void CaptureWindows()
    {
        var open = Native.Windows(settings.Layouts.SelectMany(l => l.Slots.Select(s => s.PathFor(l))), true).OrderBy(w => w.Bounds.X).ThenBy(w => w.Bounds.Y).ToList();
        if (open.Count == 0) { Log("No open WoW client windows found. Open a game client first, or choose its executable."); return; }
        var existing = launcher.Assign(layout, open).ToDictionary(p => p.Value.Identity, p => layout.Slots.Single(s => s.Id == p.Key));
        var captured = new List<Slot>(); launcher.Bindings.Clear();
        if (string.IsNullOrWhiteSpace(layout.Executable)) layout.Executable = open[0].Executable;
        foreach (var w in open.Take(32)) { var s = existing.GetValueOrDefault(w.Identity) ?? new Slot { Name = $"Client {captured.Count + 1}", Executable = Matching.SamePath(w.Executable, layout.Executable) ? "" : w.Executable }; s.SetBounds(w.Bounds); captured.Add(s); launcher.Bindings[s.Id] = w.Identity; }
        layout.Slots = captured;
        BindLayout(); SaveNow(); Log($"Captured {layout.Slots.Count} open clients with their current positions and executable paths.");
    }
    private void RefreshClients()
    {
        if (closing) return;
        try
        {
            var windows = Native.Windows(settings.Layouts.SelectMany(l => l.Slots.Select(s => s.PathFor(l)))); hover?.UpdateWindows(windows);
            canvas.Assigned = launcher.Assign(layout, windows);
            var reserved = monitors.SelectMany(DesktopGeometry.ReservedAreas).Select(a => a.Bounds).ToArray();
            var fullscreen = layout.Slots.Where(s => s.WindowMode == ClientWindowMode.FullscreenWindowed && canvas.Assigned.ContainsKey(s.Id))
                .Select(s => canvas.Assigned[s.Id]).Where(w => !w.Minimized && reserved.Any(a => a.IntersectsWith(w.Bounds)));
            var layers = layout.Slots.Where(s => canvas.Assigned.ContainsKey(s.Id)).GroupBy(s => canvas.Assigned[s.Id].Handle).ToDictionary(g => g.Key, g => g.Max(s => s.Layer));
            var fullMonitor = canvas.Assigned.Values.Where(w => monitors.Any(m => m.Bounds == w.Bounds)).Select(w => w.Handle).ToHashSet();
            hover?.UpdateCoverage(canvas.Assigned.Values, fullscreen, layers, fullMonitor);
            openCount.Text = $"{canvas.Assigned.Count} of {layout.Slots.Count} clients open";
            // Repaint the map and the cards only when an open client appeared, closed, moved or was minimized.
            var signature = new HashCode();
            foreach (var s in layout.Slots) if (canvas.Assigned.TryGetValue(s.Id, out var w)) { signature.Add(s.Id); signature.Add(w.Handle); signature.Add(w.Bounds); signature.Add(w.Minimized); }
            if (signature.ToHashCode() is var current && current != openSignature) { openSignature = current; slots.Invalidate(); canvas.Invalidate(); }
            UpdateApplyButton();
            if (hover?.Error is { } err && lastHoverError != err) { lastHoverError = err; Log("Hover focus error: " + err); toggle.Checked = false; }
            string coverageStatus = "";
            if (hover?.CoverageError is { } coverageError)
            {
                if (lastCoverageError != coverageError) { lastCoverageError = coverageError; Log("Taskbar coverage: " + coverageError); }
                coverageStatus = "Taskbar coverage failed · see Settings → Activity log";
            }
            if (!busy && !displayApplying && !arrangeDisplays.Checked) SetStatus(coverageStatus);
        }
        catch (Exception ex) { Log("Refresh failed: " + ex.Message); }
    }
    private async Task RunLayout(bool launchMissing)
    {
        if (busy || displayApplying || arrangeDisplays.Checked) return;
        SaveNow(); busy = true; launchCancellation = new(); hover?.Suspend(true); SetWorkspaceEnabled(false); start.Visible = apply.Visible = false; cancel.Visible = cancel.Enabled = true; mainMenu.Enabled = false; SetStatus("Preparing clients…");
        try
        {
            if (launchMissing)
                foreach (var slot in layout.Slots.Where(s => s.LaunchMethod == ClientLaunchMethod.BattleNet && s.BattleNet is not null))
                    BattleNetConnectionSettings.ValidateSelection(settings, slot.BattleNet!);
            var opened = await launcher.Run(layout, monitors, launchMissing, Log, launchCancellation.Token, SaveNow);
            if (launchMissing)
                foreach (var client in opened)
                {
                    var slot = layout.Slots.Single(s => s.Id == client.SlotId);
                    if (slot.LaunchMethod != ClientLaunchMethod.Direct || !ClientLoginService.ShouldLogin(slot.Login, true)) continue;
                    var window = launcher.Scan(layout).FirstOrDefault(w => w.Identity == client.Identity);
                    if (window is null) { Log($"{slot.Name}: login skipped because its original window closed or changed."); continue; }
                    SetStatus("Logging in · Escape stops");
                    try { await ClientLoginService.Run(slot, window, true, Log, launchCancellation.Token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { Log($"{slot.Name}: login skipped. {ex.Message}"); }
                }
        }
        catch (OperationCanceledException) { Log("Stopped. Opened clients remain running; no further logins will be attempted."); }
        catch (Exception ex) { Log("Could not complete layout: " + ex.Message); ThemedMessageBox.Show(this, ex.Message, "Layout needs attention", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        finally { busy = false; SetWorkspaceEnabled(true); start.Visible = true; cancel.Visible = cancel.Enabled = false; mainMenu.Enabled = true; SetStatus(""); hover?.Suspend(false); launchCancellation.Dispose(); launchCancellation = null; RefreshClients(); }
    }
    private void ChooseHotkey()
    {
        using var dialog = new Form { Text = "Choose a global toggle shortcut", ClientSize = new(420, 150), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Surface, ForeColor = Ink, Font = Font, KeyPreview = true };
        var label = new Label { Text = "Press Ctrl / Alt / Shift plus a letter, number or function key.\nEscape cancels. The shortcut works while WoW is active.", AutoSize = false, Bounds = new(18, 20, 385, 65), ForeColor = Ink };
        dialog.Controls.Add(label); DialogFrame.Apply(dialog);
        dialog.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true; if (e.KeyCode == Keys.Escape) { dialog.Close(); return; }
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
            uint mods = (e.Alt ? 1u : 0) | (e.Control ? 2u : 0) | (e.Shift ? 4u : 0);
            if ((mods & 3) == 0) { label.Text = "Include Ctrl or Alt so the shortcut does not replace normal game keys."; return; }
            if (!RegisterShortcut(mods, (int)e.KeyCode)) { label.Text = "That shortcut is already in use. Try another combination."; return; }
            settings.HotkeyModifiers = mods; settings.HotkeyKey = (int)e.KeyCode; UpdateHotkeyLabel(); SaveSoon(); dialog.Close();
        };
        dialog.ShowDialog(this);
    }
    private string HotkeyText() => ((settings.HotkeyModifiers & 2) != 0 ? "Ctrl + " : "") + ((settings.HotkeyModifiers & 1) != 0 ? "Alt + " : "") + ((settings.HotkeyModifiers & 4) != 0 ? "Shift + " : "") + (Keys)settings.HotkeyKey;
    private bool RegisterShortcut(uint modifiers, int key)
    {
        if (preview) return true;
        if (hotkeyRegistered) Native.UnregisterHotKey(Handle, 1);
        if (Native.RegisterHotKey(Handle, 1, modifiers | 0x4000, (uint)key)) { hotkeyRegistered = true; return true; }
        hotkeyRegistered = Native.RegisterHotKey(Handle, 1, settings.HotkeyModifiers | 0x4000, (uint)settings.HotkeyKey);
        Log("The global hotkey is in use. Choose another in Settings → Hover focus."); return false;
    }
    private void UpdateHotkeyLabel() => hints.SetToolTip(toggle, "Focus follows the mouse over your WoW clients. Shortcut: " + HotkeyText());
    protected override void WndProc(ref Message m)
    {
        if (HandleFrameMessage(ref m)) return;
        if (m.Msg == 0x0312 && m.WParam == 1 && toggle.Enabled) toggle.Checked = !toggle.Checked;
        bool displaysChanged = m.Msg is 0x007E or 0x001A or 0x02E0; // display, work-area/settings, DPI
        base.WndProc(ref m);
        if (displaysChanged && displaysReady && !closing && !displayRefreshQueued)
        {
            displayRefreshQueued = true;
            BeginInvoke((Action)(() => { displayRefreshQueued = false; RefreshDisplayGeometry(); }));
        }
    }
    private void RestoreWindow() { Show(); WindowState = restoreState; Activate(); }
    private void SaveSoon() { saveTimer.Stop(); saveTimer.Start(); }
    private void SaveNow()
    {
        if (preview) return;
        try { Storage.Write(Storage.SettingsPath, settings, backup: true); }
        catch (Exception ex) { Log("Could not save settings: " + ex.Message); }
    }
    private void Log(string message) { logBox.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}"); logBox.SelectionStart = logBox.TextLength; logBox.ScrollToCaret(); }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (displayApplying) { e.Cancel = true; return; }
        if (busy) { launchCancellation?.Cancel(); e.Cancel = true; Log("Cancelling launch. Close again when placement stops."); return; }
        closing = true; refresh.Stop(); saveTimer.Stop(); SaveNow(); if (hotkeyRegistered) Native.UnregisterHotKey(Handle, 1); hover?.Dispose(); tray.Visible = false; tray.Dispose(); refresh.Dispose(); saveTimer.Dispose(); base.OnFormClosing(e);
    }
}

