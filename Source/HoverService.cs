using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace HoverDeck;

public sealed class HoverSnapshot
{
    public int ProcessId { get; set; }
    public long Started { get; set; }
    public int Tracking { get; set; }
    public int Raise { get; set; }
    public int Delay { get; set; }
    public List<WindowLayerSnapshot> WindowLayers { get; set; } = new();
    // Window stacking failures are reported as an AggregateException, and only once the Windows
    // settings are back; a failure to restore the settings themselves is thrown as it is.
    public void Restore()
    {
        var errors = new List<Exception>();
        try
        {
            Native.Setting(0x1001, 0);
            Native.Setting(0x100D, Raise);
            Native.Setting(0x2003, Delay);
            Native.Setting(0x1001, Tracking);
        }
        finally
        {
            foreach (var window in WindowLayers) try { window.Restore(); } catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count > 0) throw new AggregateException("Could not restore client window stacking.", errors);
    }
}

// Windows implements focus without raising via active-window tracking. A dedicated
// low-level mouse hook gates that feature BEFORE each pointer event is queued.
// Only configured client HWNDs are eligible. No clicks or game hooks, and no keys except the
// modifier carry-over (HeldModifiers): a release-and-press of a Ctrl/Shift/Alt you are already holding.
public sealed class HoverService : IDisposable
{
    internal static readonly string RecoveryPath = Path.Combine(Storage.DirectoryPath, "hover-recovery.json");
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new(false);
    private Control? dispatcher;
    private Exception? startupError;
    private HoverSnapshot? snapshot;
    private volatile HashSet<nint> eligible = new();
    private bool enabled, nativeEnabled, suspended;
    private System.Windows.Forms.Timer? carryTimer;
    private nint carryWindow;
    private int carryCount;
    private volatile string carryNote = "";
    // Changes each time a carry-over fails, so the activity log can show why.
    public int CarryCount => Volatile.Read(ref carryCount);
    public string CarryNote => carryNote;
    private readonly int delay;
    private nint hook;
    private Native.HookProc? callback;
    private System.Windows.Forms.Timer? timer;
    private System.Windows.Forms.Timer? coverageTimer;
    private TaskbarCoverage? coverage;
    private readonly WindowsWindowOrderBackend order = new();
    private nint foregroundHook;
    private Native.WinEventProc? foregroundCallback;
    private volatile CoverageTargets coverageTargets = CoverageTargets.Empty;
    public string? Error { get; private set; }
    public string? CoverageError { get; private set; }
    public HoverService(int delayMs)
    {
        delay = delayMs;
        thread = new Thread(Run) { IsBackground = true, Name = "WoW hover focus" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("Could not start the hover focus service.");
        if (startupError is not null) throw new InvalidOperationException("Could not manage Windows hover focus.", startupError);
    }
    public static void RecoverIfNeeded()
    {
        if (!File.Exists(RecoveryPath)) return;
        HoverSnapshot? old;
        try { old = JsonSerializer.Deserialize<HoverSnapshot>(File.ReadAllText(RecoveryPath), Storage.Json); }
        catch (JsonException) { File.Delete(RecoveryPath); return; } // unreadable: nothing left that could be restored
        if (old is null || Running(old.ProcessId, old.Started)) return;
        Recover(old);
    }
    private static bool Running(int pid, long started)
    {
        try { using var p = Process.GetProcessById(pid); return p.StartTime.ToUniversalTime().Ticks == started && !p.HasExited; }
        catch { return false; } // gone, or a different process we may not inspect
    }
    // Restores what a run left behind. A client window that can no longer be restored (closed, or
    // running as administrator) must not keep the recovery file, or hover focus could never start again.
    private static void Recover(HoverSnapshot old)
    {
        try { old.Restore(); } catch (AggregateException) { }
        File.Delete(RecoveryPath);
    }
    public static void Watch(int pid, long start)
    {
        try { using var p = Process.GetProcessById(pid); if (p.StartTime.ToUniversalTime().Ticks == start) p.WaitForExit(); } catch { }
        try
        {
            // Serializes recovery against the next application instance.
            using var mutex = new Mutex(false, "Local\\HoverDeck.HoverRecovery");
            try { mutex.WaitOne(); } catch (AbandonedMutexException) { }
            try
            {
                if (!File.Exists(RecoveryPath)) return;
                var old = JsonSerializer.Deserialize<HoverSnapshot>(File.ReadAllText(RecoveryPath), Storage.Json);
                if (old?.ProcessId == pid && old.Started == start) Recover(old);
            }
            finally { mutex.ReleaseMutex(); }
        }
        catch { /* Recovery file remains for the next app launch. */ }
    }
    private void Run()
    {
        try
        {
            using var mutex = new Mutex(false, "Local\\HoverDeck.HoverRecovery");
            try { mutex.WaitOne(); } catch (AbandonedMutexException) { }
            try
            {
                RecoverIfNeeded();
                using var process = Process.GetCurrentProcess();
                snapshot = new() { ProcessId = process.Id, Started = process.StartTime.ToUniversalTime().Ticks, Tracking = Native.Setting(0x1000), Raise = Native.Setting(0x100C), Delay = Native.Setting(0x2002) };
                Storage.Write(RecoveryPath, snapshot);
                var watcher = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                watcher.ArgumentList.Add("--watch"); watcher.ArgumentList.Add(snapshot.ProcessId.ToString()); watcher.ArgumentList.Add(snapshot.Started.ToString());
                Process.Start(watcher)?.Dispose();
                Native.Setting(0x1001, 0);
                Native.Setting(0x100D, 0);
                Native.Setting(0x2003, delay);
            }
            finally { mutex.ReleaseMutex(); }
            dispatcher = new Control(); _ = dispatcher.Handle;
            callback = Hook;
            hook = Native.SetWindowsHookEx(14, callback, Native.GetModuleHandle(null), 0);
            if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows refused the mouse monitor.");
            // Also catches a window appearing/disappearing beneath a stationary cursor.
            timer = new() { Interval = 30 };
            timer.Tick += (_, _) => CheckCursor(); timer.Start();
            coverage = new(new WindowsWindowLayerBackend(), windows => { snapshot!.WindowLayers = windows; Storage.Write(RecoveryPath, snapshot); });
            coverageTimer = new() { Interval = 150 };
            coverageTimer.Tick += (_, _) => CheckCoverage(); coverageTimer.Start();
            // Re-check layers the moment another window takes focus, not only on the next tick.
            foregroundCallback = (_, _, window, _, _, _, _) => { CarryModifiers(window); CheckCoverage(); };
            foregroundHook = Native.SetWinEventHook(0x0003, 0x0003, 0, foregroundCallback, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND, out of context
            ready.Set(); Application.Run();
        }
        catch (Exception ex) { startupError = ex; Error = ex.Message; ready.Set(); }
        finally
        {
            timer?.Dispose();
            coverageTimer?.Dispose();
            carryTimer?.Dispose();
            if (foregroundHook != 0) Native.UnhookWinEvent(foregroundHook);
            if (hook != 0) Native.UnhookWindowsHookEx(hook);
            if (snapshot is not null)
            {
                try { snapshot.Restore(); File.Delete(RecoveryPath); } catch (Exception ex) { Error = "Could not restore Windows hover settings: " + ex.Message; }
            }
            dispatcher?.Dispose();
        }
    }
    private nint Hook(int code, nint wp, nint lp)
    {
        if (code >= 0)
        {
            // Runs for every mouse event system-wide: read the point without allocating.
            try { Gate(new(Marshal.ReadInt32(lp), Marshal.ReadInt32(lp, 4))); }
            catch (Exception ex) { Error = ex.Message; enabled = false; try { Native.Setting(0x1001, 0); nativeEnabled = false; } catch { } }
        }
        return Native.CallNextHookEx(hook, code, wp, lp);
    }
    private void CheckCursor()
    {
        try { if (Native.GetCursorPos(out var point)) Gate(point); }
        catch (Exception ex) { Error = ex.Message; enabled = false; }
    }
    private void Gate(Native.POINT point)
    {
        bool pressed = (Native.GetAsyncKeyState(1) & 0x8000) != 0 || (Native.GetAsyncKeyState(2) & 0x8000) != 0;
        bool desired = enabled && !suspended && !pressed && eligible.Count > 0
            && Native.GetAncestor(Native.WindowFromPoint(point), 2) is var root && eligible.Contains(root) && !Native.IsIconic(root);
        if (desired == nativeEnabled) return;
        Native.Setting(0x1001, desired ? 1 : 0);
        nativeEnabled = desired;
    }
    // A client that gains focus never saw the key-down of a modifier that was already held, so WoW treats
    // it as released. A plain extra key-down would reach it marked as a key repeat (Windows already counts
    // the key as down), so each held modifier gets a quick release-and-press instead. It waits a moment so
    // the client has finished its own focus handling (which clears its keys) first. The final state is the
    // real one: still held, and released by the real key. Only for configured clients while hover focus is on.
    private void CarryModifiers(nint window)
    {
        if (!enabled || suspended || window == 0) return;
        if (!eligible.Contains(Native.GetAncestor(window, 2))) return;
        carryWindow = window;
        if (carryTimer is null) { carryTimer = new() { Interval = CarryDelayMs }; carryTimer.Tick += (_, _) => { carryTimer.Stop(); FinishCarry(); }; }
        carryTimer.Stop(); carryTimer.Start();
    }
    internal const int CarryDelayMs = 80;
    private void FinishCarry()
    {
        var window = carryWindow; carryWindow = 0;
        if (!enabled || suspended || window == 0) return;
        var held = HeldModifiers.Held(key => (Native.GetAsyncKeyState(key) & 0x8000) != 0);
        if (held.Length == 0) return;
        string names = HeldModifiers.Names(held);
        if (Native.GetForegroundWindow() != window) { Note($"Held {names} not carried: focus moved on before it could be pressed again."); return; }
        try { HeldModifiers.Send(HeldModifiers.Events(held)); }
        catch (Exception ex) { Note($"Held {names} not carried: {ex.Message}"); }
    }
    private void Note(string text) { carryNote = text; Interlocked.Increment(ref carryCount); }
    public void UpdateWindows(IEnumerable<GameWindow> windows) => eligible = windows.Select(w => w.Handle).ToHashSet();
    public void UpdateCoverage(IEnumerable<GameWindow> tracked, IEnumerable<GameWindow> fullscreen, IReadOnlyDictionary<nint, int>? layers = null, IReadOnlySet<nint>? fullMonitor = null) => coverageTargets = new(tracked.ToArray(), fullscreen.ToArray(), layers, fullMonitor);
    private void CheckCoverage()
    {
        var targets = suspended ? CoverageTargets.Empty : coverageTargets;
        try { coverage?.Update(targets); CoverageError = null; }
        catch (Exception ex) { CoverageError = ex.Message; }
        try { if (targets.Layers is { Count: > 1 } layers) WindowStacking.Apply(order, layers); }
        catch (Exception ex) { CoverageError = "Client layers: " + ex.Message; }
    }
    private void Post(Action action)
    {
        if (dispatcher is { IsDisposed: false }) dispatcher.BeginInvoke((Action)(() => { try { action(); } catch (Exception ex) { Error = ex.Message; enabled = false; } }));
    }
    public void Enable(bool value) => Post(() => { enabled = value; CheckCursor(); });
    public void Suspend(bool value) => Post(() => { suspended = value; CheckCursor(); CheckCoverage(); });
    public void SetDelay(int value) => Post(() => Native.Setting(0x2003, value));
    public void Dispose()
    {
        if (dispatcher is { IsDisposed: false }) dispatcher.BeginInvoke((Action)(() => Application.ExitThread()));
        thread.Join(TimeSpan.FromSeconds(3));
        ready.Dispose();
    }
}

internal static class HeldModifiers
{
    // Left/right Ctrl, Shift and Alt: the side-specific key to test, the generic key WoW receives,
    // and the keyboard scan code (with the extended flag that marks the right-hand Ctrl and Alt).
    internal static readonly (int Test, ushort Key, ushort Scan, bool Extended, string Name)[] Modifiers =
    {
        (0xA2, 0x11, 0x1D, false, "left Ctrl"), (0xA3, 0x11, 0x1D, true, "right Ctrl"),
        (0xA0, 0x10, 0x2A, false, "left Shift"), (0xA1, 0x10, 0x36, false, "right Shift"),
        (0xA4, 0x12, 0x38, false, "left Alt"), (0xA5, 0x12, 0x38, true, "right Alt"),
    };
    internal static (int Test, ushort Key, ushort Scan, bool Extended, string Name)[] Held(Func<int, bool> isDown) => Modifiers.Where(k => isDown(k.Test)).ToArray();
    internal static string Names(IEnumerable<(int Test, ushort Key, ushort Scan, bool Extended, string Name)> keys) => string.Join(" + ", keys.Select(k => k.Name));
    // Each held key is released and pressed again, so the client sees a fresh press rather than a repeat.
    // The key ends up down, exactly like the real key, and its real release still arrives later.
    internal static LoginInput.Input[] Events(IEnumerable<(int Test, ushort Key, ushort Scan, bool Extended, string Name)> held) => held
        .SelectMany(k => new[] { Event(k.Key, k.Scan, k.Extended, true), Event(k.Key, k.Scan, k.Extended, false) }).ToArray();
    private static LoginInput.Input Event(ushort key, ushort scan, bool extended, bool up) =>
        new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Scan = scan, Flags = (extended ? 1u : 0) | (up ? 2u : 0) } } };
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, LoginInput.Input[] input, int size);
    internal static void Send(LoginInput.Input[] events)
    {
        if (events.Length == 0) return;
        if (SendInput((uint)events.Length, events, Marshal.SizeOf<LoginInput.Input>()) != events.Length)
            throw new InvalidOperationException("Windows blocked the key. Run HoverDeck and the client at the same permission level.");
    }
}
