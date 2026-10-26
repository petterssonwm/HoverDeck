namespace HoverDeck;

// Only window identity and the original stacking flag enter crash-recovery storage.
public sealed class WindowLayerSnapshot
{
    public long Handle { get; set; }
    public int ProcessId { get; set; }
    public long Started { get; set; }
    public bool WasTopmost { get; set; }
    public static WindowLayerSnapshot From(GameWindow window) => new() { Handle = window.Handle.ToInt64(), ProcessId = window.ProcessId, Started = window.Started };
    public void Restore()
    {
        // Always enqueue restoration: an earlier asynchronous promotion may still be pending.
        if (Native.IsSameWindow(Handle, ProcessId, Started))
            Native.SetTopmost((nint)Handle, WasTopmost);
    }
}

internal sealed record CoverageTargets(GameWindow[] Tracked, GameWindow[] Fullscreen)
{
    public static readonly CoverageTargets Empty = new(Array.Empty<GameWindow>(), Array.Empty<GameWindow>());
}

internal interface IWindowLayerBackend
{
    nint Foreground { get; }
    bool Exists(WindowLayerSnapshot window);
    bool CanCover(nint handle);
    bool IsTopmost(nint handle);
    bool TaskbarAbove(nint handle);
    void SetTopmost(nint handle, bool value);
}

internal sealed class WindowsWindowLayerBackend : IWindowLayerBackend
{
    public nint Foreground => Native.GetAncestor(Native.GetForegroundWindow(), 3);
    public bool Exists(WindowLayerSnapshot window) => Native.IsSameWindow(window.Handle, window.ProcessId, window.Started);
    public bool CanCover(nint handle) => Native.IsWindowVisible(handle) && !Native.IsIconic(handle);
    public bool IsTopmost(nint handle) => Native.IsTopmost(handle);
    public bool TaskbarAbove(nint handle) => Native.TaskbarAbove(handle);
    public void SetTopmost(nint handle, bool value) => Native.SetTopmost(handle, value);
}

internal sealed class TaskbarCoverage(IWindowLayerBackend backend, Action<List<WindowLayerSnapshot>> persist)
{
    private readonly Dictionary<long, WindowLayerSnapshot> changed = new();
    public void Update(CoverageTargets targets)
    {
        var foreground = backend.Foreground;
        bool playing = targets.Tracked.Any(w => w.Handle == foreground && backend.Exists(WindowLayerSnapshot.From(w)));
        var desired = playing ? targets.Fullscreen.Where(w => !w.Minimized && backend.CanCover(w.Handle)).Select(WindowLayerSnapshot.From)
            .Where(backend.Exists).DistinctBy(w => w.Handle).ToDictionary(w => w.Handle) : new Dictionary<long, WindowLayerSnapshot>();
        var errors = new List<Exception>(); bool removed = false;
        foreach (var old in changed.Values.ToArray())
        {
            if (desired.TryGetValue(old.Handle, out var wanted) && wanted.ProcessId == old.ProcessId && wanted.Started == old.Started) continue;
            try
            {
                if (backend.Exists(old)) backend.SetTopmost((nint)old.Handle, old.WasTopmost);
                changed.Remove(old.Handle); removed = true;
            }
            catch (Exception ex) { errors.Add(ex); }
        }
        // A stale recovery entry is harmless; never forget a changed window before restoring it.
        if (removed) persist(changed.Values.ToList());
        foreach (var window in desired.Values.OrderBy(w => w.Handle == foreground.ToInt64()))
        {
            try
            {
                bool topmost = backend.IsTopmost((nint)window.Handle);
                if (!changed.ContainsKey(window.Handle))
                {
                    if (topmost)
                    {
                        // Keep a game's existing topmost flag, but allow it above a raised taskbar.
                        if (backend.TaskbarAbove((nint)window.Handle)) backend.SetTopmost((nint)window.Handle, true);
                        continue;
                    }
                    changed.Add(window.Handle, window);
                    try { persist(changed.Values.ToList()); }
                    catch { changed.Remove(window.Handle); throw; }
                }
                if (!topmost || backend.TaskbarAbove((nint)window.Handle)) backend.SetTopmost((nint)window.Handle, true);
            }
            catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count > 0) throw new InvalidOperationException(errors[0].Message, errors[0]);
    }
}
