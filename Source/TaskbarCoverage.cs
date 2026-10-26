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

// Layers maps each tracked client window to its layer (higher = in front). Null when unused.
// FullMonitor lists fullscreen clients that cover their entire display.
internal sealed record CoverageTargets(GameWindow[] Tracked, GameWindow[] Fullscreen, IReadOnlyDictionary<nint, int>? Layers = null, IReadOnlySet<nint>? FullMonitor = null)
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
    // A client covering its whole display does not need lifting: Windows keeps the taskbar out of the
    // way of an active full-display window by itself. If clients on higher layers sit on top of it,
    // lifting it would put it in the same band as them, and every click on it would briefly cover them.
    private bool YieldsToHigherLayers(GameWindow window, CoverageTargets targets)
    {
        if (targets.Layers is not { } layers || targets.FullMonitor is not { } full || !full.Contains(window.Handle)) return false;
        int Layer(nint handle) => layers.TryGetValue(handle, out var layer) ? layer : 1;
        return targets.Tracked.Any(w => w.Handle != window.Handle && !w.Minimized && Layer(w.Handle) > Layer(window.Handle) && w.Bounds.IntersectsWith(window.Bounds) && backend.CanCover(w.Handle));
    }
    public void Update(CoverageTargets targets)
    {
        var foreground = backend.Foreground;
        bool playing = targets.Tracked.Any(w => w.Handle == foreground && backend.Exists(WindowLayerSnapshot.From(w)));
        var desired = playing ? targets.Fullscreen.Where(w => !w.Minimized && backend.CanCover(w.Handle) && !YieldsToHigherLayers(w, targets)).Select(WindowLayerSnapshot.From)
            .Where(backend.Exists).DistinctBy(w => w.Handle).ToDictionary(w => w.Handle) : new Dictionary<long, WindowLayerSnapshot>();
        // While playing, clients above the lowest layer join the topmost band. Clicking a lower-layer
        // client then cannot raise it over them (no flicker), and a covered fullscreen client cannot
        // jump in front of them either. Leaving the game restores them like any covered client.
        if (playing && targets.Layers is { } layers)
        {
            int Layer(long handle) => layers.TryGetValue((nint)handle, out var layer) ? layer : 1;
            var visible = targets.Tracked.Where(w => !w.Minimized && backend.CanCover(w.Handle)).ToList();
            if (visible.Select(w => Layer(w.Handle)).Distinct().Count() > 1)
            {
                int lowest = visible.Min(w => Layer(w.Handle));
                foreach (var w in visible.Where(w => Layer(w.Handle) > lowest && !desired.ContainsKey(w.Handle)))
                    if (WindowLayerSnapshot.From(w) is var snap && backend.Exists(snap)) desired[snap.Handle] = snap;
            }
        }
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
            nint handle = (nint)window.Handle;
            try
            {
                // A game's own topmost flag is kept (and not recorded), but it may still need lifting above a raised taskbar.
                // A window is recorded before it is changed; one Windows refuses stays recorded, which is harmless to restore.
                bool topmost = backend.IsTopmost(handle);
                if (!topmost && !changed.ContainsKey(window.Handle))
                {
                    changed.Add(window.Handle, window);
                    try { persist(changed.Values.ToList()); }
                    catch { changed.Remove(window.Handle); throw; }
                }
                if (!topmost || backend.TaskbarAbove(handle)) backend.SetTopmost(handle, true);
            }
            catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count > 0) throw new InvalidOperationException(errors[0].Message, errors[0]);
    }
}
