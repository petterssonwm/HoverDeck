using System.Drawing.Drawing2D;
using static HoverDeck.AppTheme;

namespace HoverDeck;

// Small owner-drawn controls for the launcher-style workspace. Behaviour stays
// with the standard WinForms base classes; only the painting is replaced.

internal sealed class CapsLabel : Control
{
    public float Tracking { get; set; } = 1.4f;
    public CapsLabel(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Text = text; BackColor = Color.Transparent; ForeColor = Caption; Font = new("Segoe UI", 8f, FontStyle.Bold); AutoSize = true;
    }
    public override Size GetPreferredSize(Size proposedSize) { var s = MeasureTracked(Text.ToUpperInvariant(), Font, Tracking); return new(s.Width + Padding.Horizontal + 2, s.Height + Padding.Vertical); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (AutoSize) Size = PreferredSize; Invalidate(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (AutoSize) Size = PreferredSize; }
    protected override void OnPaint(PaintEventArgs e) => DrawTracked(e.Graphics, Text.ToUpperInvariant(), Font, ForeColor, new Rectangle(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical), Tracking);
}

// Uppercase text link. AutoCheck turns it into a toggle (Arrange displays).
internal sealed class NavLink : CheckBox
{
    private bool hot;
    public NavLink(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        Text = text; AutoSize = false; BackColor = Color.Transparent; Font = new("Segoe UI", 9f, FontStyle.Bold); Cursor = Cursors.Hand; Margin = new(18, 0, 0, 0);
        Size = new(MeasureTracked(text.ToUpperInvariant(), Font, 1f).Width + 4, 30);
    }
    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        OnPaintBackground(e);
        var color = !Enabled ? Caption : Checked || hot ? Color.White : Color.FromArgb(154, 161, 176);
        DrawTracked(e.Graphics, Text.ToUpperInvariant(), Font, color, ClientRectangle, 1f);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
}

// The hover-focus switch at the top of the launch frame: a sliding switch, its state and the shortcut.
// The parent draws the frame around it and Start clients in FrameColor; this control paints the top
// part with rounded top corners and the divider above Start clients. On, everything turns blue.
internal sealed class HoverSwitch : CheckBox
{
    private static readonly Font DetailFont = new("Segoe UI", 8f);
    private string detail = "";
    public string Detail { get => detail; set { detail = value; Invalidate(); } }
    public Color FrameColor => Checked && Enabled ? Blue : FieldBorder;
    public HoverSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AutoSize = false; Font = new("Segoe UI", 8.5f, FontStyle.Bold); Cursor = Cursors.Hand; Height = 48;
    }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); Parent?.Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); Parent?.Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Rail); g.SmoothingMode = SmoothingMode.AntiAlias;
        bool on = Checked && Enabled;
        // Rounded at the top only: the shape runs past the bottom edge, so the bottom corners stay square.
        using (var shape = Round(new RectangleF(0, 0, Width, Height + 8), 5))
        using (var fill = new SolidBrush(on ? Color.FromArgb(14, 39, 66) : Surface)) g.FillPath(fill, shape);
        using (var divider = new Pen(FrameColor)) g.DrawLine(divider, 0, Height - 1, Width, Height - 1);
        var track = new RectangleF(12, (Height - 20) / 2f, 36, 20);
        using (var trackShape = Round(track, 10))
        using (var trackFill = new SolidBrush(on ? Blue : Color.FromArgb(44, 49, 59))) g.FillPath(trackFill, trackShape);
        using (var knob = new SolidBrush(!Enabled ? Color.FromArgb(70, 75, 84) : on ? Color.White : Color.FromArgb(138, 145, 160)))
            g.FillEllipse(knob, on ? track.Right - 17 : track.X + 3, track.Y + 3, 14, 14);
        int x = (int)track.Right + 12;
        DrawTracked(g, Text.ToUpperInvariant(), Font, !Enabled ? Muted : on ? Color.White : Color.FromArgb(201, 206, 216), new Rectangle(x, 7, Width - x - 8, 18), .7f);
        TextRenderer.DrawText(g, Detail, DetailFont, new Rectangle(x, 26, Width - x - 8, 16), on ? Color.FromArgb(143, 198, 255) : Muted, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -3, -3));
    }
}

// The large blue launcher button. With ShowArrow it becomes a split button: the
// right-hand segment raises ArrowClick (for a menu) instead of Click.
internal sealed class BlueButton : Button
{
    private const int ArrowWidth = 46;
    private bool hot, down, overArrow, pressedArrow;
    public bool ShowArrow { get; set; }
    // Square top corners, for a button that sits directly under another part of the same frame.
    public bool SquareTop { get; set; }
    public event Action? ArrowClick;
    public BlueButton(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Text = text; Font = new("Segoe UI", 11.5f, FontStyle.Bold); Cursor = Cursors.Hand; FlatStyle = FlatStyle.Flat; UseVisualStyleBackColor = false;
    }
    private bool InArrow(int x) => ShowArrow && x >= Width - ArrowWidth;
    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = down = overArrow = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseMove(MouseEventArgs e) { bool arrow = InArrow(e.X); if (arrow != overArrow) { overArrow = arrow; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; pressedArrow = InArrow(e.X); Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (ShowArrow && (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up || e.KeyCode == Keys.F4 || e.KeyCode == Keys.Apps)) { e.Handled = true; ArrowClick?.Invoke(); return; }
        base.OnKeyDown(e);
    }
    protected override void OnClick(EventArgs e)
    {
        // Mouse clicks on the arrow open the menu; Enter/Space and clicks elsewhere start clients.
        if (pressedArrow && e is MouseEventArgs) { pressedArrow = false; ArrowClick?.Invoke(); return; }
        pressedArrow = false; base.OnClick(e);
    }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    private static (Color Top, Color Bottom) Fill(bool enabled, bool down, bool hot) =>
        !enabled ? (Color.FromArgb(38, 52, 70), Color.FromArgb(30, 42, 58)) : down ? (Color.FromArgb(12, 108, 210), Color.FromArgb(8, 90, 180)) : hot ? (Color.FromArgb(64, 170, 255), Color.FromArgb(22, 130, 240)) : (Color.FromArgb(42, 155, 255), BlueDark);
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Rail); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = SquareTop ? Round(new RectangleF(0, -8, Width - 1, Height + 7), 5) : Round(new RectangleF(0, 0, Width - 1, Height - 1), 5);
        var main = Fill(Enabled, down && !pressedArrow, hot && !overArrow);
        using (var fill = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), main.Top, main.Bottom, 90f)) g.FillPath(fill, path);
        int split = ShowArrow ? Width - ArrowWidth : Width;
        if (ShowArrow)
        {
            var arrow = Fill(Enabled, down && pressedArrow, hot && overArrow);
            var state = g.Save(); g.SetClip(new Rectangle(split, 0, ArrowWidth, Height));
            using (var fill = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), arrow.Top, arrow.Bottom, 90f)) g.FillPath(fill, path);
            g.Restore(state);
            using var divider = new Pen(Enabled ? Color.FromArgb(9, 86, 166) : Color.FromArgb(24, 34, 48)); g.DrawLine(divider, split, 6, split, Height - 7);
            float cx = split + ArrowWidth / 2f, cy = Height / 2f;
            using var glyph = new SolidBrush(Enabled ? Color.White : Color.FromArgb(120, 140, 165));
            g.FillPolygon(glyph, new PointF[] { new(cx - 5, cy + 2.5f), new(cx + 5, cy + 2.5f), new(cx, cy - 3) });
        }
        if (Enabled) { using var shine = new Pen(Color.FromArgb(110, 188, 255)); g.DrawLine(shine, 5, 0.5f, Width - 6, 0.5f); }
        DrawTracked(g, Text.ToUpperInvariant(), Font, Enabled ? Color.White : Color.FromArgb(120, 140, 165), new Rectangle(0, 0, split, Height), 1.6f, true);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4));
    }
}

internal enum CaptionAction { Minimize, Maximize, Close }

// Minimize / maximize / close buttons for the borderless main window.
internal sealed class CaptionButton : Control
{
    private bool hot;
    public CaptionAction Action { get; }
    public CaptionButton(CaptionAction action)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        SetStyle(ControlStyles.Selectable, false);
        Action = action; Width = 46; BackColor = Bar; AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = action switch { CaptionAction.Minimize => "Minimize", CaptionAction.Maximize => "Maximize", _ => "Close" };
    }
    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(hot ? Action == CaptionAction.Close ? Color.FromArgb(196, 43, 28) : Color.FromArgb(30, 33, 41) : BackColor);
        using var pen = new Pen(hot ? Color.White : Muted, 1f);
        int cx = Width / 2, cy = Height / 2;
        switch (Action)
        {
            case CaptionAction.Minimize: g.DrawLine(pen, cx - 5, cy, cx + 5, cy); break;
            case CaptionAction.Maximize when FindForm()?.WindowState == FormWindowState.Maximized:
                g.DrawRectangle(pen, cx - 5, cy - 3, 8, 8); g.DrawLines(pen, new[] { new Point(cx - 3, cy - 3), new Point(cx - 3, cy - 5), new Point(cx + 5, cy - 5), new Point(cx + 5, cy + 3), new Point(cx + 3, cy + 3) }); break;
            case CaptionAction.Maximize: g.DrawRectangle(pen, cx - 5, cy - 5, 10, 10); break;
            default: g.SmoothingMode = SmoothingMode.AntiAlias; g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5); g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5); break;
        }
    }
}

internal sealed class LauncherLogo : Control
{
    public LauncherLogo()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Bar; Font = new("Segoe UI", 11f, FontStyle.Bold); Width = 178;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
        var mark = new Rectangle(0, (Height - 32) / 2, 32, 32);
        if (AppTheme.Logo is { } logo) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(logo, mark); }
        DrawTracked(g, "HOVERDECK", Font, Ink, new Rectangle(42, 0, Width - 42, Height), 1f);
    }
}

// Flat dark drop-down list. The closed box is fully painted here; the open list is owner-drawn.
internal sealed class DarkComboBox : ComboBox
{
    public DarkComboBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DropDownStyle = ComboBoxStyle.DropDownList; FlatStyle = FlatStyle.Flat; DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 24;
        BackColor = Field; ForeColor = Ink; Font = new("Segoe UI", 9.75f);
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) => PaintBox(e.Graphics);
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var brush = new SolidBrush(selected ? Selected : Field); e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, Rectangle.Inflate(e.Bounds, -8, 0), Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    private void PaintBox(Graphics g)
    {
        var r = ClientRectangle; bool focused = Focused || DroppedDown;
        using var bg = new SolidBrush(Enabled ? Field : Color.FromArgb(19, 21, 26)); g.FillRectangle(bg, r);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(9, 0, r.Width - 34, r.Height), Enabled ? Ink : Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        int ax = r.Right - 17, ay = r.Height / 2 - 2; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var arrow = new SolidBrush(Muted); g.FillPolygon(arrow, new PointF[] { new(ax - 4, ay), new(ax + 4, ay), new(ax, ay + 5) });
        using var edge = new Pen(focused ? Blue : FieldBorder); g.SmoothingMode = SmoothingMode.None; g.DrawRectangle(edge, 0, 0, r.Width - 1, r.Height - 1);
    }
}

// One-pixel bordered host for text-like inputs (number fields).
internal sealed class FieldBox : Panel
{
    public FieldBox(Control inner, int width)
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Field; Size = new(width, 30); Padding = new(9, 6, 6, 0); Margin = new(0);
        inner.BackColor = Field; inner.ForeColor = Ink;
        if (inner is NumericUpDown number)
        {
            // Hidden spin buttons still reserve (and smear) their strip, so the field is made
            // wider than an inner clip area that stops inside this box's 1px border.
            number.BorderStyle = BorderStyle.None; number.Controls[0].Visible = false;
            number.Enter += (_, _) => Invalidate(); number.Leave += (_, _) => Invalidate();
            Controls.Add(clip); clip.Controls.Add(number);
            Resize += (_, _) => PlaceNumber(number); PaddingChanged += (_, _) => PlaceNumber(number); PlaceNumber(number);
        }
        else { inner.Dock = DockStyle.Fill; Controls.Add(inner); }
    }
    private readonly Panel clip = new() { BackColor = Field, Margin = Padding.Empty };
    private void PlaceNumber(NumericUpDown number)
    {
        clip.Bounds = new(1, 1, Math.Max(4, Width - 2), Math.Max(4, Height - 2));
        int left = Math.Max(0, Padding.Left - 1);
        number.Location = new(left, Math.Max(0, Padding.Top - 1)); number.Width = clip.Width - left + number.Controls[0].Width;
        number.Height = Math.Max(10, clip.Height - number.Top);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(ContainsFocus ? Blue : FieldBorder); e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
