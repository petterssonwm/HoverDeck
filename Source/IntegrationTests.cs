using System.Diagnostics;

namespace HoverDeck;

internal sealed class IntegrationTests : Form
{
    private readonly string executable, report;
    private readonly List<string> lines = new();
    public IntegrationTests(string executable, string report)
    {
        this.executable = executable; this.report = report;
        Text = "HoverDeck integration checks"; ShowInTaskbar = false; Opacity = 0;
        Shown += async (_, _) => await Run();
    }
    private void Check(string name, bool value) { if (!value) throw new Exception("FAIL: " + name); lines.Add("PASS: " + name); }
    private async Task Run()
    {
        try
        {
            var monitors = Native.Monitors(); var area = monitors.Last().WorkArea;
            var layout = new Layout { Executable = executable, Slots = new() { new() { Id = "one", Name = "Test 1", X = area.X + 40, Y = area.Y + 60, Width = 620, Height = 400 }, new() { Id = "two", Name = "Test 2", X = area.X + 700, Y = area.Y + 60, Width = 620, Height = 400 } } };
            using var initial = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false })!;
            for (int i = 0; i < 100 && Native.Windows(new[] { executable }).Count == 0; i++) await Task.Delay(100);
            Check("Initial fixture opened", Native.Windows(new[] { executable }).Count == 1);
            var launcher = new Launcher();
            var newlyOpened = await launcher.Run(layout, monitors, true, lines.Add, CancellationToken.None);
            var windows = Native.Windows(new[] { executable });
            Check("Fill missing launches exactly one additional client", windows.Count == 2 && windows.Any(w => w.ProcessId == initial.Id));
            Check("Only the newly launched client is eligible for automatic login", newlyOpened.Count == 1 && newlyOpened.All(c => windows.Any(w => w.Identity == c.Identity && w.ProcessId != initial.Id)));
            Check("Real window geometry matches both requested rectangles", windows.Select(w => w.Bounds).ToHashSet().SetEquals(layout.Slots.Select(s => s.Bounds)));
            var identities = windows.Select(w => w.Identity).ToHashSet();
            newlyOpened = await launcher.Run(layout, monitors, true, lines.Add, CancellationToken.None);
            windows = Native.Windows(new[] { executable });
            Check("Repeated Start reuses both clients without duplicates", windows.Count == 2 && identities.SetEquals(windows.Select(w => w.Identity)));
            Check("Repeated Start does not queue logins for reused clients", newlyOpened.Count == 0);
            Native.ShowWindowAsync(windows[0].Handle, 6); Native.ShowWindowAsync(windows[1].Handle, 3);
            await Task.Delay(300);
            await launcher.Run(layout, monitors, false, lines.Add, CancellationToken.None);
            windows = Native.Windows(new[] { executable });
            Check("Restored minimized and maximized windows match the requested visible edges", windows.All(w => !w.Minimized) && windows.Select(w => w.Bounds).ToHashSet().SetEquals(layout.Slots.Select(s => s.Bounds)));
            using (var p = Process.GetProcessById(windows[0].ProcessId)) { p.CloseMainWindow(); await p.WaitForExitAsync(); }
            var survivor = Native.Windows(new[] { executable }).Single().Identity;
            await launcher.Run(layout, monitors, true, lines.Add, CancellationToken.None);
            windows = Native.Windows(new[] { executable });
            Check("Closing one client causes only its missing slot to refill", windows.Count == 2 && windows.Any(w => w.Identity == survivor));
            var beforeClose = windows.Select(w => w.Identity).ToHashSet();
            var notClosed = await launcher.Close(layout, lines.Add, CancellationToken.None, TimeSpan.FromSeconds(15));
            Check("Close clients closes every open client of the layout without force-killing", notClosed.Count == 0 && Native.Windows(new[] { executable }).Count == 0);
            await launcher.Run(layout, monitors, true, lines.Add, CancellationToken.None);
            windows = Native.Windows(new[] { executable });
            Check("Restart opens fresh clients in every slot at the requested rectangles", windows.Count == 2 && !windows.Any(w => beforeClose.Contains(w.Identity)) && windows.Select(w => w.Bounds).ToHashSet().SetEquals(layout.Slots.Select(s => s.Bounds)));
            // Clients opened outside HoverDeck: the player connects each window to a Battle.net client, which
            // HoverDeck cannot work out itself. Connected windows are then placed like HoverDeck's own.
            {
                var outsideLayout = new Layout { Slots = new() {
                    new() { Id = "hunter", Name = "Hunter", X = area.X + 40, Y = area.Y + 60, Width = 620, Height = 400, LaunchMethod = ClientLaunchMethod.BattleNet, BattleNet = new() { Executable = executable, GameVersion = "World of Warcraft Classic", GameTabId = "game-nav-btn-WoWC", AccountName = "WoW3 (EU)" } },
                    new() { Id = "warrior", Name = "Warrior", X = area.X + 700, Y = area.Y + 60, Width = 620, Height = 400, LaunchMethod = ClientLaunchMethod.BattleNet, BattleNet = new() { Executable = executable, GameVersion = "World of Warcraft Classic", GameTabId = "game-nav-btn-WoWC", AccountName = "WoW4 (EU)" } } } };
                var connector = new Launcher();
                var open = connector.Scan(outsideLayout).OrderBy(w => w.ProcessId).ToList();
                Check("Battle.net clients HoverDeck did not start are not matched on their own", open.Count == 2 && connector.Assign(outsideLayout, open).Count == 0);
                ConnectClientsForm.Apply(outsideLayout, connector.Bindings, new[] { (open[0], (Slot?)outsideLayout.Slots[1]), (open[1], (Slot?)outsideLayout.Slots[0]) });
                var connected = connector.Assign(outsideLayout, connector.Scan(outsideLayout));
                Check("Connecting open windows binds each one to the chosen Battle.net client", connected["warrior"].Identity == open[0].Identity && connected["hunter"].Identity == open[1].Identity);
                ConnectClientsForm.Apply(outsideLayout, connector.Bindings, new[] { (open[0], (Slot?)outsideLayout.Slots[0]), (open[1], (Slot?)outsideLayout.Slots[1]) });
                connected = connector.Assign(outsideLayout, connector.Scan(outsideLayout));
                Check("Connecting again swaps the clients without leaving a window on two clients", connected["hunter"].Identity == open[0].Identity && connected["warrior"].Identity == open[1].Identity
                    && outsideLayout.Slots.Select(s => s.BattleNet!.BoundIdentity).Distinct().Count() == 2);
                await connector.Run(outsideLayout, monitors, false, lines.Add, CancellationToken.None);
                connected = connector.Assign(outsideLayout, connector.Scan(outsideLayout));
                Check("Connected Battle.net clients are placed at their spots on the map without pressing Play", connected.All(p => p.Value.Bounds == outsideLayout.Slots.Single(s => s.Id == p.Key).Bounds));
                using var picker = new ConnectClientsForm(outsideLayout, open, connected, monitors, "Each card shows a live picture of an open WoW window. Choose which client it is.");
                picker.ShowInTaskbar = false; picker.TopMost = true; picker.Show(); Application.DoEvents(); await Task.Delay(800); Application.DoEvents();
                var choices = picker.Choices;
                Check("The connect window shows one card per open window, pre-filled with the current connections", choices.Count == 2 && choices.Single(c => c.Window.Identity == open[0].Identity).Slot?.Id == "hunter" && choices.Single(c => c.Window.Identity == open[1].Identity).Slot?.Id == "warrior");
                using (var shot = new Bitmap(picker.Width, picker.Height)) { using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(picker.Location, Point.Empty, picker.Size); shot.Save(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "connect-open-clients.png")); }
                picker.Close();
            }
            // Layers across separate client processes: slot two (layer 2) overlaps slot one (layer 1).
            var assigned = launcher.Assign(layout, Native.Windows(new[] { executable }));
            var lower = assigned["one"]; var upper = assigned["two"];
            Native.Place(upper, new(layout.Slots[0].X + 200, layout.Slots[0].Y + 100, 400, 250)); await Task.Delay(300);
            Native.SetWindowPos(lower.Handle, 0, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); await Task.Delay(300); // layer-1 client brought to the front
            var stackOrder = new WindowsWindowOrderBackend(); var stackLayers = new Dictionary<nint, int> { [lower.Handle] = 1, [upper.Handle] = 2 };
            for (int i = 0; i < 10 && WindowStacking.Apply(stackOrder, stackLayers); i++) await Task.Delay(150); // asynchronous across processes
            var front = stackOrder.FrontToBack(new[] { lower.Handle, upper.Handle });
            Check("Layers keep a higher-layer client in front of a lower-layer client in another process", front.Count == 2 && front[0] == upper.Handle);
            // The live path used by the app: layers handed to the hover service are enforced on their own.
            using (var layered = new HoverService(50))
            {
                Native.SetWindowPos(lower.Handle, 0, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); await Task.Delay(300); // layer-1 client in front
                layered.UpdateCoverage(new[] { lower, upper }, Array.Empty<GameWindow>(), new Dictionary<nint, int> { [lower.Handle] = 1, [upper.Handle] = 2 });
                await Task.Delay(800);
                front = stackOrder.FrontToBack(new[] { lower.Handle, upper.Handle });
                Check("The running service restacks open clients by layer without an Apply step", front[0] == upper.Handle);
                layered.UpdateCoverage(new[] { lower, upper }, Array.Empty<GameWindow>(), new Dictionary<nint, int> { [lower.Handle] = 3, [upper.Handle] = 2 }); // layers changed while open
                await Task.Delay(800);
                front = stackOrder.FrontToBack(new[] { lower.Handle, upper.Handle });
                Check("Changing layers of open clients takes effect immediately", front[0] == lower.Handle);
            }
            var before = new[] { Native.Setting(0x1000), Native.Setting(0x100C), Native.Setting(0x2002) };
            using (var service = new HoverService(50))
            {
                service.UpdateWindows(windows); service.Enable(true); await Task.Delay(150);
                Check("Native hover delay and no-raise mode are configured", Native.Setting(0x2002) == 50 && Native.Setting(0x100C) == 0);
                service.SetDelay(0); await Task.Delay(100); Check("Zero-delay mode is accepted by Windows", Native.Setting(0x2002) == 0);
                service.Enable(false); await Task.Delay(100); Check("Disabling focus turns active tracking off", Native.Setting(0x1000) == 0);
            }
            Check("Original Windows hover settings are restored on exit", before.SequenceEqual(new[] { Native.Setting(0x1000), Native.Setting(0x100C), Native.Setting(0x2002) }));
            Check("Hotkey registers globally", Native.RegisterHotKey(Handle, 7, 3 | 0x4000, (uint)Keys.H)); Native.UnregisterHotKey(Handle, 7);
            lines.Add("Integration checks passed.");
        }
        catch (Exception ex) { lines.Add(ex.ToString()); Environment.ExitCode = 1; }
        finally
        {
            foreach (var window in Native.Windows(new[] { executable })) { try { using var p = Process.GetProcessById(window.ProcessId); p.CloseMainWindow(); } catch { } }
            File.WriteAllLines(report, lines); Close();
        }
    }
}
