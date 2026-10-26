using System.Drawing.Drawing2D;

namespace HoverDeck;

internal static class AppTheme
{
    // Launcher palette: near-black surfaces with Blizzard blue as the single accent.
    public static readonly Color Background = Color.FromArgb(11, 12, 16), Bar = Color.FromArgb(16, 18, 23), Rail = Color.FromArgb(17, 19, 24), Surface = Color.FromArgb(22, 25, 31),
        Field = Color.FromArgb(15, 17, 22), Line = Color.FromArgb(31, 35, 43), FieldBorder = Color.FromArgb(42, 46, 56), ButtonFace = Color.FromArgb(34, 38, 47), ButtonHover = Color.FromArgb(44, 49, 60), ButtonBorder = Color.FromArgb(47, 52, 64),
        Ink = Color.FromArgb(242, 244, 248), Muted = Color.FromArgb(139, 147, 163), Caption = Color.FromArgb(111, 119, 135),
        Blue = Color.FromArgb(20, 142, 255), BlueDark = Color.FromArgb(13, 116, 223), BlueText = Color.FromArgb(90, 174, 255), BlueWash = Color.FromArgb(13, 42, 71), Online = Color.FromArgb(58, 209, 111), Selected = Color.FromArgb(27, 42, 61);

    private static Icon? appIcon; private static Image? logo;
    public static Icon AppIcon => appIcon ??= Load(s => new Icon(s)) ?? SystemIcons.Application;
    public static Image? Logo => logo ??= Load(Image.FromStream);
    private static T? Load<T>(Func<Stream, T> read) where T : class
    {
        string name = typeof(T) == typeof(Icon) ? "HoverDeck.App.ico" : "HoverDeck.Logo.png";
        using var stream = typeof(AppTheme).Assembly.GetManifestResourceStream(name);
        if (stream is null) return null;
        var copy = new MemoryStream(); stream.CopyTo(copy); copy.Position = 0; return read(copy); // GDI+ images need their stream kept open
    }
    public static void Apply(Form form)
    {
        form.BackColor = Surface; form.ForeColor = Ink; form.Icon = AppIcon;
        Style(form); DialogFrame.Apply(form);
    }
    public static void Style(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is TextBox or ComboBox or ListBox or NumericUpDown) { control.BackColor = Field; control.ForeColor = Ink; }
            if (control is TextBox { BorderStyle: BorderStyle.Fixed3D } text) text.BorderStyle = BorderStyle.FixedSingle;
            if (control is ListBox { BorderStyle: BorderStyle.Fixed3D } list) list.BorderStyle = BorderStyle.FixedSingle;
            if (control is Button button) { button.FlatStyle = FlatStyle.Flat; button.BackColor = ButtonFace; button.ForeColor = Ink; button.FlatAppearance.BorderColor = ButtonBorder; button.FlatAppearance.MouseOverBackColor = ButtonHover; }
            Style(control);
        }
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, object> primaries = new();
    // Finishing pass for every framed dialog: dark button and field borders, light text. Buttons
    // that were styled on purpose (blue primary actions, coloured text such as Delete) keep their look.
    public static void Polish(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            switch (control)
            {
                case Button button when !primaries.TryGetValue(button, out _):
                    button.FlatStyle = FlatStyle.Flat;
                    if (button.UseVisualStyleBackColor || button.BackColor == SystemColors.Control) { button.UseVisualStyleBackColor = false; button.BackColor = ButtonFace; }
                    if (button.ForeColor == SystemColors.ControlText || button.ForeColor == Color.White) button.ForeColor = Ink;
                    button.FlatAppearance.BorderColor = ButtonBorder; button.FlatAppearance.MouseOverBackColor = ButtonHover;
                    break;
                case TextBox text: text.BackColor = Field; text.ForeColor = Ink; if (text.BorderStyle == BorderStyle.Fixed3D) text.BorderStyle = BorderStyle.FixedSingle; break;
                case ListBox list: list.BackColor = Field; list.ForeColor = Ink; if (list.BorderStyle == BorderStyle.Fixed3D) list.BorderStyle = BorderStyle.FixedSingle; break;
            }
            Polish(control);
        }
    }
    public static void Primary(Button button)
    {
        primaries.AddOrUpdate(button, new object());
        button.FlatStyle = FlatStyle.Flat; button.BackColor = Blue; button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = Blue; button.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 155, 255);
        // Flat buttons only grey out their text, so a disabled primary action also loses its blue.
        void Sync() { button.BackColor = button.Enabled ? Blue : Color.FromArgb(30, 42, 58); button.FlatAppearance.BorderColor = button.BackColor; }
        button.EnabledChanged += (_, _) => Sync(); Sync();
    }
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
        return path;
    }
    // GDI has no letter spacing, so small uppercase captions are laid out one character at a time.
    public static Size MeasureTracked(string text, Font font, float tracking)
    {
        float width = 0; int height = TextRenderer.MeasureText("Ag", font, Size.Empty, TextFormatFlags.NoPadding).Height;
        foreach (char c in text) width += TextRenderer.MeasureText(c.ToString(), font, Size.Empty, TextFormatFlags.NoPadding).Width + tracking;
        return new((int)Math.Ceiling(Math.Max(0, width - tracking)), height);
    }
    public static void DrawTracked(Graphics g, string text, Font font, Color color, Rectangle bounds, float tracking, bool center = false)
    {
        var size = MeasureTracked(text, font, tracking);
        float x = center ? bounds.X + (bounds.Width - size.Width) / 2f : bounds.X; int y = bounds.Y + (bounds.Height - size.Height) / 2;
        foreach (char c in text)
        {
            var s = c.ToString(); TextRenderer.DrawText(g, s, font, new Point((int)Math.Round(x), y), color, TextFormatFlags.NoPadding);
            x += TextRenderer.MeasureText(s, font, Size.Empty, TextFormatFlags.NoPadding).Width + tracking;
        }
    }
    public static void DrawTile(Graphics g, Rectangle r, Color color, string text, Font font)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new LinearGradientBrush(r, ControlPaint.Light(color, .35f), color, 45f);
        using var path = Round(r, Math.Max(3, r.Width / 7f)); g.FillPath(fill, path);
        TextRenderer.DrawText(g, text, font, r, Background, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
