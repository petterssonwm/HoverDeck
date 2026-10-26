namespace HoverDeck;

// The canvas, presets and launch validation share one live Windows desktop snapshot.
internal sealed class DesktopState
{
    private readonly Func<List<MonitorInfo>> read;
    public List<MonitorInfo> Monitors { get; private set; } = new();
    public DesktopState(Func<List<MonitorInfo>>? read = null) { this.read = read ?? Native.Monitors; Refresh(); }
    public bool Refresh()
    {
        var next = read();
        if (next.Count == 0 || next.Any(m => m.Bounds.Width <= 0 || m.Bounds.Height <= 0))
            throw new InvalidOperationException("The current display snapshot is incomplete.");
        if (Monitors.SequenceEqual(next)) return false;
        Monitors = next;
        return true;
    }
}

internal static class DesktopGeometry
{
    public static IEnumerable<(Rectangle Bounds, string Edge, int Pixels)> ReservedAreas(MonitorInfo monitor)
    {
        var b = monitor.Bounds;
        var w = Rectangle.Intersect(b, monitor.WorkArea);
        if (w.Width <= 0 || w.Height <= 0) yield break;
        if (w.Top > b.Top) yield return (new(b.Left, b.Top, b.Width, w.Top - b.Top), "top", w.Top - b.Top);
        if (w.Bottom < b.Bottom) yield return (new(b.Left, w.Bottom, b.Width, b.Bottom - w.Bottom), "bottom", b.Bottom - w.Bottom);
        if (w.Left > b.Left) yield return (new(b.Left, w.Top, w.Left - b.Left, w.Height), "left", w.Left - b.Left);
        if (w.Right < b.Right) yield return (new(w.Right, w.Top, b.Right - w.Right, w.Height), "right", b.Right - w.Right);
    }
    // Subtract the actual displays, so gaps between displays remain off-screen.
    public static List<Rectangle> OffscreenParts(Rectangle window, IEnumerable<MonitorInfo> monitors) => Subtract(window, monitors.Select(m => m.Bounds));
    public static List<Rectangle> Subtract(Rectangle window, IEnumerable<Rectangle> covered)
    {
        var parts = new List<Rectangle> { window };
        foreach (var rectangle in covered)
        {
            var remaining = new List<Rectangle>();
            foreach (var part in parts)
            {
                var hit = Rectangle.Intersect(part, rectangle);
                if (hit.Width <= 0 || hit.Height <= 0) { remaining.Add(part); continue; }
                if (hit.Top > part.Top) remaining.Add(Rectangle.FromLTRB(part.Left, part.Top, part.Right, hit.Top));
                if (hit.Bottom < part.Bottom) remaining.Add(Rectangle.FromLTRB(part.Left, hit.Bottom, part.Right, part.Bottom));
                if (hit.Left > part.Left) remaining.Add(Rectangle.FromLTRB(part.Left, hit.Top, hit.Left, hit.Bottom));
                if (hit.Right < part.Right) remaining.Add(Rectangle.FromLTRB(hit.Right, hit.Top, part.Right, hit.Bottom));
            }
            parts = remaining;
        }
        return parts;
    }
}
