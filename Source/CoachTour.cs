using System.Drawing.Drawing2D;

namespace HoverDeck;

// Where a tour bubble sits relative to the control it explains; its arrow points back at the control.
internal enum CoachSide { Above, Below, Left, Right, Inside }
internal sealed record CoachStep(Control Target, string Title, string Text, CoachSide Side);

// A short tour of the main window, like a game launcher's first-start hints: one bubble at a time
// pointing at a part of the window, with Next and Skip tour. Bubbles follow the window if it moves.
internal sealed class CoachTour
{
    private readonly Form owner;
    private readonly IReadOnlyList<CoachStep> steps;
    private CoachBubble? bubble;
    private int index = -1;
    public event Action? Finished;
    public CoachBubble? Current => bubble;
    public CoachTour(Form owner, IReadOnlyList<CoachStep> steps) { this.owner = owner; this.steps = steps; }
    public void Start() { owner.Move += Reposition; owner.Resize += Reposition; Show(0); }
    private void Show(int step)
    {
        var old = bubble; bubble = null; old?.Close(); old?.Dispose();
        if (step >= steps.Count || owner.IsDisposed) { End(); return; }
        index = step; var s = steps[step];
        bubble = new CoachBubble(s.Title, s.Text, step + 1, steps.Count);
        bubble.Next += () => Show(index + 1); bubble.Skip += End;
        Place(); bubble.Show(owner);
    }
    private void Reposition(object? sender, EventArgs e) { if (bubble is not null) Place(); }
    private void Place()
    {
        if (bubble is null) return;
        var s = steps[index]; var target = s.Target.RectangleToScreen(s.Target.ClientRectangle);
        // Bubbles stay inside HoverDeck's window (and on screen), so none hangs over other programs.
        var screen = Screen.FromControl(owner).WorkingArea; var window = Rectangle.Intersect(screen, owner.RectangleToScreen(owner.ClientRectangle));
        var area = window.Width > 400 && window.Height > 300 ? window : screen; var size = bubble.BubbleSize(s.Side);
        int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));
        var location = s.Side switch
        {
            CoachSide.Above => new Point(target.Left + target.Width / 2 - size.Width / 2, target.Top - size.Height),
            CoachSide.Below => new Point(target.Left + target.Width / 2 - size.Width / 2, target.Bottom),
            CoachSide.Left => new Point(target.Left - size.Width, target.Top + target.Height / 2 - size.Height / 2),
            CoachSide.Right => new Point(target.Right, target.Top + target.Height / 2 - size.Height / 2),
            _ => new Point(target.Left + target.Width / 2 - size.Width / 2, target.Top + target.Height / 2 - size.Height / 2)
        };
        location = new(Clamp(location.X, area.Left, area.Right - size.Width), Clamp(location.Y, area.Top, area.Bottom - size.Height));
        // The arrow points at the middle of the target, even when the bubble had to move to stay on screen.
        int along = s.Side is CoachSide.Above or CoachSide.Below ? target.Left + target.Width / 2 - location.X : target.Top + target.Height / 2 - location.Y;
        bubble.Arrange(s.Side, along, new Rectangle(location, size));
    }
    private void End()
    {
        owner.Move -= Reposition; owner.Resize -= Reposition;
        var old = bubble; bubble = null; old?.Close(); old?.Dispose();
        Finished?.Invoke(); Finished = null;
    }
}

internal sealed class CoachBubble : Form
{
    private const int ArrowSize = 11, Radius = 8, BodyWidth = 330;
    private static readonly Font TitleFont = new("Segoe UI", 11f, FontStyle.Bold), TextFont = new("Segoe UI", 9.5f), SmallFont = new("Segoe UI", 8.5f);
    private readonly Label title = new() { AutoSize = false, ForeColor = Color.White, Font = TitleFont, BackColor = Color.Transparent };
    private readonly Label text = new() { AutoSize = false, ForeColor = Color.FromArgb(201, 206, 216), Font = TextFont, BackColor = Color.Transparent };
    private readonly Label counter = new() { AutoSize = true, ForeColor = AppTheme.Caption, Font = SmallFont, BackColor = Color.Transparent };
    private readonly Button skip = new() { Text = "Skip tour", Name = "CoachSkip", Width = 90, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = AppTheme.Muted, BackColor = AppTheme.Surface, Cursor = Cursors.Hand };
    private readonly Button next = new() { Name = "CoachNext", Width = 90, Height = 30, Cursor = Cursors.Hand };
    private CoachSide side = CoachSide.Inside;
    private int along;
    private readonly int bodyHeight;
    public event Action? Next, Skip;
    public string Title => title.Text;
    protected override bool ShowWithoutActivation => true;
    public CoachBubble(string heading, string body, int step, int steps)
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; BackColor = AppTheme.Surface; Font = TextFont;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        title.Text = heading; text.Text = body; counter.Text = $"{step} of {steps}";
        next.Text = step == steps ? "Got it" : "Next"; AppTheme.Primary(next);
        skip.FlatAppearance.BorderSize = 0; skip.FlatAppearance.MouseOverBackColor = AppTheme.ButtonHover; skip.Visible = step < steps;
        int textHeight = TextRenderer.MeasureText(body, TextFont, new Size(BodyWidth - 36, 0), TextFormatFlags.WordBreak).Height;
        bodyHeight = 16 + 24 + 6 + textHeight + 16 + 30 + 14;
        Controls.AddRange(new Control[] { title, text, counter, skip, next });
        next.Click += (_, _) => Next?.Invoke(); skip.Click += (_, _) => Skip?.Invoke();
        title.Size = new(BodyWidth - 36, 24); text.Size = new(BodyWidth - 36, textHeight);
    }
    public Size BubbleSize(CoachSide where) => where switch
    {
        CoachSide.Above or CoachSide.Below => new(BodyWidth, bodyHeight + ArrowSize),
        CoachSide.Left or CoachSide.Right => new(BodyWidth + ArrowSize, bodyHeight),
        _ => new(BodyWidth, bodyHeight)
    };
    // Lays the bubble out with its arrow on the side facing the target, 'along' pixels from its left or top edge.
    public void Arrange(CoachSide where, int alongEdge, Rectangle bounds)
    {
        side = where; along = alongEdge; Bounds = bounds;
        var body = Body();
        title.Location = new(body.X + 18, body.Y + 16); text.Location = new(body.X + 18, body.Y + 16 + 24 + 6);
        int buttons = body.Bottom - 14 - 30;
        next.Location = new(body.Right - 18 - next.Width, buttons); skip.Location = new(next.Left - 8 - skip.Width, buttons);
        counter.Location = new(body.X + 18, buttons + 7);
        using var shape = Shape(); var old = Region; Region = new Region(shape); old?.Dispose();
        Invalidate();
    }
    private Rectangle Body() => side switch
    {
        CoachSide.Above => new(0, 0, Width, Height - ArrowSize),
        CoachSide.Below => new(0, ArrowSize, Width, Height - ArrowSize),
        CoachSide.Left => new(0, 0, Width - ArrowSize, Height),
        CoachSide.Right => new(ArrowSize, 0, Width - ArrowSize, Height),
        _ => new(0, 0, Width, Height)
    };
    private RectangleF BodyShape(float inset)
    {
        var body = Body(); return new(body.X + inset, body.Y + inset, body.Width - 1 - inset * 2, body.Height - 1 - inset * 2);
    }
    // The arrow: its base reaches 2 px into the body, so painting it covers the body's border there.
    private PointF[] Tip(RectangleF r)
    {
        float a = Math.Clamp(along, Radius + ArrowSize + 2, (side is CoachSide.Above or CoachSide.Below ? Width : Height) - Radius - ArrowSize - 2);
        return side switch
        {
            CoachSide.Above => new PointF[] { new(a - ArrowSize, r.Bottom - 2), new(a, Height - 1), new(a + ArrowSize, r.Bottom - 2) },
            CoachSide.Below => new PointF[] { new(a - ArrowSize, r.Top + 2), new(a, 0), new(a + ArrowSize, r.Top + 2) },
            CoachSide.Left => new PointF[] { new(r.Right - 2, a - ArrowSize), new(Width - 1, a), new(r.Right - 2, a + ArrowSize) },
            _ => new PointF[] { new(r.Left + 2, a - ArrowSize), new(0, a), new(r.Left + 2, a + ArrowSize) }
        };
    }
    private GraphicsPath Shape()
    {
        var r = BodyShape(0); var path = AppTheme.Round(r, Radius);
        if (side != CoachSide.Inside) { path.AddPolygon(Tip(r)); path.FillMode = FillMode.Winding; }
        return path;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(AppTheme.Surface);
        var r = BodyShape(.75f);
        using var pen = new Pen(AppTheme.Blue, 1.5f); using var fill = new SolidBrush(AppTheme.Surface);
        using (var body = AppTheme.Round(r, Radius)) { g.FillPath(fill, body); g.DrawPath(pen, body); }
        if (side == CoachSide.Inside) return;
        var tip = Tip(r); g.FillPolygon(fill, tip); g.DrawLine(pen, tip[0], tip[1]); g.DrawLine(pen, tip[1], tip[2]);
    }
}
