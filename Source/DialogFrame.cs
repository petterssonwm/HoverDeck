using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using static HoverDeck.AppTheme;

namespace HoverDeck;

// Gives any dialog HoverDeck's own frame instead of the Windows title bar: a dark top bar with the
// app icon, the title and a close button. The dialog keeps its layout; the frame is added around it
// when it loads. Resizable dialogs stay resizable from their edges.
internal static class DialogFrame
{
    public const int TitleHeight = 40;
    private static readonly ConditionalWeakTable<Form, object> framed = new();

    public static void Apply(Form form)
    {
        if (framed.TryGetValue(form, out _)) return;
        framed.Add(form, new object());
        form.Load += (_, _) => Frame(form);
    }

    private static void Frame(Form form)
    {
        bool sizable = form.FormBorderStyle is FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow;
        int edge = sizable ? 5 : 1;
        var client = form.ClientSize; var chrome = form.Size - form.ClientSize; var minimum = form.MinimumSize;
        Polish(form);
        form.SuspendLayout();
        form.FormBorderStyle = FormBorderStyle.None;
        form.Icon = AppIcon;
        foreach (Control child in form.Controls) if (child.Dock == DockStyle.None) child.Location = new(child.Left + edge, child.Top + TitleHeight);
        form.Padding = new(form.Padding.Left + edge, form.Padding.Top + TitleHeight, form.Padding.Right + edge, form.Padding.Bottom + edge);
        form.ClientSize = new(client.Width + 2 * edge, client.Height + TitleHeight + edge);
        if (!minimum.IsEmpty) form.MinimumSize = new(minimum.Width - chrome.Width + 2 * edge, minimum.Height - chrome.Height + TitleHeight + edge);
        var bar = new DialogTitleBar(form) { Bounds = new(1, 1, form.ClientSize.Width - 2, TitleHeight - 1), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        form.Controls.Add(bar); bar.BringToFront();
        form.Paint += (_, e) => { using var line = new Pen(ButtonBorder); e.Graphics.DrawRectangle(line, 0, 0, form.Width - 1, form.Height - 1); };
        form.Resize += (_, _) => form.Invalidate();
        form.ResumeLayout(true);
        // The new size changes where a centred dialog belongs.
        var anchor = form.Owner is { } owner ? owner.Bounds : Screen.FromPoint(Cursor.Position).WorkingArea;
        if (form.StartPosition is FormStartPosition.CenterParent or FormStartPosition.CenterScreen)
            form.Location = new(anchor.X + (anchor.Width - form.Width) / 2, Math.Max(anchor.Y, anchor.Y + (anchor.Height - form.Height) / 2));
        _ = new FrameHitTest(form, bar, sizable ? edge : 0);
    }

    // Lets the dialog be dragged by its top bar and resized from its edges.
    private sealed class FrameHitTest : NativeWindow
    {
        private readonly Form form; private readonly DialogTitleBar bar; private readonly int edge;
        public FrameHitTest(Form form, DialogTitleBar bar, int edge)
        {
            this.form = form; this.bar = bar; this.edge = edge;
            form.HandleCreated += (_, _) => AssignHandle(form.Handle); form.HandleDestroyed += (_, _) => ReleaseHandle();
            if (form.IsHandleCreated) AssignHandle(form.Handle);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) // WM_NCHITTEST
            {
                long value = m.LParam.ToInt64(); var p = form.PointToClient(new((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF)));
                if (edge > 0 && form.WindowState == FormWindowState.Normal)
                {
                    bool left = p.X < edge, right = p.X >= form.ClientSize.Width - edge, top = p.Y < edge, bottom = p.Y >= form.ClientSize.Height - edge;
                    int hit = top ? left ? 13 : right ? 14 : 12 : bottom ? left ? 16 : right ? 17 : 15 : left ? 10 : right ? 11 : 0;
                    if (hit != 0) { m.Result = hit; return; }
                }
                if (bar.Bounds.Contains(p)) { m.Result = 2; return; } // HTCAPTION
            }
            base.WndProc(ref m);
        }
    }
}

internal sealed class DialogTitleBar : Control
{
    private readonly Form form;
    private readonly CaptionButton close = new(CaptionAction.Close) { Dock = DockStyle.Right, Width = 46 };
    public DialogTitleBar(Form form)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        this.form = form; BackColor = Bar; Font = new("Segoe UI", 10f, FontStyle.Bold);
        close.BackColor = Bar; close.Click += (_, _) => form.Close(); Controls.Add(close);
        form.TextChanged += (_, _) => Invalidate();
    }
    // Clicks on the bar itself (not the close button) fall through to the dialog, which drags it.
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084) { m.Result = -1; return; } // HTTRANSPARENT
        base.WndProc(ref m);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor);
        if (Logo is { } logo) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(logo, new Rectangle(14, (Height - 22) / 2, 22, 22)); }
        TextRenderer.DrawText(g, form.Text, Font, new Rectangle(46, 0, Width - 46 - close.Width - 8, Height), Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using var line = new Pen(Line); g.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }
}

// HoverDeck's own message box: same calls as MessageBox.Show, drawn in the app's style.
internal static class ThemedMessageBox
{
    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None) => Show(null, text, caption, buttons, icon);
    public static DialogResult Show(IWin32Window? owner, string text) => Show(owner, text, "HoverDeck");
    public static DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        var choices = buttons switch
        {
            MessageBoxButtons.YesNo => new[] { ("Yes", DialogResult.Yes), ("No", DialogResult.No) },
            MessageBoxButtons.YesNoCancel => new[] { ("Yes", DialogResult.Yes), ("No", DialogResult.No), ("Cancel", DialogResult.Cancel) },
            MessageBoxButtons.OKCancel => new[] { ("OK", DialogResult.OK), ("Cancel", DialogResult.Cancel) },
            MessageBoxButtons.RetryCancel => new[] { ("Retry", DialogResult.Retry), ("Cancel", DialogResult.Cancel) },
            _ => new[] { ("OK", DialogResult.OK) },
        };
        using var dialog = new Form { Text = caption, StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = owner is null, TopMost = owner is null, KeyPreview = true, Font = new("Segoe UI", 10f), BackColor = Surface, ForeColor = Ink };
        bool hasIcon = icon != MessageBoxIcon.None; int textLeft = hasIcon ? 72 : 24;
        var message = new Label { Text = text, AutoSize = true, MaximumSize = new(440, 0), Location = new(textLeft, 22), ForeColor = Ink, BackColor = Color.Transparent, UseMnemonic = false };
        dialog.Controls.Add(message);
        var size = message.GetPreferredSize(new(440, 0));
        int width = Math.Max(360, textLeft + size.Width + 28), bodyHeight = Math.Max(hasIcon ? 44 : 0, size.Height);
        if (hasIcon)
        {
            var glyph = new Panel { Bounds = new(24, 20, 34, 34), BackColor = Surface };
            var (mark, color) = icon switch { MessageBoxIcon.Warning => ("!", Color.FromArgb(237, 185, 109)), MessageBoxIcon.Error => ("×", Color.FromArgb(255, 128, 118)), MessageBoxIcon.Question => ("?", BlueText), _ => ("i", BlueText) };
            glyph.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var ring = new Pen(color, 2f); e.Graphics.DrawEllipse(ring, 2, 2, 29, 29);
                using var markFont = new Font("Segoe UI", 13f, FontStyle.Bold); TextRenderer.DrawText(e.Graphics, mark, markFont, new Rectangle(1, 0, 33, 34), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            dialog.Controls.Add(glyph);
        }
        int buttonTop = 22 + bodyHeight + 24, x = width - 20;
        int defaultIndex = Math.Min(choices.Length - 1, defaultButton switch { MessageBoxDefaultButton.Button2 => 1, MessageBoxDefaultButton.Button3 => 2, _ => 0 });
        for (int i = choices.Length - 1; i >= 0; i--)
        {
            var (label, result) = choices[i];
            var button = new Button { Text = label, DialogResult = result, Size = new(100, 34), FlatStyle = FlatStyle.Flat, BackColor = ButtonFace, ForeColor = Ink, Font = new("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = ButtonBorder; button.FlatAppearance.MouseOverBackColor = ButtonHover;
            if (i == defaultIndex) { Primary(button); dialog.AcceptButton = button; dialog.Shown += (_, _) => button.Focus(); }
            if (result is DialogResult.Cancel or DialogResult.No || choices.Length == 1) dialog.CancelButton = button;
            x -= button.Width; button.Location = new(x, buttonTop); x -= 8; dialog.Controls.Add(button);
        }
        dialog.ClientSize = new(width, buttonTop + 34 + 20);
        DialogFrame.Apply(dialog);
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }
}

// Tab strip drawn in the app's style (the standard one is always light).
internal sealed class DarkTabControl : TabControl
{
    // Only the tab captions are bold; the pages keep the dialog's normal font.
    private static readonly Font TabFont = new("Segoe UI", 9f, FontStyle.Bold);
    public DarkTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SizeMode = TabSizeMode.Fixed; ItemSize = new(130, 36);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Surface);
        var page = DisplayRectangle; page.Inflate(4, 4);
        using (var fill = new SolidBrush(Surface)) g.FillRectangle(fill, page);
        using (var edge = new Pen(Line)) g.DrawRectangle(edge, page.X, page.Y, page.Width - 1, page.Height - 1);
        for (int i = 0; i < TabCount; i++)
        {
            var r = GetTabRect(i); bool selected = i == SelectedIndex;
            if (selected) { using var on = new SolidBrush(Surface); g.FillRectangle(on, r); using var accent = new SolidBrush(Blue); g.FillRectangle(accent, r.X, r.Bottom - 2, r.Width, 2); }
            DrawTracked(g, TabPages[i].Text.ToUpperInvariant(), TabFont, selected ? Color.White : Color.FromArgb(154, 161, 176), r, 1f, true);
        }
    }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
}
