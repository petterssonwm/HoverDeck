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
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); Transform();
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var gridPen = new Pen(Color.FromArgb(20, 24, 33));
        for (int x = 0; x < Width; x += 24) g.DrawLine(gridPen, x, 0, x, Height);
        for (int y = 0; y < Height; y += 24) g.DrawLine(gridPen, 0, y, Width, y);
        using var small = new Font("Segoe UI", 9);
        using var regular = new Font("Segoe UI", 10, FontStyle.Bold);
        using var numberFont = new Font("Segoe UI", 21, FontStyle.Bold);
        using var muted = new SolidBrush(Color.FromArgb(111, 119, 135));
        using var number = new SolidBrush(Color.FromArgb(74, 82, 98));
        using var taskbarHatch = new HatchBrush(HatchStyle.ForwardDiagonal, Color.FromArgb(144, 119, 77), Color.FromArgb(67, 59, 44));
        using var taskbarPen = new Pen(TaskbarColor, 1);
        if (Monitors.Count == 0) { g.DrawString("No displays detected", regular, Brushes.White, 30, 60); return; }
        var covered = !DisplayEditMode && Layout is not null ? Layout.Slots.Where(s => s.WindowMode == ClientWindowMode.FullscreenWindowed).Select(s => s.Bounds).ToArray() : Array.Empty<Rectangle>();
        for (int i = 0; i < Monitors.Count; i++)
        {
            var m = Monitors[i]; var r = Map(m.Bounds);
            using var fill = new SolidBrush(Color.FromArgb(22, 26, 34));
            using var border = new Pen(DisplayEditMode && i == SelectedMonitor ? AppTheme.Blue : m.Primary ? Color.FromArgb(59, 90, 128) : Color.FromArgb(44, 50, 61), DisplayEditMode && i == SelectedMonitor ? 3 : 2);
            g.FillRectangle(fill, r); g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height);
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
        var indices = Layout is null || DisplayEditMode ? Array.Empty<int>() : Enumerable.Range(0, Layout.Slots.Count).Where(i => i != Selected).Append(Selected).Distinct().ToArray();
        foreach (var i in indices)
        {
            if (Layout is null || i < 0 || i >= Layout.Slots.Count) continue;
            using var fill = new SolidBrush(Color.FromArgb(i == Selected ? 65 : 40, SlotColors[i % SlotColors.Length]));
            g.FillRectangle(fill, Map(Layout.Slots[i].Bounds));
        }
        foreach (var i in indices)
        {
            if (Layout is null || i < 0 || i >= Layout.Slots.Count) continue;
            var s = Layout.Slots[i]; var r = Map(s.Bounds); var color = SlotColors[i % SlotColors.Length];
            using var edge = new Pen(color, i == Selected ? 2.5f : 1.2f);
            g.DrawRectangle(edge, r.X, r.Y, r.Width, r.Height);
            var clip = g.Save(); g.SetClip(r, CombineMode.Intersect);
            float labelY = r.Y;
            if (i != Selected && Selected >= 0 && Selected < Layout.Slots.Count)
            {
                var selectedRect = Map(Layout.Slots[Selected].Bounds);
                if (new RectangleF(r.X, r.Y, r.Width, 32).IntersectsWith(new RectangleF(selectedRect.X, selectedRect.Y, selectedRect.Width, 32))) labelY += 36;
            }
            using var band = new SolidBrush(Color.FromArgb(15, 19, 26));
            var label = $"{i + 1:00}  {s.Name}";
            g.FillRectangle(band, r.X + 1, labelY + 1, Math.Min(r.Width - 2, g.MeasureString(label, regular).Width + 22), 30);
            using var text = new SolidBrush(color);
            g.DrawString(label, regular, text, r.X + 9, labelY + 7);
            g.Restore(clip);
            if (i == Selected) { using var handle = new SolidBrush(color); g.FillRectangle(handle, r.Right - 9, r.Bottom - 9, 9, 9); }
        }
        foreach (var i in indices)
        {
            if (Layout is null || i < 0 || i >= Layout.Slots.Count) continue;
            var slot = Layout.Slots[i];
            if (!Assigned.TryGetValue(slot.Id, out var live) || live.Minimized || live.Bounds == slot.Bounds) continue;
            var actual = Map(live.Bounds);
            using var liveEdge = new Pen(Color.FromArgb(235, 241, 250), 1) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(liveEdge, actual.X, actual.Y, actual.Width, actual.Height);
        }
        using (var frame = new Pen(AppTheme.Line)) g.DrawRectangle(frame, 0, 0, Width - 1, Height - 1);
        g.DrawString(DisplayEditMode ? "Zoom stays fixed · Fit view to show all displays · Ctrl disables snapping" : "Drag to move  ·  Bottom-right corner to resize  ·  Ctrl disables snapping", small, muted, 20, Height - 25);
    }
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
        if (e.Button != MouseButtons.Left || Layout is null) return;
        var order = new[] { Selected }.Concat(Enumerable.Range(0, Layout.Slots.Count).Reverse()).Distinct();
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
            Cursor = r.Contains(e.Location) && e.X > r.Right - 16 && e.Y > r.Bottom - 16 ? Cursors.SizeNWSE : r.Contains(e.Location) ? Cursors.SizeAll : Cursors.Default;
            return;
        }
        int dx = (int)Math.Round((e.X - dragStart.X) / scale), dy = (int)Math.Round((e.Y - dragStart.Y) / scale);
        var next = resizing ? new Rectangle(initial.X, initial.Y, Math.Clamp(initial.Width + dx, 160, 32000), Math.Clamp(initial.Height + dy, 120, 32000)) : new Rectangle(initial.X + dx, initial.Y + dy, initial.Width, initial.Height);
        if ((ModifierKeys & Keys.Control) == 0)
        {
            int threshold = (int)(8 / scale);
            var areas = Monitors.Select(m => ClientPlacement.Area(m, s.WindowMode)).ToArray();
            var edgesX = areas.SelectMany(a => new[] { a.Left, a.Right, a.Left + a.Width / 2 });
            var edgesY = areas.SelectMany(a => new[] { a.Top, a.Bottom, a.Top + a.Height / 2 });
            foreach (int x in edgesX) { if (resizing && Math.Abs(next.Right - x) < threshold) next.Width = Math.Max(160, x - next.X); else if (!resizing) { if (Math.Abs(next.Left - x) < threshold) next.X = x; else if (Math.Abs(next.Right - x) < threshold) next.X = x - next.Width; } }
            foreach (int y in edgesY) { if (resizing && Math.Abs(next.Bottom - y) < threshold) next.Height = Math.Max(120, y - next.Y); else if (!resizing) { if (Math.Abs(next.Top - y) < threshold) next.Y = y; else if (Math.Abs(next.Bottom - y) < threshold) next.Y = y - next.Height; } }
        }
        s.SetBounds(next); LayoutEdited?.Invoke(); Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; draggingMonitor = false; Capture = false; Invalidate(); }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) { dragging = false; draggingMonitor = false; Invalidate(); } }
}
