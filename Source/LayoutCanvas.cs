using System.Drawing.Drawing2D;

namespace HoverDeck;

public sealed class LayoutCanvas : Control
{
    public List<MonitorInfo> Monitors { get; set; } = new();
    public new Layout? Layout { get; set; }
    public int Selected { get; set; }
    private bool displayEditMode;
    private Rectangle? displayViewBounds;
    public bool DisplayEditMode
    {
        get => displayEditMode;
        set { if (displayEditMode == value) return; displayEditMode = value; FitView(); }
    }
    public int SelectedMonitor { get; set; }
    public Dictionary<string, GameWindow> Assigned { get; set; } = new();
    public event Action? SelectionChanged;
    public event Action<Point>? ClientContextRequested;
    public event Action? LayoutEdited;
    public event Action? MonitorSelectionChanged;
    public event Action? MonitorsEdited;
    private double scale = 1;
    private PointF offset;
    private Rectangle desktop;
    private Point dragStart;
    private Rectangle initial;
    private bool dragging, resizing, draggingMonitor;
    private static readonly Color TaskbarColor = Color.FromArgb(218, 186, 130);
    public static readonly Color[] SlotColors = { Color.FromArgb(95, 223, 183), Color.FromArgb(111, 171, 255), Color.FromArgb(233, 178, 103), Color.FromArgb(185, 148, 247), Color.FromArgb(239, 138, 169), Color.FromArgb(98, 208, 225) };
    public LayoutCanvas()
    {
        DoubleBuffered = true; ResizeRedraw = true;
        BackColor = Color.FromArgb(12, 14, 19); ForeColor = AppTheme.Ink;
        AccessibleName = "Display layout editor";
    }
    private void Transform()
    {
        if (draggingMonitor) return; // Keep the grabbed display under the pointer.
        if (Monitors.Count == 0) return;
        // Editing a monitor's origin must not change the map's scale or origin.
        // Keep the editing viewport until an explicit Fit view / Reset action.
        var bounds = Monitors.Select(m => m.Bounds).Aggregate(Rectangle.Union);
        desktop = DisplayEditMode ? displayViewBounds ??= bounds : bounds;
        scale = Math.Max(.01, Math.Min((ClientSize.Width - 48d) / desktop.Width, (ClientSize.Height - 64d) / desktop.Height));
        offset = new((float)((Width - desktop.Width * scale) / 2 - desktop.Left * scale), (float)((Height - desktop.Height * scale) / 2 - desktop.Top * scale - 12));
    }
    public void FitView() { displayViewBounds = null; Invalidate(); }
    private RectangleF Map(Rectangle r) => new((float)(offset.X + r.X * scale), (float)(offset.Y + r.Y * scale), (float)(r.Width * scale), (float)(r.Height * scale));
    internal RectangleF Project(Rectangle r) { Transform(); return Map(r); }
    // The map repaints on every mouse move while dragging, so its fixed fonts, brushes and pens are made once.
    private static readonly Font small = new("Segoe UI", 9), regular = new("Segoe UI", 10, FontStyle.Bold), numberFont = new("Segoe UI", 21, FontStyle.Bold);
    private static readonly Pen gridPen = new(Color.FromArgb(20, 24, 33)), taskbarPen = new(TaskbarColor, 1), framePen = new(AppTheme.Line), liveEdge = new(Color.FromArgb(235, 241, 250), 1) { DashStyle = DashStyle.Dash };
    private static readonly Brush muted = new SolidBrush(Color.FromArgb(111, 119, 135)), number = new SolidBrush(Color.FromArgb(74, 82, 98)), monitorFill = new SolidBrush(Color.FromArgb(22, 26, 34)),
        tagBack = new SolidBrush(Color.FromArgb(15, 19, 26)), tagText = new SolidBrush(Color.FromArgb(139, 147, 163)),
        taskbarHatch = new HatchBrush(HatchStyle.ForwardDiagonal, Color.FromArgb(144, 119, 77), Color.FromArgb(67, 59, 44));
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); Transform();
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int x = 0; x < Width; x += 24) g.DrawLine(gridPen, x, 0, x, Height);
        for (int y = 0; y < Height; y += 24) g.DrawLine(gridPen, 0, y, Width, y);
        if (Monitors.Count == 0) { g.DrawString("No displays detected", regular, Brushes.White, 30, 60); return; }
        var covered = !DisplayEditMode && Layout is not null ? Layout.Slots.Where(s => s.WindowMode == ClientWindowMode.FullscreenWindowed).Select(s => s.Bounds).ToArray() : Array.Empty<Rectangle>();
        for (int i = 0; i < Monitors.Count; i++)
        {
            var m = Monitors[i]; var r = Map(m.Bounds);
            using var border = new Pen(DisplayEditMode && i == SelectedMonitor ? AppTheme.Blue : m.Primary ? Color.FromArgb(59, 90, 128) : Color.FromArgb(44, 50, 61), DisplayEditMode && i == SelectedMonitor ? 3 : 2);
            g.FillRectangle(monitorFill, r); g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height);
            g.DrawString(DisplayNumber(m, i), numberFont, number, r.X + 12, r.Y + 8);
            var description = $"{m.Bounds.Width} × {m.Bounds.Height}" + (m.Primary ? "  ·  Primary" : "");
            g.DrawString(description, small, muted, r.X + 12, r.Bottom - 26);
            // Taskbar space belongs to the monitor model. Draw it at that
            // monitor's own Windows coordinates before rendering the clients.
            foreach (var area in DesktopGeometry.ReservedAreas(m))
            {
                foreach (var part in DesktopGeometry.Subtract(area.Bounds, covered))
                {
                    var bar = Map(part);
                    g.FillRectangle(taskbarHatch, bar);
                    g.DrawRectangle(taskbarPen, bar.X, bar.Y, bar.Width, bar.Height);
                }
            }
        }
        // A layout editor must show the complete requested window geometry,
        // including overlap with taskbars and areas beyond a monitor. The same
        // desktop-to-canvas transform is used for displays, taskbars and slots.
        // Back to front: lower layers first; within a layer the selected client is drawn last.
        var indices = Layout is null || DisplayEditMode ? Array.Empty<int>() : DrawOrder().ToArray();
        bool layered = Layout is not null && Layout.Slots.Select(s => s.Layer).Distinct().Count() > 1;
        foreach (var i in indices)
        {
            if (Layout is null || i < 0 || i >= Layout.Slots.Count) continue;
            using var fill = new SolidBrush(Color.FromArgb(i == Selected ? 65 : 40, SlotColors[i % SlotColors.Length]));
            g.FillRectangle(fill, Map(Layout.Slots[i].Bounds));
        }
        foreach (var i in indices)
        {
            if (Layout is null || i < 0 || i >= Layout.Slots.Count) continue;
            var r = Map(Layout.Slots[i].Bounds); var color = SlotColors[i % SlotColors.Length];
            using var edge = new Pen(color, i == Selected ? 2.5f : 1.2f);
            g.DrawRectangle(edge, r.X, r.Y, r.Width, r.Height);
            if (i == Selected) { using var handle = new SolidBrush(color); g.FillRectangle(handle, r.Right - 9, r.Bottom - 9, 9, 9); }
        }
        // Name tags are drawn after every rectangle, so a client hidden behind another one
        // still shows its tag, and the tag works as a handle to select and drag that client.
        labelHandles.Clear();
        var placed = new List<RectangleF>();
        foreach (var i in indices)
        {
            if (Layout is null || i < 0 || i >= Layout.Slots.Count) continue;
            var s = Layout.Slots[i]; var r = Map(s.Bounds); var color = SlotColors[i % SlotColors.Length];
            var label = $"{i + 1:00}  {s.Name}"; float labelWidth = g.MeasureString(label, regular).Width;
            // The layer tag is shown only when layers are in use and it fits inside the client.
            string tag = $"LAYER {s.Layer}"; float tagWidth = g.MeasureString(tag, small).Width + 10;
            bool showTag = layered && labelWidth + tagWidth + 26 <= r.Width;
            var band = new RectangleF(r.X + 1, r.Y + 1, Math.Min(r.Width - 2, labelWidth + 22 + (showTag ? tagWidth : 0)), 30);
            // Step down past tags already placed in the same spot (for clients sharing a corner).
            for (int step = 0; step < 6 && placed.Any(p => p.IntersectsWith(band)) && band.Bottom + 36 <= r.Bottom; step++) band.Y += 36;
            placed.Add(band);
            var visible = RectangleF.Intersect(band, r);
            if (visible.Width < 4 || visible.Height < 4) continue;
            labelHandles.Add((i, visible));
            var clip = g.Save(); g.SetClip(r, CombineMode.Intersect);
            g.FillRectangle(tagBack, band);
            if (i == Selected) { using var mark = new SolidBrush(color); g.FillRectangle(mark, band.X, band.Bottom - 2, band.Width, 2); }
            if (showTag) g.DrawString(tag, small, tagText, r.X + 9 + labelWidth + 6, band.Y + 8);
            using var text = new SolidBrush(color);
            g.DrawString(label, regular, text, r.X + 9, band.Y + 6);
            g.Restore(clip);
        }
        foreach (var i in indices)
        {
            if (Layout is null || i < 0 || i >= Layout.Slots.Count) continue;
            var slot = Layout.Slots[i];
            if (!Assigned.TryGetValue(slot.Id, out var live) || live.Minimized || live.Bounds == slot.Bounds) continue;
            var actual = Map(live.Bounds);
            g.DrawRectangle(liveEdge, actual.X, actual.Y, actual.Width, actual.Height);
        }
        g.DrawRectangle(framePen, 0, 0, Width - 1, Height - 1);
        g.DrawString(DisplayEditMode ? "Zoom stays fixed · Fit view to show all displays · Ctrl disables snapping" : "Drag to move  ·  Bottom-right corner to resize  ·  Ctrl disables snapping", small, muted, 20, Height - 25);
    }
    // Snaps a dragged or resized client to the nearest guide line per direction. Only the single
    // closest candidate is used, so a far edge can never override a nearer one.
    internal static Rectangle Snap(Rectangle r, bool resizing, int[] edgesX, int[] edgesY, int threshold)
    {
        static int? Nearest(int[] edges, int threshold, params int[] points)
        {
            int? best = null;
            foreach (int edge in edges) foreach (int point in points)
                if (Math.Abs(edge - point) < threshold && (best is null || Math.Abs(edge - point) < Math.Abs(best.Value))) best = edge - point;
            return best;
        }
        if (resizing)
        {
            if (Nearest(edgesX, threshold, r.Right) is { } dw) r.Width = Math.Max(160, r.Width + dw);
            if (Nearest(edgesY, threshold, r.Bottom) is { } dh) r.Height = Math.Max(120, r.Height + dh);
        }
        else
        {
            if (Nearest(edgesX, threshold, r.Left, r.Right) is { } dx) r.X += dx;
            if (Nearest(edgesY, threshold, r.Top, r.Bottom) is { } dy) r.Y += dy;
        }
        return r;
    }
    private readonly List<(int Index, RectangleF Bounds)> labelHandles = new();
    private int LabelAt(Point p) { for (int k = labelHandles.Count - 1; k >= 0; k--) if (labelHandles[k].Bounds.Contains(p)) return labelHandles[k].Index; return -1; }
    private IEnumerable<int> DrawOrder() => Layout is null ? Enumerable.Empty<int>() :
        Enumerable.Range(0, Layout.Slots.Count).OrderBy(i => Layout.Slots[i].Layer).ThenBy(i => i == Selected).ThenBy(i => i);
    public static string DisplayNumber(MonitorInfo monitor, int fallback)
    {
        var digits = new string(monitor.Name.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? digits : (fallback + 1).ToString();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (DisplayEditMode)
        {
            if (e.Button != MouseButtons.Left) return;
            Transform();
            for (int i = Monitors.Count - 1; i >= 0; i--)
            {
                if (!Map(Monitors[i].Bounds).Contains(e.Location)) continue;
                SelectedMonitor = i; initial = Monitors[i].Bounds; dragStart = e.Location;
                draggingMonitor = true; Capture = true; MonitorSelectionChanged?.Invoke(); Invalidate(); break;
            }
            return;
        }
        if (e.Button == MouseButtons.Right && Layout is not null)
        {
            // Right-click selects the client under the pointer (its name tag first) and opens its menu.
            int hit = LabelAt(e.Location);
            if (hit < 0) hit = DrawOrder().Reverse().FirstOrDefault(i => Map(Layout.Slots[i].Bounds).Contains(e.Location), -1);
            if (hit < 0) return;
            Selected = hit; SelectionChanged?.Invoke(); Invalidate(); ClientContextRequested?.Invoke(e.Location); return;
        }
        if (e.Button != MouseButtons.Left || Layout is null) return;
        // A name tag always wins: it is the way to reach a client hidden behind another one.
        int tagged = LabelAt(e.Location);
        if (tagged >= 0 && tagged < Layout.Slots.Count)
        {
            Selected = tagged; initial = Layout.Slots[tagged].Bounds; dragStart = e.Location;
            resizing = false; dragging = true; Capture = true;
            SelectionChanged?.Invoke(); Invalidate(); return;
        }
        var order = DrawOrder().Reverse(); // front-most first, matching what is drawn on top
        foreach (var i in order)
        {
            if (i < 0 || i >= Layout.Slots.Count) continue;
            var r = Map(Layout.Slots[i].Bounds);
            if (!r.Contains(e.Location)) continue;
            Selected = i; initial = Layout.Slots[i].Bounds; dragStart = e.Location;
            resizing = e.X >= r.Right - 16 && e.Y >= r.Bottom - 16; dragging = true; Capture = true;
            SelectionChanged?.Invoke(); Invalidate(); break;
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (DisplayEditMode)
        {
            if (SelectedMonitor < 0 || SelectedMonitor >= Monitors.Count) return;
            if (!draggingMonitor) { Cursor = Monitors.Any(m => Map(m.Bounds).Contains(e.Location)) ? Cursors.SizeAll : Cursors.Default; return; }
            int moveX = (int)Math.Round((e.X - dragStart.X) / scale), moveY = (int)Math.Round((e.Y - dragStart.Y) / scale);
            var proposed = new Rectangle(Math.Clamp(initial.X + moveX, -64000, 64000), Math.Clamp(initial.Y + moveY, -64000, 64000), initial.Width, initial.Height);
            if ((ModifierKeys & Keys.Control) == 0) proposed = DisplayArrangement.Snap(proposed, Monitors.Where((_, i) => i != SelectedMonitor).Select(m => m.Bounds), (int)(10 / scale));
            Monitors[SelectedMonitor] = DisplayArrangement.Move(Monitors[SelectedMonitor], proposed.Location);
            MonitorsEdited?.Invoke(); Invalidate(); return;
        }
        if (Layout is null || Selected >= Layout.Slots.Count) return;
        var s = Layout.Slots[Selected];
        if (!dragging)
        {
            var r = Map(s.Bounds);
            Cursor = LabelAt(e.Location) >= 0 ? Cursors.Hand : r.Contains(e.Location) && e.X > r.Right - 16 && e.Y > r.Bottom - 16 ? Cursors.SizeNWSE : r.Contains(e.Location) ? Cursors.SizeAll : Cursors.Default;
            return;
        }
        int dx = (int)Math.Round((e.X - dragStart.X) / scale), dy = (int)Math.Round((e.Y - dragStart.Y) / scale);
        var next = resizing ? new Rectangle(initial.X, initial.Y, Math.Clamp(initial.Width + dx, 160, 32000), Math.Clamp(initial.Height + dy, 120, 32000)) : new Rectangle(initial.X + dx, initial.Y + dy, initial.Width, initial.Height);
        if ((ModifierKeys & Keys.Control) == 0)
        {
            int threshold = (int)(8 / scale);
            var areas = Monitors.Select(m => ClientPlacement.Area(m, s.WindowMode)).ToArray();
            // Display edges and middles, plus the edges of the other clients (for placing one inside another).
            var others = Layout.Slots.Where((_, k) => k != Selected).Select(o => o.Bounds).ToArray();
            var edgesX = areas.SelectMany(a => new[] { a.Left, a.Right, a.Left + a.Width / 2 }).Concat(others.SelectMany(o => new[] { o.Left, o.Right }));
            var edgesY = areas.SelectMany(a => new[] { a.Top, a.Bottom, a.Top + a.Height / 2 }).Concat(others.SelectMany(o => new[] { o.Top, o.Bottom }));
            next = Snap(next, resizing, edgesX.ToArray(), edgesY.ToArray(), threshold);
        }
        s.SetBounds(next); LayoutEdited?.Invoke(); Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; draggingMonitor = false; Capture = false; Invalidate(); }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) { dragging = false; draggingMonitor = false; Invalidate(); } }
}
