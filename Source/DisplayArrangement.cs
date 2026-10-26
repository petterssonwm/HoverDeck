namespace HoverDeck;

internal static class DisplayArrangement
{
    public static MonitorInfo Move(MonitorInfo monitor, Point location)
    {
        var bounds = monitor.Bounds; var work = monitor.WorkArea;
        int dx = location.X - bounds.X, dy = location.Y - bounds.Y;
        bounds.Offset(dx, dy); work.Offset(dx, dy);
        return monitor with { Bounds = bounds, WorkArea = work };
    }
    public static List<MonitorInfo> Normalize(IReadOnlyList<MonitorInfo> monitors)
    {
        var primary = monitors.Single(m => m.Primary);
        return monitors.Select(m => Move(m, new(m.Bounds.X - primary.Bounds.X, m.Bounds.Y - primary.Bounds.Y))).ToList();
    }
    public static string? Validate(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0 || monitors.Count(m => m.Primary) != 1) return "Choose exactly one primary display.";
        if (monitors.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != monitors.Count) return "Each display must appear once.";
        if (monitors.Any(m => m.Bounds.Width <= 0 || m.Bounds.Height <= 0 || Math.Abs((long)m.Bounds.X) > 64000 || Math.Abs((long)m.Bounds.Y) > 64000)) return "A display position is outside the supported range.";
        for (int i = 0; i < monitors.Count; i++)
            for (int j = i + 1; j < monitors.Count; j++)
            {
                var overlap = Rectangle.Intersect(monitors[i].Bounds, monitors[j].Bounds);
                if (overlap.Width > 0 && overlap.Height > 0) return "Displays overlap. Drag them beside one another.";
            }
        var connected = new HashSet<int> { 0 };
        while (true)
        {
            int count = connected.Count;
            for (int i = 0; i < monitors.Count; i++)
                if (!connected.Contains(i) && connected.Any(j => SharesEdge(monitors[i].Bounds, monitors[j].Bounds))) connected.Add(i);
            if (count == connected.Count) break;
        }
        return connected.Count == monitors.Count ? null : "Bring the displays together so their edges touch.";
    }
    public static bool SharesEdge(Rectangle a, Rectangle b) =>
        ((a.Right == b.Left || b.Right == a.Left) && Math.Min(a.Bottom, b.Bottom) > Math.Max(a.Top, b.Top)) ||
        ((a.Bottom == b.Top || b.Bottom == a.Top) && Math.Min(a.Right, b.Right) > Math.Max(a.Left, b.Left));
    public static Rectangle Snap(Rectangle moving, IEnumerable<Rectangle> others, int threshold)
    {
        var neighbors = others.ToList();
        var xDeltas = neighbors.SelectMany(r => new[] { r.Left - moving.Right, r.Right - moving.Left, r.Left - moving.Left, r.Right - moving.Right });
        var yDeltas = neighbors.SelectMany(r => new[] { r.Top - moving.Bottom, r.Bottom - moving.Top, r.Top - moving.Top, r.Bottom - moving.Bottom });
        int dx = xDeltas.Where(d => Math.Abs((long)d) <= threshold).OrderBy(d => Math.Abs((long)d)).FirstOrDefault();
        int dy = yDeltas.Where(d => Math.Abs((long)d) <= threshold).OrderBy(d => Math.Abs((long)d)).FirstOrDefault();
        moving.Offset(dx, dy); return moving;
    }
    public static bool SamePositions(IReadOnlyList<MonitorInfo> a, IReadOnlyList<MonitorInfo> b) => a.Count == b.Count && a.All(m => b.Any(n => n.Name == m.Name && n.Bounds == m.Bounds && n.Primary == m.Primary));
}
