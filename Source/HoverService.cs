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
    public void Restore()
    {
        try
        {
            Native.Setting(0x1001, 0);
            Native.Setting(0x100D, Raise);
            Native.Setting(0x2003, Delay);
            Native.Setting(0x1001, Tracking);
        }
        finally
        {
            var errors = new List<Exception>();
            foreach (var window in WindowLayers) try { window.Restore(); } catch (Exception ex) { errors.Add(ex); }
            if (errors.Count > 0) throw new AggregateException("Could not restore client window stacking.", errors);
        }
    }
}

// Windows implements focus without raising via active-window tracking. A dedicated
// low-level mouse hook gates that feature BEFORE each pointer event is queued.
// Only configured client HWNDs are eligible. No injected keys, clicks or game hooks.
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
    private int delay;
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
        var old = JsonSerializer.Deserialize<HoverSnapshot>(File.ReadAllText(RecoveryPath), Storage.Json);
        if (old is null) return;
        try { using var p = Process.GetProcessById(old.ProcessId); if (p.StartTime.ToUniversalTime().Ticks == old.Started && !p.HasExited) return; } catch (ArgumentException) { }
        old.Restore(); File.Delete(RecoveryPath);
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
                if (old?.ProcessId == pid && old.Started == start) { old.Restore(); File.Delete(RecoveryPath); }
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
            foregroundCallback = (_, _, _, _, _, _, _) => CheckCoverage();
            foregroundHook = Native.SetWinEventHook(0x0003, 0x0003, 0, foregroundCallback, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND, out of context
            ready.Set(); Application.Run();
        }
        catch (Exception ex) { startupError = ex; Error = ex.Message; ready.Set(); }
        finally
        {
            timer?.Dispose();
            coverageTimer?.Dispose();
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
            try { var mouse = Marshal.PtrToStructure<Native.MOUSEHOOK>(lp); Gate(mouse.Point); }
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
        var root = Native.GetAncestor(Native.WindowFromPoint(point), 2);
        bool pressed = (Native.GetAsyncKeyState(1) & 0x8000) != 0 || (Native.GetAsyncKeyState(2) & 0x8000) != 0;
        bool desired = enabled && !suspended && !pressed && eligible.Contains(root) && !Native.IsIconic(root);
        if (desired == nativeEnabled) return;
        Native.Setting(0x1001, desired ? 1 : 0);
        nativeEnabled = desired;
    }
    public void UpdateWindows(IEnumerable<GameWindow> windows) => eligible = windows.Select(w => w.Handle).ToHashSet();
    public void UpdateCoverage(IEnumerable<GameWindow> tracked, IEnumerable<GameWindow> fullscreen, IReadOnlyDictionary<nint, int>? layers = null) => coverageTargets = new(tracked.ToArray(), fullscreen.ToArray(), layers);
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
    public void SetDelay(int value) => Post(() => { delay = value; Native.Setting(0x2003, value); });
    public void Dispose()
    {
        if (dispatcher is { IsDisposed: false }) dispatcher.BeginInvoke((Action)(() => Application.ExitThread()));
        thread.Join(TimeSpan.FromSeconds(3));
        ready.Dispose();
    }
}
