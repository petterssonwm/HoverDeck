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
