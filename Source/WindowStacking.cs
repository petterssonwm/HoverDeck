namespace HoverDeck;

// Client layers: where clients overlap, a higher layer stays in front of a lower one.
// Only the game windows are reordered among themselves. The frontmost of them keeps its
// place relative to other applications, so HoverDeck never pulls WoW in front of a browser.
internal interface IWindowOrderBackend
{
    IReadOnlyList<nint> FrontToBack(IReadOnlyCollection<nint> handles);
    nint WindowAbove(nint handle);
    bool IsTopmost(nint handle);
    bool CanStack(nint handle);
    void PlaceBelow(nint handle, nint above);
}

internal sealed class WindowsWindowOrderBackend : IWindowOrderBackend
{
    public IReadOnlyList<nint> FrontToBack(IReadOnlyCollection<nint> handles)
    {
        var wanted = handles.ToHashSet(); var order = new List<nint>(wanted.Count);
        for (nint h = Native.GetTopWindow(0); h != 0 && order.Count < wanted.Count; h = Native.GetWindow(h, 2)) // GW_HWNDNEXT
            if (wanted.Contains(h)) order.Add(h);
        return order;
    }
    public nint WindowAbove(nint handle)
    {
        nint above = Native.GetWindow(handle, 3); // GW_HWNDPREV
        // Never anchor to a window in the other topmost band; that would change this client's band.
        return above != 0 && Native.IsTopmost(above) == Native.IsTopmost(handle) ? above : 0;
    }
    public bool IsTopmost(nint handle) => Native.IsTopmost(handle);
    public bool CanStack(nint handle) => Native.IsWindowVisible(handle) && !Native.IsIconic(handle);
    // Asynchronous, without activation or size/position changes, so a busy game never blocks HoverDeck.
    public void PlaceBelow(nint handle, nint above) => Native.SetWindowPos(handle, above, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200 | 0x4000);
}

internal static class WindowStacking
{
    // Returns true when the order was corrected. Does nothing unless at least two layers are in use.
    public static bool Apply(IWindowOrderBackend backend, IReadOnlyDictionary<nint, int> layers)
    {
        var handles = layers.Keys.Where(backend.CanStack).ToList();
        if (handles.Select(h => layers[h]).Distinct().Count() < 2) return false;
        bool changed = false;
        foreach (var band in handles.GroupBy(backend.IsTopmost))
        {
            var current = backend.FrontToBack(band.ToList());
            if (current.Count < 2) continue;
            // Higher layers first. OrderBy is stable, so clients on the same layer keep their
            // current order and behave like normal windows among themselves.
            var wanted = current.OrderByDescending(h => layers[h]).ToList();
            if (wanted.SequenceEqual(current)) continue;
            nint above = backend.WindowAbove(current[0]);
            foreach (var handle in wanted) { backend.PlaceBelow(handle, above); above = handle; }
            changed = true;
        }
        return changed;
    }
}
