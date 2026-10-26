using System.Diagnostics;
using System.Text.Json;

namespace HoverDeck;

internal static class TaskbarCoverageTests
{
    public static void Run(Action<string, bool> check)
    {
        var first = new GameWindow(501, 51, 1001, @"C:\Fixture\Wow.exe", "Fixture", new(0, 0, 800, 900), false);
        var second = first with { Handle = 502, ProcessId = 52, Started = 1002 };
        var backend = new FakeLayers(first, second);
        var saved = new List<WindowLayerSnapshot>(); bool journalFirst = true;
        var coverage = new TaskbarCoverage(backend, values => saved = values);
        backend.Changing = (handle, value) => { if (value && !backend.Topmost.Contains(handle) && !saved.Any(w => w.Handle == handle.ToInt64())) journalFirst = false; };
        var targets = new CoverageTargets(new[] { first, second }, new[] { first, second });
        coverage.Update(targets);
        check("Other applications do not cause game windows to become topmost", backend.Calls.Count == 0);
        backend.Foreground = first.Handle; coverage.Update(targets);
        check("Focusing a tracked game covers taskbars for both fullscreen clients", backend.Topmost.SetEquals(new[] { first.Handle, second.Handle }) && saved.Count == 2);
        check("Original window identities are saved before changing stacking", journalFirst && saved.All(w => !w.WasTopmost));
        int calls = backend.Calls.Count; coverage.Update(targets);
        check("Stable fullscreen coverage does not continually raise game windows", backend.Calls.Count == calls);
        backend.BarAbove.Add(second.Handle); coverage.Update(targets);
        check("A taskbar raised over a managed client triggers only the needed correction", backend.Calls.Count == calls + 1 && backend.Calls.Last() == (second.Handle, true));
        backend.Foreground = 999; coverage.Update(targets);
        check("Switching to another app restores stacking and clears recovery entries", backend.Topmost.Count == 0 && saved.Count == 0);
        backend.Topmost.Add(second.Handle); backend.BarAbove.Add(second.Handle); backend.Foreground = first.Handle; coverage.Update(targets);
        check("A game already marked topmost can also move above a raised taskbar", !backend.BarAbove.Contains(second.Handle) && !saved.Any(w => w.Handle == second.Handle.ToInt64()));
        coverage.Update(CoverageTargets.Empty);
        check("A window already made topmost by the game is preserved", backend.Topmost.SetEquals(new[] { second.Handle }) && saved.Count == 0);
        backend.Topmost.Clear(); coverage.Update(targets);
        coverage.Update(new(new[] { first, second }, new[] { first }));
        check("Switching a client to Windowed restores only that client's stacking", backend.Topmost.SetEquals(new[] { first.Handle }) && saved.Count == 1);
        backend.Hidden.Add(first.Handle); coverage.Update(targets);
        check("Minimizing a fullscreen client releases its temporary stacking", !backend.Topmost.Contains(first.Handle));
        backend.Hidden.Clear(); coverage.Update(targets);
        backend.Live[first.Handle] = first with { Started = first.Started + 1 }; calls = backend.Calls.Count;
        backend.Foreground = 999; coverage.Update(CoverageTargets.Empty);
        check("Reused process identities are not modified during restoration", backend.Calls.Skip(calls).All(c => c.Item1 != first.Handle));
        backend.Topmost.Clear(); backend.Live[first.Handle] = first; backend.Foreground = first.Handle;
        var failing = new TaskbarCoverage(backend, _ => throw new IOException("Synthetic journal failure"));
        bool failed = false; try { failing.Update(new(new[] { first }, new[] { first })); } catch (InvalidOperationException) { failed = true; }
        check("Failed recovery storage prevents changing window stacking", failed && backend.Topmost.Count == 0);
        backend.Deferred = true;
        var queued = new TaskbarCoverage(backend, _ => { });
        queued.Update(new(new[] { first }, new[] { first }));
        backend.Foreground = 999; queued.Update(CoverageTargets.Empty); backend.Flush();
        check("Leaving before an asynchronous promotion finishes still restores normal stacking", backend.Topmost.Count == 0);
    }

    private sealed class FakeLayers(params GameWindow[] windows) : IWindowLayerBackend
    {
        public readonly Dictionary<nint, GameWindow> Live = windows.ToDictionary(w => w.Handle);
        public readonly HashSet<nint> Topmost = new(), BarAbove = new(), Hidden = new();
        public readonly List<(nint, bool)> Calls = new();
        public Action<nint, bool>? Changing;
        public bool Deferred;
        private readonly List<(nint Handle, bool Value)> queued = new();
        public nint Foreground { get; set; }
        public bool Exists(WindowLayerSnapshot window) => Live.TryGetValue((nint)window.Handle, out var live) && live.ProcessId == window.ProcessId && live.Started == window.Started;
        public bool CanCover(nint handle) => !Hidden.Contains(handle);
        public bool IsTopmost(nint handle) => Topmost.Contains(handle);
        public bool TaskbarAbove(nint handle) => BarAbove.Contains(handle);
        public void SetTopmost(nint handle, bool value) { Changing?.Invoke(handle, value); Calls.Add((handle, value)); if (Deferred) queued.Add((handle, value)); else Apply(handle, value); }
        private void Apply(nint handle, bool value) { if (value) Topmost.Add(handle); else Topmost.Remove(handle); BarAbove.Remove(handle); }
        public void Flush() { foreach (var change in queued) Apply(change.Handle, change.Value); queued.Clear(); }
    }
}

// Uses only this test process's windows. Focus is simulated; no real application is activated.
internal sealed class TaskbarCoverageIntegrationTests : Form
{
    public TaskbarCoverageIntegrationTests(string report, string? fixturePath = null)
    {
        ShowInTaskbar = false; Opacity = 0;
        Shown += async (_, _) =>
        {
            var lines = new List<string>(); var recovery = new List<WindowLayerSnapshot>(); Process? child = null;
            using var game = new FixtureWindow { Text = "HoverDeck partial-window test", BackColor = Color.Teal };
            using var bar = new FixtureWindow { Text = "HoverDeck taskbar fixture", BackColor = Color.Goldenrod, TopMost = true };
            try
            {
                var monitor = Native.Monitors().Last(); var bounds = ClientPlacement.Fit(monitor, ClientWindowMode.FullscreenWindowed, ClientFit.RightHalf);
                game.Bounds = bounds; bar.Bounds = new(bounds.X, bounds.Bottom - 32, bounds.Width, 32);
                game.Show(); bar.Show(); await Task.Delay(100);
                using var process = Process.GetCurrentProcess();
                var window = new GameWindow(game.Handle, process.Id, process.StartTime.ToUniversalTime().Ticks, Environment.ProcessPath!, "Fixture", bounds, false);
                var backend = new SimulatedFocusBackend { Foreground = game.Handle };
                var coverage = new TaskbarCoverage(backend, windows => recovery = windows);
                var targets = new CoverageTargets(new[] { window }, new[] { window });
                nint foreground = Native.GetForegroundWindow();
                coverage.Update(targets); await Task.Delay(100);
                if (!Native.IsTopmost(game.Handle) || Native.TaskbarAbove(game.Handle)) throw new Exception("Partial client did not rise above the real taskbar.");
                nint atOverlap = Native.GetAncestor(Native.WindowFromPoint(new(bounds.X + bounds.Width / 2, bounds.Bottom - 16)), 2);
                if (atOverlap != game.Handle) throw new Exception("A taskbar fixture still covered the partial client.");
                lines.Add("PASS: a partial-screen client covers both the real taskbar and an overlapping topmost taskbar fixture.");
                Native.GetWindowRect(game.Handle, out var actual);
                if (actual.Rectangle != bounds || Native.GetForegroundWindow() != foreground) throw new Exception("Layering changed size, position or keyboard focus.");
                lines.Add("PASS: stacking changes preserve exact geometry and do not steal keyboard focus.");
                backend.Foreground = 0; coverage.Update(targets); await Task.Delay(100);
                if (Native.IsTopmost(game.Handle) || recovery.Count != 0) throw new Exception("Leaving WoW did not restore normal stacking.");
                lines.Add("PASS: switching away restores the game to normal stacking.");
                backend.Foreground = game.Handle; coverage.Update(targets); await Task.Delay(100);
                coverage.Update(new(new[] { window }, Array.Empty<GameWindow>())); await Task.Delay(100);
                if (Native.IsTopmost(game.Handle)) throw new Exception("Windowed mode kept fullscreen stacking.");
                lines.Add("PASS: Windowed mode removes temporary taskbar coverage.");
                coverage.Update(targets); await Task.Delay(100);
                var persisted = JsonSerializer.Deserialize<List<WindowLayerSnapshot>>(JsonSerializer.Serialize(recovery, Storage.Json), Storage.Json)!;
                foreach (var original in persisted) original.Restore(); await Task.Delay(100);
                if (Native.IsTopmost(game.Handle)) throw new Exception("Recovery did not restore the original stacking flag.");
                lines.Add("PASS: persisted recovery metadata restores stacking through the watchdog's recovery method.");
                if (fixturePath is not null)
                {
                    child = Process.Start(new ProcessStartInfo(fixturePath) { UseShellExecute = false })!;
                    GameWindow? external = null;
                    for (int i = 0; i < 100 && external is null; i++) { external = Native.Windows(new[] { fixturePath }).FirstOrDefault(w => w.ProcessId == child.Id); if (external is null) await Task.Delay(50); }
                    if (external is null) throw new Exception("Separate-process fixture did not open.");
                    Native.Place(external, bounds); await Task.Delay(100); Native.Place(external, bounds);
                    backend.Foreground = external.Handle;
                    targets = new(new[] { external }, new[] { external });
                    coverage.Update(targets); await Task.Delay(200);
                    if (!Native.IsTopmost(external.Handle) || Native.TaskbarAbove(external.Handle)) throw new Exception("Separate-process coverage did not apply.");
                    lines.Add("PASS: partial-screen coverage also works for a client in a separate process.");
                    backend.Foreground = 0; coverage.Update(targets); await Task.Delay(100);
                    backend.Foreground = external.Handle; coverage.Update(targets);
                    foreach (var original in recovery) original.Restore(); await Task.Delay(200);
                    if (Native.IsTopmost(external.Handle)) throw new Exception("Queued promotion survived restoration in another process.");
                    lines.Add("PASS: immediate recovery reverses an asynchronous promotion in the separate client process.");
                }
                lines.Add("Only synthetic local windows were changed; no WoW clients or Windows taskbar settings were modified.");
            }
            catch (Exception ex) { lines.Add("FAILED: " + ex); Environment.ExitCode = 1; }
            finally
            {
                foreach (var original in recovery) try { original.Restore(); } catch { }
                if (child is not null) { try { if (!child.HasExited) { child.CloseMainWindow(); if (!child.WaitForExit(2000)) child.Kill(); } } finally { child.Dispose(); } }
                game.Close(); bar.Close(); File.WriteAllLines(report, lines); Close();
            }
        };
    }
    private sealed class FixtureWindow : Form
    {
        public FixtureWindow() { StartPosition = FormStartPosition.Manual; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; }
        protected override bool ShowWithoutActivation => true;
    }
    private sealed class SimulatedFocusBackend : IWindowLayerBackend
    {
        private readonly WindowsWindowLayerBackend native = new();
        public nint Foreground { get; set; }
        public bool Exists(WindowLayerSnapshot window) => native.Exists(window);
        public bool CanCover(nint handle) => native.CanCover(handle);
        public bool IsTopmost(nint handle) => native.IsTopmost(handle);
        public bool TaskbarAbove(nint handle) => native.TaskbarAbove(handle);
        public void SetTopmost(nint handle, bool value) => native.SetTopmost(handle, value);
    }
}
