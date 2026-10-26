using System.Diagnostics;
using System.Text.Json;

namespace HoverDeck;

// Real local windows with a fake launcher gateway. Never talks to Blizzard.
internal sealed class BattleNetIntegrationTests : Form
{
    private readonly string executable, report;
    private readonly List<string> lines = new();
    public BattleNetIntegrationTests(string executable, string report)
    {
        this.executable = executable; this.report = report;
        Text = "HoverDeck Battle.net fixture checks — no server"; ShowInTaskbar = false; Opacity = 0;
        Shown += async (_, _) => await Run();
    }
    private void Check(string name, bool value) { if (!value) throw new Exception("FAIL: " + name); lines.Add("PASS: " + name); }
    private async Task Run()
    {
        var gateway = new FakeGateway(executable);
        try
        {
            var monitors = Native.Monitors(); var area = monitors.Last().WorkArea;
            Slot NewSlot(int number) => new() { Id = "test" + number, Name = "Fixture " + number, X = area.X + 30 + (number - 1) % 2 * 660, Y = area.Y + 30 + (number - 1) / 2 % 2 * 430, Width = 620, Height = 400,
                LaunchMethod = ClientLaunchMethod.BattleNet, BattleNet = new() { GameTabId = "game-nav-btn-WoWC", GameVersion = "World of Warcraft Classic", Executable = executable, AccountName = $"WoW{number} ({(number == 2 ? "US" : number == 3 ? "KR" : "EU")})" } };
            var layout = new Layout { Slots = new() { NewSlot(1), NewSlot(2) } }; int saves = 0;
            var launcher = new Launcher(gateway);
            await launcher.Run(layout, monitors, true, lines.Add, CancellationToken.None, () => saves++);
            Check("Two requested accounts each issue exactly one Play", gateway.Plays == 2 && saves == 4);
            Check("Each account is bound to a different live process", layout.Slots.Select(s => s.BattleNet!.BoundIdentity).Distinct().Count() == 2 && layout.Slots.All(s => s.BattleNet!.BoundIdentity.Length > 0));
            Check("Both Battle.net client windows match their saved visible positions", launcher.Assign(layout, launcher.Scan(layout)).All(p => p.Value.Bounds == layout.Slots.Single(s => s.Id == p.Key).Bounds));
            var originalBounds = layout.Slots[0].Bounds;
            layout.Slots[0].WindowMode = ClientWindowMode.FullscreenWindowed;
            layout.Slots[0].SetBounds(ClientPlacement.Fit(monitors.Last(), layout.Slots[0].WindowMode, ClientFit.Screen));
            await launcher.Run(layout, monitors, false, lines.Add, CancellationToken.None);
            Check("Fullscreen windowed places the real fixture at the full display edges", launcher.Assign(layout, launcher.Scan(layout))[layout.Slots[0].Id].Bounds == monitors.Last().Bounds && gateway.Plays == 2);
            layout.Slots[0].WindowMode = ClientWindowMode.Windowed;
            layout.Slots[0].SetBounds(ClientPlacement.Fit(monitors.Last(), layout.Slots[0].WindowMode, ClientFit.Screen));
            await launcher.Run(layout, monitors, false, lines.Add, CancellationToken.None);
            Check("Windowed places the same fixture inside the taskbar work area", launcher.Assign(layout, launcher.Scan(layout))[layout.Slots[0].Id].Bounds == monitors.Last().WorkArea && gateway.Plays == 2);
            layout.Slots[0].SetBounds(originalBounds);
            layout = JsonSerializer.Deserialize<Layout>(JsonSerializer.Serialize(layout, Storage.Json), Storage.Json)!;
            launcher = new Launcher(gateway);
            await launcher.Run(layout, monitors, true, lines.Add, CancellationToken.None);
            Check("Restarted launcher reuses saved account bindings without another Play", gateway.Plays == 2);
            var first = launcher.Assign(layout, launcher.Scan(layout))[layout.Slots[0].Id];
            using (var process = Process.GetProcessById(first.ProcessId)) { process.CloseMainWindow(); await process.WaitForExitAsync(); }
            await launcher.Run(layout, monitors, true, lines.Add, CancellationToken.None);
            Check("Only the missing account is relaunched", gateway.Plays == 3 && gateway.RequestedAccounts.Last() == "WoW1 (EU)");
            layout.Slots.Add(NewSlot(3)); using var cancel = new CancellationTokenSource(); gateway.AfterPlay = cancel.Cancel;
            bool stopped = false;
            try { await launcher.Run(layout, monitors, true, lines.Add, cancel.Token); } catch (OperationCanceledException) { stopped = true; }
            gateway.AfterPlay = null;
            Check("Cancel after Play leaves a persistent pending attempt", stopped && layout.Slots[2].BattleNet!.PendingSinceUtc.Length > 0 && gateway.Plays == 4);
            layout = JsonSerializer.Deserialize<Layout>(JsonSerializer.Serialize(layout, Storage.Json), Storage.Json)!;
            launcher = new Launcher(gateway); await launcher.Run(layout, monitors, true, lines.Add, CancellationToken.None);
            Check("Retry after restart picks up the pending client without another Play", gateway.Plays == 4 && layout.Slots[2].BattleNet!.BoundIdentity.Length > 0 && layout.Slots[2].BattleNet!.PendingSinceUtc.Length == 0);
            var outside = gateway.StartFixture();
            for (int i = 0; i < 100 && !launcher.Scan(layout).Any(w => w.ProcessId == outside.Id); i++) await Task.Delay(100);
            layout.Slots.Add(NewSlot(4)); bool unknownStopped = false;
            try { await launcher.Run(layout, monitors, true, lines.Add, CancellationToken.None); } catch (InvalidOperationException ex) { unknownStopped = ex.Message.Contains("not tracked by HoverDeck"); }
            Check("Unknown externally opened account blocks duplicate launch", unknownStopped && gateway.Plays == 4);
            outside.CloseMainWindow(); await outside.WaitForExitAsync();
            await launcher.Run(layout, monitors, true, lines.Add, CancellationToken.None);
            Check("Closing an external client allows HoverDeck to launch and track the requested account", gateway.Plays == 5 && launcher.Assign(layout, launcher.Scan(layout)).Count == 4);
            var missing = NewSlot(9); missing.BattleNet!.ConnectionId = "connected-fixture"; missing.BattleNet.WebAccountId = "missing-fixture-license"; layout.Slots.Add(missing); bool missingStopped = false;
            try { await launcher.Run(layout, monitors, true, lines.Add, CancellationToken.None); } catch (InvalidOperationException ex) { missingStopped = ex.Message.Contains("WoW9 (EU) was not found"); }
            Check("Unavailable account never presses Play", missingStopped && gateway.Plays == 5 && missing.BattleNet!.PendingSinceUtc.Length == 0);
            layout.Slots.Remove(missing);
            var unavailableVersion = NewSlot(5); unavailableVersion.BattleNet!.GameVersion = "Unavailable fixture version"; layout.Slots.Add(unavailableVersion); bool versionStopped = false;
            try { await launcher.Run(layout, monitors, true, lines.Add, CancellationToken.None); } catch (InvalidOperationException ex) { versionStopped = ex.Message.Contains("version was not found"); }
            Check("Unavailable version reports an error without pressing Play or leaving a pending attempt", versionStopped && gateway.Plays == 5 && unavailableVersion.BattleNet.PendingSinceUtc.Length == 0);
            layout.Slots.Remove(unavailableVersion);
            foreach (var slot in layout.Slots.Take(2))
            {
                var window = launcher.Assign(layout, launcher.Scan(layout))[slot.Id];
                using var process = Process.GetProcessById(window.ProcessId); process.CloseMainWindow(); await process.WaitForExitAsync();
                slot.BattleNet!.ClearBinding(); slot.BattleNet.ConnectionId = "connected-fixture"; slot.BattleNet.WebAccountId = "license-" + slot.Id;
            }
            bool canceled = false;
            try { await launcher.Run(layout, monitors, true, lines.Add, new CancellationToken(true)); } catch (OperationCanceledException) { canceled = true; }
            Check("Canceling before launch sends no Play", canceled && gateway.Plays == 5);
            await launcher.Run(layout, monitors, true, lines.Add, CancellationToken.None);
            Check("Connected accounts launch directly with no confirmation callback", gateway.Plays == 7 && gateway.RequestedAccounts.TakeLast(2).SequenceEqual(new[] { "WoW1 (EU)", "WoW2 (US)" }) && layout.Slots.Take(2).Select(s => s.BattleNet!.BoundIdentity).Distinct().Count() == 2);
            await launcher.Run(layout, monitors, true, lines.Add, CancellationToken.None);
            Check("Reusing connected clients sends no additional Play", gateway.Plays == 7);
            foreach (var process in gateway.Processes) { try { if (!process.HasExited) { process.CloseMainWindow(); process.WaitForExit(5000); } } catch { } }
            // A starting game is logged in with the account Battle.net has selected, so the next
            // account must not be chosen while the previous game is still loading.
            var slow = new FakeGateway(executable, gateway.Processes) { Arguments = "--window-delay 3000" };
            var ordered = new Layout { Slots = new() { NewSlot(1), NewSlot(2) } }; var sequential = new Launcher(slow);
            int windowsAtSecondPrepare = -1; slow.BeforePrepare = () => { if (slow.Plays == 1) windowsAtSecondPrepare = sequential.Scan(ordered).Count; };
            await sequential.Run(ordered, monitors, true, lines.Add, CancellationToken.None);
            Check("The next account is selected only after the previous game window has opened", slow.Plays == 2 && windowsAtSecondPrepare == 1);
            Check("Both slow-loading clients are placed at their positions", sequential.Assign(ordered, sequential.Scan(ordered)).Count == 2
                && sequential.Assign(ordered, sequential.Scan(ordered)).All(p => p.Value.Bounds == ordered.Slots.Single(s => s.Id == p.Key).Bounds));
            lines.Add("Battle.net launch coordination checks passed using local fixtures only.");
        }
        catch (Exception ex) { lines.Add(ex.ToString()); Environment.ExitCode = 1; }
        finally
        {
            foreach (var process in gateway.Processes) { try { if (!process.HasExited) process.CloseMainWindow(); } catch { } process.Dispose(); }
            File.WriteAllLines(report, lines); Close();
        }
    }
    private sealed class FakeGateway(string executable, List<Process>? processes = null) : IBattleNetGateway
    {
        public int Plays;
        public Action? AfterPlay, BeforePrepare;
        public string Arguments = "";
        public readonly List<Process> Processes = processes ?? new();
        public readonly List<string> RequestedAccounts = new();
        public Process StartFixture() { var process = Process.Start(new ProcessStartInfo(executable, Arguments) { UseShellExecute = false })!; Processes.Add(process); return process; }
        public Task<string> Prepare(BattleNetLaunch profile, Action<string> log, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); BeforePrepare?.Invoke();
            if (profile.GameVersion != "World of Warcraft Classic") throw new InvalidOperationException("The selected version was not found in the fixture launcher.");
            return Task.FromResult(BattleNetAccounts.Resolve(profile, new[] { "WoW1 (EU)", "WoW2 (US)", "WoW3 (KR)", "WoW4 (EU)" }));
        }
        public Task Play(BattleNetLaunch profile, string account, CancellationToken token) { token.ThrowIfCancellationRequested(); Plays++; RequestedAccounts.Add(account); StartFixture(); AfterPlay?.Invoke(); return Task.CompletedTask; }
    }
}
