using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace HoverDeck;

// Renders synthetic configuration and exercises WebView2 with a local page only.
internal sealed class LibraryUiTests : Form
{
    public LibraryUiTests(string output)
    {
        ShowInTaskbar = false; Opacity = 0;
        Shown += async (_, _) =>
        {
            Directory.CreateDirectory(output);
            var lines = new List<string>();
            try
            {
                var installation = new WoWInstallation { Product = "wow_classic_era", GameVersion = "World of Warcraft Classic", Build = "1.15.9.12345", Executable = @"D:\Games\World of Warcraft\_classic_era_\WowClassic.exe", RootFolder = @"D:\Games\World of Warcraft" };
                var connection = new BattleNetConnection { Name = "My main Battle.net login", Accounts = new() { new() { Id = "fixture-1", Name = "WoW1", Region = "EU" }, new() { Id = "fixture-2", Name = "WoW2", Region = "EU" } } };
                var settings = new Settings { WoWLibrary = new() { Folders = new() { @"D:\Games\World of Warcraft", @"E:\Another installation" }, Installations = new() { installation } }, BattleNetConnections = new() { connection, new() { Name = "My second Battle.net login" } } };
                var slot = new Slot { Name = "Client 1", LaunchMethod = ClientLaunchMethod.BattleNet, BattleNet = new() { Executable = installation.Executable, GameVersion = installation.GameVersion, GameTabId = installation.GameTabId, ConnectionId = connection.Id, WebAccountId = "fixture-2", AccountName = "WoW2 (EU)" } };
                void Render(Form form, string name)
                {
                    using (form)
                    {
                        form.ShowInTaskbar = false; form.Show(); form.Update(); Application.DoEvents();
                        using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        bitmap.Save(Path.Combine(output, name + ".png")); form.Close();
                    }
                    lines.Add("PASS: rendered " + name);
                }
                Render(new ClientLaunchForm(slot, new(), settings), "launch-settings");
                var advancedEditor = new ClientLaunchForm(slot, new(), settings); ((TabControl)advancedEditor.Controls.Find("ClientEditorTabs", true).Single()).SelectedIndex = 1; Render(advancedEditor, "advanced");
                Render(new ClientLaunchForm(new Slot { Name = "Private client" }, new Layout { Executable = @"D:\Private server\Wow.exe" }, settings), "direct-client");
                Render(new WoWFoldersForm(settings.WoWLibrary), "folders");
                // The setup guide: five steps; Clients defaults to Battle.net with the installed versions and the imported
                // accounts in order; only looking at it changes nothing; Finish writes each client's version and account.
                var guideLayout = new Layout { Name = "Guide", Slots = new() { new() { Name = "Client 1" }, new() { Name = "Client 2" } } };
                var warnings = new List<string>();
                using (var guide = new SetupGuideForm(settings, guideLayout, () => { }, () => "Ctrl + Alt + H", () => { }, _ => { }, _ => { }, message => { warnings.Add(message); return true; }))
                {
                    guide.ShowInTaskbar = false; guide.Show(); Application.DoEvents();
                    var guideNext = (Button)guide.Controls.Find("GuideNext", true).Single();
                    for (int step = 0; step < 5; step++)
                    {
                        guide.Refresh();
                        using (var shot = new Bitmap(guide.Width, guide.Height)) { guide.DrawToBitmap(shot, new Rectangle(Point.Empty, guide.Size)); shot.Save(Path.Combine(output, $"setup-step-{step + 1}.png")); }
                        if (step == 2)
                        {
                            if (guide.UsePrivateServer || guide.Clients.Count != 2 || guide.Clients.Any(c => c.Installation != installation)) throw new Exception("The guide's Clients step does not default to Battle.net with the installed version.");
                            if (guide.Clients[0].Account?.Id != "fixture-1" || guide.Clients[1].Account?.Id != "fixture-2") throw new Exception("The guide does not suggest the imported accounts in order.");
                            if (guide.ClientsChanged) throw new Exception("Only looking at the guide's Clients step counts as a change.");
                        }
                        guideNext.PerformClick(); Application.DoEvents();
                    }
                    if (guide.DialogResult != DialogResult.OK || warnings.Count != 0) throw new Exception("Finish did not complete the setup guide, or warned although everything was set up.");
                }
                lines.Add("PASS: the setup guide walks through five steps; Clients defaults to Battle.net with the found versions and imported accounts, and viewing it changes nothing");
                // Not signed in: Next on Battle.net warns; Clients cannot continue unfinished, only through Skip, which warns too.
                var unsigned = new Settings { WoWLibrary = settings.WoWLibrary };
                using (var guide = new SetupGuideForm(unsigned, new Layout { Slots = new() { new() } }, () => { }, () => "Ctrl + Alt + H", () => { }, _ => { }, _ => { }, message => { warnings.Add(message); return warnings.Count > 1; }))
                {
                    guide.ShowInTaskbar = false; guide.Show(); Application.DoEvents();
                    var guideNext = (Button)guide.Controls.Find("GuideNext", true).Single(); var title = Descendants(guide).OfType<Label>().First(l => l.Text.StartsWith("STEP "));
                    guideNext.PerformClick(); guideNext.PerformClick(); Application.DoEvents();
                    if (warnings.Count != 1 || !warnings[0].Contains("can't start Blizzard's WoW versions") || title.Text != "STEP 2 OF 5") throw new Exception("Next without signing in did not warn and stay, as answered.");
                    guideNext.PerformClick(); Application.DoEvents();
                    if (title.Text != "STEP 3 OF 5") throw new Exception("Continuing without signing in did not reach the Clients step.");
                    if (!guide.Controls.Find("GuideConnectClients", true).Single().Visible) throw new Exception("The Clients step offers no Battle.net sign-in when not signed in.");
                    guideNext.PerformClick(); Application.DoEvents();
                    if (title.Text != "STEP 3 OF 5") throw new Exception("Next left an unfinished Clients step.");
                    ((Button)guide.Controls.Find("GuideSkipClients", true).Single()).PerformClick(); Application.DoEvents();
                    if (title.Text != "STEP 4 OF 5" || !warnings[^1].Contains("Start clients can't open any WoW") || guide.ClientsChanged) throw new Exception("Skip for now did not warn and continue without applying clients.");
                    guide.Close();
                }
                lines.Add("PASS: without Battle.net the guide warns on Next, offers sign-in on the Clients step, blocks an unfinished Clients step and only skips it after a warning");
                SetupGuideForm.ApplyClients(guideLayout, false, "", 3, new[] { new ClientChoice(installation, connection, connection.Accounts[1]), new ClientChoice(installation, connection, connection.Accounts[0]), new ClientChoice(installation, null, null) }, i => new Slot { Name = $"Client {i + 1}" });
                if (guideLayout.Slots.Count != 3 || guideLayout.Slots.Any(s => s.LaunchMethod != ClientLaunchMethod.BattleNet) || guideLayout.Slots[0].BattleNet?.WebAccountId != "fixture-2"
                    || guideLayout.Slots[1].BattleNet?.AccountName != "WoW1 (EU)" || guideLayout.Slots[2].BattleNet?.GameVersion != installation.GameVersion || guideLayout.Slots[2].BattleNet?.AccountName != "")
                    throw new Exception("Finishing the guide did not give each client its chosen version and account.");
                SetupGuideForm.ApplyClients(guideLayout, true, @"D:\Private server\Wow.exe", 1, Array.Empty<ClientChoice>(), i => new Slot());
                if (guideLayout.Slots.Count != 1 || guideLayout.Slots[0].LaunchMethod != ClientLaunchMethod.Direct || guideLayout.Executable != @"D:\Private server\Wow.exe") throw new Exception("The guide's private-server choice was not applied.");
                lines.Add("PASS: finishing the guide sets each client's version and account, or one private-server game for all clients");
                // A new PC: Battle.net connected, but no WoW folders saved yet. Edit client must still list the installed versions.
                var newPc = new Settings { WoWLibrary = new() { RootFoldersInitialized = true }, BattleNetConnections = new() { connection } };
                using (var fresh = new MainForm(true, newPc))
                {
                    await (Task)typeof(MainForm).GetMethod("RefreshWoWLibrary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(fresh, null)!;
                    int expected = WoWInstallationScanner.Scan(WoWInstallationScanner.DetectedRoots()).Installations.Count;
                    if (newPc.WoWLibrary.Installations.Count != expected) throw new Exception("On a new PC, Edit client does not list the WoW versions installed on this PC.");
                    lines.Add($"PASS: on a new PC, Edit client lists the {expected} installed WoW version(s) found automatically");
                }
                using (var nothing = new ClientLaunchForm(new Slot { Name = "Client 1", LaunchMethod = ClientLaunchMethod.BattleNet }, new(), new Settings()))
                {
                    nothing.Show(); Application.DoEvents();
                    if (!Descendants(nothing).OfType<Label>().Any(l => l.Text.Contains("No installed World of Warcraft was found"))) throw new Exception("Edit client does not explain an empty version list.");
                    nothing.Close(); lines.Add("PASS: with no WoW installed, Edit client says where to add the WoW folder");
                }
                Render(new BattleNetConnectionsForm(settings, () => { }, connection.Id), "connections");
                var area = Native.Monitors().First(m => m.Primary).WorkArea;
                slot.SetBounds(new(area.X, area.Y, area.Width / 2, area.Height));
                var second = new Slot { Name = "Client 2", LaunchMethod = ClientLaunchMethod.BattleNet, BattleNet = new() { Executable = installation.Executable, GameVersion = installation.GameVersion, AccountName = "WoW1 (EU)" } }; second.SetBounds(new(area.X + area.Width / 2, area.Y, area.Width / 2, area.Height));
                settings.Layouts = new() { new() { Name = "Evening WoW", Slots = new() { slot, second } } }; settings.ActiveLayout = "Evening WoW";
                Render(new MainForm(true, settings), "workspace");
                Render(new MainForm(true, settings) { Size = new(1080, 760) }, "workspace-small");
                var bigSlot = new Slot { Name = "Main", LaunchMethod = ClientLaunchMethod.BattleNet, BattleNet = new() { AccountName = "WoW1 (EU)" }, Layer = 1 }; bigSlot.SetBounds(area);
                var smallSlot = new Slot { Name = "Healer", Layer = 2 }; smallSlot.SetBounds(new(area.Right - area.Width / 4, area.Bottom - area.Height / 3, area.Width / 4, area.Height / 3));
                var layeredSettings = new Settings { Layouts = new() { new() { Name = "Layered setup", Slots = new() { bigSlot, smallSlot } } }, ActiveLayout = "Layered setup" };
                using (var layeredForm = new MainForm(true, layeredSettings))
                {
                    var layerField = (NumericUpDown)layeredForm.Controls.Find("ClientLayer", true).Single();
                    if (layerField.Value != 1) throw new Exception("The Layer field does not show the selected client's layer.");
                    layeredForm.ShowInTaskbar = false; layeredForm.Show(); Application.DoEvents();
                    using var bitmap = new Bitmap(layeredForm.Width, layeredForm.Height); layeredForm.DrawToBitmap(bitmap, new Rectangle(Point.Empty, layeredForm.Size)); bitmap.Save(Path.Combine(output, "workspace-layers.png"));
                    layerField.Value = 3;
                    if (bigSlot.Layer != 3) throw new Exception("Changing the Layer field did not update the client.");
                    // Same layer, big client selected and drawn over the small one: the small client's name tag must still select it.
                    layerField.Value = 1; smallSlot.Layer = 1;
                    var map = Descendants(layeredForm).OfType<LayoutCanvas>().First(); map.Refresh();
                    var handles = (System.Collections.IList)typeof(LayoutCanvas).GetField("labelHandles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(map)!;
                    var smallTag = handles.Cast<(int Index, RectangleF Bounds)>().Single(h => h.Index == 1).Bounds;
                    var click = new MouseEventArgs(MouseButtons.Left, 1, (int)(smallTag.X + smallTag.Width / 2), (int)(smallTag.Y + smallTag.Height / 2), 0);
                    typeof(LayoutCanvas).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(map, new object[] { click });
                    typeof(LayoutCanvas).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(map, new object[] { click });
                    if (map.Selected != 1) throw new Exception("Clicking the name tag of a client hidden behind another did not select it.");
                    lines.Add("PASS: a client hidden behind a bigger one is selected through its name tag");
                    // A typed layer is kept when you click away (no Enter needed).
                    map.Selected = 0; map.Refresh(); typeof(MainForm).GetMethod("UpdateSlotDetails", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(layeredForm, null);
                    layerField.Focus(); layerField.Text = "4";
                    typeof(Control).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(map, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0) });
                    if (bigSlot.Layer != 4) throw new Exception("A typed layer was lost when clicking the map without pressing Enter.");
                    layerField.Text = "5"; typeof(Form).GetMethod("OnDeactivate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(layeredForm, new object[] { EventArgs.Empty });
                    if (bigSlot.Layer != 5) throw new Exception("A typed layer was lost when switching to another app.");
                    lines.Add("PASS: a typed layer is saved when clicking the map or switching apps, without pressing Enter");
                    map.Selected = 0; map.Refresh();
                    using (var hidden = new Bitmap(layeredForm.Width, layeredForm.Height)) { layeredForm.DrawToBitmap(hidden, new Rectangle(Point.Empty, layeredForm.Size)); hidden.Save(Path.Combine(output, "workspace-hidden-tag.png")); }
                    layeredForm.Close();
                }
                lines.Add("PASS: the Layer field shows and edits the selected client's layer; layered layout rendered");
                var trio = new Settings { Layouts = new() { new() { Name = "Three", Slots = new() { new() { Name = "A" }, new() { Name = "B" }, new() { Name = "C" } } } }, ActiveLayout = "Three" };
                using (var deleting = new MainForm(true, trio))
                {
                    deleting.ShowInTaskbar = false; deleting.Show(); Application.DoEvents();
                    var middle = trio.Layouts[0].Slots[1]; deleting.RemoveClient(middle);
                    var counter = Descendants(deleting).OfType<NumericUpDown>().First(n => n.AccessibleName == "Number of clients");
                    if (trio.Layouts[0].Slots.Select(s => s.Name).SequenceEqual(new[] { "A", "C" }) is false || counter.Value != 2) throw new Exception("Deleting the middle client did not remove exactly that client.");
                    deleting.RemoveClient(trio.Layouts[0].Slots[0]); deleting.RemoveClient(trio.Layouts[0].Slots[0]);
                    if (trio.Layouts[0].Slots.Count != 1) throw new Exception("The last client of a layout was deleted.");
                    deleting.Close();
                }
                using (var editor = new ClientLaunchForm(trio.Layouts[0].Slots[0], new Layout { Slots = new() { new(), new() } }, settings))
                {
                    var del = (Button)editor.Controls.Find("DeleteClient", true).Single(); editor.Show(); del.PerformClick();
                    if (editor.DialogResult != DialogResult.Abort) throw new Exception("Edit client's Delete button does not request deletion.");
                }
                lines.Add("PASS: deleting removes exactly the chosen client, keeps the last one, and Edit client has a Delete button");
                var modeEditor = new MainForm(true, settings) { Size = new(1080, 760) };
                var mode = (ComboBox)modeEditor.Controls.Find("ClientWindowMode", true).Single();
                mode.SelectedIndex = (int)ClientWindowMode.FullscreenWindowed;
                var monitor = Native.Monitors().First(m => m.Primary);
                if (slot.WindowMode != ClientWindowMode.FullscreenWindowed || slot.Bounds != ClientPlacement.Fit(monitor, ClientWindowMode.FullscreenWindowed, ClientFit.LeftHalf) || second.WindowMode != ClientWindowMode.Windowed)
                    throw new Exception("Mode selection did not refit only the chosen client's half-screen placement.");
                Render(modeEditor, "workspace-fullscreen-windowed");
                lines.Add("PASS: mode selector refits the selected client using full-display geometry and preserves other clients.");
                using (var idle = new MainForm(true, settings))
                {
                    idle.ShowInTaskbar = false; idle.Show(); Application.DoEvents();
                    if (idle.Controls.Find("ApplyPositions", true).Single().Visible) throw new Exception("Apply positions is shown although no open client differs from the map.");
                    lines.Add("PASS: Apply positions stays hidden while no open window differs from the map");
                    var actions = (ContextMenuStrip)typeof(MainForm).GetField("clientMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(idle)!;
                    if (!actions.Items.Cast<ToolStripItem>().Select(i => i.Text).SequenceEqual(new[] { "Close clients", "Restart clients" })) throw new Exception("The Start clients arrow menu does not offer Close clients and Restart clients.");
                    lines.Add("PASS: the Start clients arrow menu offers Close clients and Restart clients");
                    var startButton = Descendants(idle).OfType<BlueButton>().First(); idle.TopMost = true; idle.Activate();
                    actions.Show(startButton, new Point(0, -4), ToolStripDropDownDirection.AboveRight); Application.DoEvents(); Thread.Sleep(300); Application.DoEvents();
                    var menuArea = Rectangle.Union(startButton.RectangleToScreen(startButton.ClientRectangle), actions.Bounds); menuArea.Inflate(16, 16);
                    using (var menuShot = new Bitmap(menuArea.Width, menuArea.Height)) { using (var g = Graphics.FromImage(menuShot)) g.CopyFromScreen(menuArea.Location, Point.Empty, menuArea.Size); menuShot.Save(Path.Combine(output, "start-menu-open.png")); }
                    if (actions.Bounds.Bottom > startButton.RectangleToScreen(startButton.ClientRectangle).Top) throw new Exception("The Start clients menu does not open above the button.");
                    actions.Close(); lines.Add("PASS: the arrow menu opens above Start clients");
                    // The hover switch shares Start clients' frame, and the frame turns blue while it is on.
                    var hoverSwitch = Descendants(idle).OfType<HoverSwitch>().Single(); var dock = startButton.Parent!;
                    if (hoverSwitch.Parent != dock || Descendants(idle.Controls.Find("TitleBar", true).Single()).Contains(hoverSwitch) || hoverSwitch.Bottom > startButton.Top)
                        throw new Exception("The hover focus switch is not directly above Start clients in the same frame.");
                    void Shoot(string name) { dock.Refresh(); using var shot = new Bitmap(dock.Width + 24, dock.Height + 24); using (var g = Graphics.FromImage(shot)) g.Clear(AppTheme.Rail); dock.DrawToBitmap(shot, new Rectangle(12, 12, dock.Width, dock.Height)); shot.Save(Path.Combine(output, name)); }
                    hoverSwitch.Enabled = true; hoverSwitch.Checked = false; Shoot("hover-switch-off.png");
                    if (hoverSwitch.FrameColor == AppTheme.Blue || !hoverSwitch.Detail.Contains('+')) throw new Exception("The switch frame is blue while off, or the shortcut is missing.");
                    hoverSwitch.Checked = true; Shoot("hover-switch-on.png");
                    if (hoverSwitch.FrameColor != AppTheme.Blue || hoverSwitch.Text != "Hover focus on") throw new Exception("Turning hover focus on did not turn the frame blue.");
                    hoverSwitch.Checked = false;
                    lines.Add("PASS: the hover focus switch sits in Start clients' frame, shows the shortcut, and turns the frame blue when on");
                    // The tour after the guide: five bubbles in order, each pointing at the part it explains.
                    using (var toured = new MainForm(true, settings))
                    {
                        toured.ShowInTaskbar = false; toured.Show(); Application.DoEvents();
                        toured.StartTour(); Application.DoEvents();
                        var stops = new (string Title, Control Target, CoachSide Side)[]
                        {
                            ("Your displays and clients", Descendants(toured).OfType<LayoutCanvas>().First(), CoachSide.Inside),
                            ("Window mode", toured.Controls.Find("ClientWindowMode", true).Single(), CoachSide.Above),
                            ("Layer", toured.Controls.Find("ClientLayer", true).Single(), CoachSide.Above),
                            ("Edit client", toured.Controls.Find("EditClient", true).Single(), CoachSide.Above),
                            ("Hover focus", Descendants(toured).OfType<HoverSwitch>().Single(), CoachSide.Right),
                        };
                        for (int i = 0; i < stops.Length; i++)
                        {
                            var bubble = toured.TourBubble ?? throw new Exception($"The tour shows no bubble at stop {i + 1}.");
                            var target = stops[i].Target.RectangleToScreen(stops[i].Target.ClientRectangle);
                            bool placed = stops[i].Side switch
                            {
                                CoachSide.Above => Math.Abs(bubble.Bottom - target.Top) <= 1, CoachSide.Right => Math.Abs(bubble.Left - target.Right) <= 1,
                                _ => bubble.Bounds.IntersectsWith(target)
                            };
                            if (bubble.Title != stops[i].Title || !placed) throw new Exception($"Tour stop {i + 1} is '{bubble.Title}' at {bubble.Bounds}, not '{stops[i].Title}' beside {target}.");
                            using (var shot = new Bitmap(toured.Width, toured.Height))
                            {
                                toured.DrawToBitmap(shot, new Rectangle(Point.Empty, toured.Size));
                                using var picture = new Bitmap(bubble.Width, bubble.Height); bubble.DrawToBitmap(picture, new Rectangle(Point.Empty, bubble.Size));
                                using (var g = Graphics.FromImage(shot))
                                {
                                    if (bubble.Region?.Clone() is { } clip) { clip.Translate(bubble.Left - toured.Left, bubble.Top - toured.Top); g.Clip = clip; }
                                    g.DrawImage(picture, bubble.Left - toured.Left, bubble.Top - toured.Top);
                                }
                                shot.Save(Path.Combine(output, $"tour-{i + 1}.png"));
                            }
                            ((Button)bubble.Controls.Find("CoachNext", true).Single()).PerformClick(); Application.DoEvents();
                        }
                        if (toured.TourBubble is not null) throw new Exception("The tour did not end after its last bubble.");
                        toured.Close();
                    }
                    lines.Add("PASS: the tour shows five bubbles in order (map, window mode, layer, Edit client, hover focus), each beside the part it explains, and ends with Got it");
                    // Simulate an open client window that sits somewhere other than its map position.
                    var map = Descendants(idle).OfType<LayoutCanvas>().First();
                    var applyButton = idle.Controls.Find("ApplyPositions", true).Single();
                    void Recheck() => typeof(MainForm).GetMethod("UpdateApplyButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(idle, null);
                    map.Assigned = new() { [slot.Id] = new GameWindow(1, 1, 1, installation.Executable, "World of Warcraft", new(slot.X + 200, slot.Y + 100, slot.Width, slot.Height), false) }; Recheck(); Application.DoEvents();
                    if (!applyButton.Visible) throw new Exception("Apply positions did not appear for an open client away from its map position.");
                    using (var shot = new Bitmap(idle.Width, idle.Height)) { idle.DrawToBitmap(shot, new Rectangle(Point.Empty, idle.Size)); shot.Save(Path.Combine(output, "workspace-apply.png")); }
                    map.Assigned = new() { [slot.Id] = new GameWindow(1, 1, 1, installation.Executable, "World of Warcraft", slot.Bounds, false) }; Recheck(); Application.DoEvents();
                    if (applyButton.Visible) throw new Exception("Apply positions stayed visible after the open client matched its map position.");
                    lines.Add("PASS: Apply positions appears when an open client is away from its map position and hides again once it matches"); idle.Close();
                }
                CheckWindowFrame(new MainForm(true, settings), lines, output);
                var displays = new MainForm(true, settings); ((CheckBox)displays.Controls.Find("ArrangeDisplays", true).Single()).Checked = true;
                Render(displays, "display-mode");
                using (var owner = new MainForm(true, settings))
                {
                    owner.ShowInTaskbar = false; owner.Show(); owner.Update();
                    var menu = (MenuStrip)owner.Controls.Find("WorkspaceMenu", true).Single();
                    void RenderDialog(ToolStripItem command, string name)
                    {
                        bool captured = false;
                        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
                        timer.Tick += (_, _) =>
                        {
                            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f != this && f != owner);
                            if (dialog is null) return;
                            if (dialog.FormBorderStyle != FormBorderStyle.None || !dialog.Controls.OfType<DialogTitleBar>().Any()) throw new Exception(name + " opened with the Windows title bar instead of the HoverDeck frame.");
                            dialog.Update(); using var bitmap = new Bitmap(dialog.Width, dialog.Height);
                            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(output, name + ".png"));
                            captured = true; timer.Stop(); dialog.Close();
                        };
                        // Some commands open their dialog only after an await (the setup guide refreshes the WoW versions first).
                        timer.Start(); command.PerformClick(); for (int i = 0; i < 200 && !captured; i++) { Application.DoEvents(); Thread.Sleep(50); } timer.Stop();
                        if (!captured) throw new Exception("Could not open settings dialog: " + name);
                        lines.Add("PASS: opened, rendered and closed " + name + " through its menu");
                    }
                    ToolStripItem Command(int menuIndex, string text) => ((ToolStripMenuItem)menu.Items[menuIndex]).DropDownItems.Cast<ToolStripItem>().Single(i => i.Text == text);
                    RenderDialog(Command(1, "Hover focus…"), "hover-settings");
                    RenderDialog(((ToolStripMenuItem)menu.Items[0]).DropDownItems[4], "direct-defaults");
                    RenderDialog(Command(1, "Battle.net connections…"), "connections-menu");
                    RenderDialog(Command(1, "Setup guide…"), "setup-guide-menu");
                    // HoverDeck's own message box, and the frame on every dialog opened above.
                    bool framedMessage = false;
                    using (var capture = new System.Windows.Forms.Timer { Interval = 150 })
                    {
                        capture.Tick += (_, _) =>
                        {
                            var box = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f != this && f != owner); if (box is null) return;
                            capture.Stop(); box.Update(); using var shot = new Bitmap(box.Width, box.Height); box.DrawToBitmap(shot, new Rectangle(Point.Empty, box.Size)); shot.Save(Path.Combine(output, "message-box.png"));
                            framedMessage = box.FormBorderStyle == FormBorderStyle.None && box.Controls.OfType<DialogTitleBar>().Any(); box.Close();
                        };
                        capture.Start(); ThemedMessageBox.Show(owner, "Delete Client 3?\n\nIts position, launch settings and saved login are removed from this layout. A game that is running is not closed.", "Delete client", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    }
                    if (!framedMessage) throw new Exception("HoverDeck's message box did not open with the HoverDeck frame.");
                    lines.Add("PASS: messages use HoverDeck's own framed message box");
                    owner.Close();
                }
                using var web = new WebView2 { Dock = DockStyle.Fill }; Controls.Add(web);
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(output, "test-browser-profile"));
                await web.EnsureCoreWebView2Async(environment);
                var completed = new TaskCompletionSource<bool>();
                web.CoreWebView2.NavigationCompleted += (_, e) => completed.TrySetResult(e.IsSuccess);
                web.CoreWebView2.NavigateToString("<!doctype html><html><body>HoverDeck local WebView2 test</body></html>");
                if (!await completed.Task.WaitAsync(TimeSpan.FromSeconds(20))) throw new Exception("Local browser navigation failed.");
                string body = await web.CoreWebView2.ExecuteScriptAsync("document.body.innerText");
                if (!body.Contains("HoverDeck local WebView2 test")) throw new Exception("Local browser rendering failed.");
                lines.Add("PASS: packaged WebView2 initializes, renders and reads a local test page.");
                lines.Add("No Blizzard login or web session was accessed.");
            }
            catch (Exception ex) { lines.Add(ex.ToString()); Environment.ExitCode = 1; }
            finally { File.WriteAllLines(Path.Combine(output, "report.txt"), lines); Close(); }
        };
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetParent(nint window);
    // Resolves a screen point the way Windows does: the deepest window first, then up
    // through parents while a child answers HTTRANSPARENT.
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    private static int HitTest(Form form, Point screen)
    {
        // Wait until the point really belongs to this window: right after Show (or while
        // another window briefly covers it) WindowFromPoint can return something else.
        var window = Native.WindowFromPoint(new(screen.X, screen.Y));
        for (int i = 0; i < 30 && GetAncestor(window, 2) != form.Handle; i++) { Application.DoEvents(); Thread.Sleep(100); window = Native.WindowFromPoint(new(screen.X, screen.Y)); }
        if (GetAncestor(window, 2) != form.Handle) throw new Exception($"Another window covers the test point {screen}; close it and run the check again.");
        nint lParam = (nint)((screen.Y & 0xFFFF) << 16 | (screen.X & 0xFFFF));
        while (window != 0) { int hit = (int)SendMessage(window, 0x0084, 0, lParam); if (hit != -1) return hit; window = GetParent(window); }
        return 0;
    }
    private static void CheckWindowFrame(MainForm form, List<string> lines, string output)
    {
        using (form)
        {
            form.TopMost = true; form.StartPosition = FormStartPosition.Manual; // shown like the real app (ShowInTaskbar changes recreate the window)
            var area = Screen.PrimaryScreen!.WorkingArea; form.Bounds = new(area.X + 40, area.Y + 40, 1200, 800);
            form.Show(); form.Update(); Application.DoEvents();
            var b = form.Bounds; var title = form.Controls.Find("TitleBar", true).Single(); var menu = (MenuStrip)form.Controls.Find("WorkspaceMenu", true).Single();
            var start = form.Controls.OfType<Control>().SelectMany(Descendants).First(c => c is BlueButton);
            var emptyBar = new Point(menu.RectangleToScreen(menu.ClientRectangle).Right - 20, b.Y + title.Height / 2);
            var expected = new (string Name, Point Point, int Hit)[]
            {
                ("empty top-bar area drags the window", emptyBar, 2), ("logo drags the window", new(b.X + 40, b.Y + title.Height / 2), 2),
                ("top edge resizes", new(b.X + b.Width / 2, b.Y + 2), 12), ("left edge over the client cards resizes", new(b.X + 2, b.Y + 200), 10),
                ("right edge resizes", new(b.Right - 2, b.Y + 400), 11), ("bottom edge resizes", new(b.X + 600, b.Bottom - 2), 15), ("bottom-right corner resizes", new(b.Right - 2, b.Bottom - 2), 17),
                ("menu items stay clickable", menu.RectangleToScreen(menu.Items[0].Bounds) is var item ? new(item.X + item.Width / 2, item.Y + item.Height / 2) : default, 1),
                ("Start clients stays clickable", start.RectangleToScreen(new Rectangle(start.Width / 2, start.Height / 2, 1, 1)).Location, 1),
                ("close button stays clickable", new(b.Right - 23, b.Y + title.Height / 2), 1),
            };
            foreach (var (name, point, hit) in expected)
            {
                int actual = HitTest(form, point);
                if (actual != hit) throw new Exception($"Window frame: {name} returned hit-test {actual}, expected {hit}.");
                lines.Add("PASS: " + name);
            }
            // Real screen pixels, focused and unfocused: Windows must not paint its own
            // caption or frame over the borderless window (DrawToBitmap cannot show that).
            CheckScreenPixels(form, emptyBar, lines, output, "active");
            using (var other = new Form { ShowInTaskbar = false, TopMost = true, StartPosition = FormStartPosition.Manual, Bounds = new(b.Right + 10, b.Y, 120, 80) })
            {
                other.Show(); other.Activate(); Application.DoEvents(); Thread.Sleep(300); Application.DoEvents(); // no Refresh: a repaint would hide frame drawing done on deactivation
                if (Form.ActiveForm == form) throw new Exception("Could not move focus away from the main window for the unfocused check.");
                CheckScreenPixels(form, emptyBar, lines, output, "inactive");
            }
            form.Activate(); Application.DoEvents();
            var normal = form.Bounds;
            foreach (var screen in Screen.AllScreens)
            {
                form.Bounds = new(screen.WorkingArea.X + 20, screen.WorkingArea.Y + 20, Math.Min(1100, screen.WorkingArea.Width - 40), Math.Min(780, screen.WorkingArea.Height - 40));
                form.WindowState = FormWindowState.Maximized; Application.DoEvents();
                var window = form.Bounds; var client = form.RectangleToScreen(form.ClientRectangle);
                if (window != screen.WorkingArea || client != screen.WorkingArea) throw new Exception($"Maximized on {screen.DeviceName}: window {window}, client {client}, work area {screen.WorkingArea}.");
                form.WindowState = FormWindowState.Normal; Application.DoEvents();
            }
            form.Bounds = normal; Application.DoEvents();
            lines.Add($"PASS: maximizing fills the work area on each of {Screen.AllScreens.Length} displays without covering the taskbar");
            form.WindowState = FormWindowState.Normal; Application.DoEvents();
            form.WindowState = FormWindowState.Minimized; Application.DoEvents(); form.WindowState = FormWindowState.Normal; Application.DoEvents();
            if (form.Bounds != normal) throw new Exception($"Restored bounds {form.Bounds} differ from {normal}.");
            lines.Add("PASS: maximize, minimize and restore keep the original window bounds");
            form.Close();
        }
    }
    private static void CheckScreenPixels(MainForm form, Point emptyBar, List<string> lines, string output, string state)
    {
        Thread.Sleep(250); Application.DoEvents();
        var b = form.Bounds; using var shot = new Bitmap(b.Width, b.Height);
        using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
        shot.Save(Path.Combine(output, "window-" + state + ".png"));
        var probes = new (string Where, Point At, Color Expected)[]
        {
            ("top bar at the very top", new(emptyBar.X - b.X, 2), AppTheme.Bar), ("top bar middle", new(emptyBar.X - b.X, 29), AppTheme.Bar),
            ("left edge", new(1, b.Height / 2), AppTheme.Rail), ("right edge", new(b.Width - 2, b.Height / 2), AppTheme.Background), ("bottom edge", new(b.Width / 2, b.Height - 2), AppTheme.Background),
            ("X field right border", FieldEdge(form, "Client X", b), AppTheme.FieldBorder), ("client-count field right border", FieldEdge(form, "Number of clients", b), AppTheme.FieldBorder),
        };
        foreach (var (where, at, expected) in probes)
        {
            var actual = shot.GetPixel(at.X, at.Y);
            if (Math.Abs(actual.R - expected.R) > 8 || Math.Abs(actual.G - expected.G) > 8 || Math.Abs(actual.B - expected.B) > 8)
                throw new Exception($"Window frame ({state}): {where} shows {actual} on screen instead of {expected}. Windows is drawing its own frame over the window.");
        }
        lines.Add($"PASS: on screen while {state}, the top bar and all edges show HoverDeck's own colours (no Windows caption or frame)");
    }
    private static Point FieldEdge(MainForm form, string field, Rectangle window)
    {
        var box = Descendants(form).First(c => c.AccessibleName == field).Parent!.Parent!;
        var r = box.RectangleToScreen(box.ClientRectangle); return new(r.Right - 1 - window.X, r.Y + r.Height / 2 - window.Y);
    }
    private static IEnumerable<Control> Descendants(Control c) => new[] { c }.Concat(c.Controls.Cast<Control>().SelectMany(Descendants));
}
