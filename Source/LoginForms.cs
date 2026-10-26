using System.Drawing.Imaging;

namespace HoverDeck;

internal sealed class ClientLoginForm : Form
{
    private readonly TextBox account = new() { Width = 390, MaxLength = 320 };
    private readonly TextBox password = new() { Width = 390, MaxLength = 512, UseSystemPasswordChar = true };
    private readonly CheckBox onLaunch = new() { Text = "Log in automatically when HoverDeck opens this client", AutoSize = true };
    private readonly NumericUpDown delay = new() { Minimum = 0, Maximum = 120, Width = 85 };
    private readonly Label screenStatus = new() { AutoSize = false, Height = 46, Dock = DockStyle.Fill };
    private readonly string executable;
    private readonly GameWindow? window;
    private LoginScreen? screen;
    public ClientLogin? Result { get; private set; }
    public bool LoginNow { get; private set; }
    public ClientLoginForm(Slot slot, Layout layout, GameWindow? window) : this(slot, layout, window, slot.Login) { }
    public ClientLoginForm(Slot slot, Layout layout, GameWindow? window, ClientLogin? draft)
    {
        this.window = window; executable = slot.PathFor(layout); screen = draft?.Screen;
        Text = slot.Name + " — Login"; ClientSize = new(610, 600); MinimumSize = Size; FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent; MaximizeBox = MinimizeBox = false;
        Font = new("Segoe UI", 10); BackColor = AppTheme.Surface; ForeColor = AppTheme.Ink;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 11 };
        foreach (int height in new[] { 43, 24, 34, 24, 36, 32, 35, 47, 41, 90, 40 }) root.RowStyles.Add(new(SizeType.Absolute, height));
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "Private-server login · Username and password inside WoW", Dock = DockStyle.Fill, Font = new(Font, FontStyle.Bold) }, 0, 0);
        root.Controls.Add(new Label { Text = "Account / email", AutoSize = true }, 0, 1); account.AccessibleName = "Client login account"; root.Controls.Add(account, 0, 2);
        root.Controls.Add(new Label { Text = "Password", AutoSize = true }, 0, 3); password.AccessibleName = "Client login password"; root.Controls.Add(password, 0, 4);
        root.Controls.Add(onLaunch, 0, 5);
        var waitRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; waitRow.Controls.Add(new Label { Text = "Wait after launch (seconds)", AutoSize = true, Margin = new(0, 4, 10, 0) }); waitRow.Controls.Add(delay); root.Controls.Add(waitRow, 0, 6);
        root.Controls.Add(screenStatus, 0, 7);
        var choose = MakeButton("Set up login screen…", 185); choose.Name = "SetupLogin"; choose.Enabled = window is not null; root.Controls.Add(choose, 0, 8);
        root.Controls.Add(new Label { Text = "One-time setup: click the account field and a fixed login label/logo in a preview. Tab then selects Password; Enter submits. Custom field positions are optional. Set up again if the client size changes.\n\nCredentials are encrypted for this Windows account. Escape or switching away stops login input.", Dock = DockStyle.Fill, Font = new("Segoe UI", 9) }, 0, 9);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var save = MakeButton("Save", 85); var login = MakeButton("Save && log in", 125); var remove = MakeButton("Clear login", 105); var cancel = MakeButton("Cancel", 90);
        actions.Controls.AddRange(new Control[] { save, login, remove, cancel }); root.Controls.Add(actions, 0, 10);
        cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel; AcceptButton = save;
        save.Name = "SaveLogin"; cancel.Name = "CancelLogin";
        onLaunch.Checked = draft?.OnLaunch ?? true; delay.Value = Math.Clamp(draft?.DelaySeconds ?? 8, 0, 120);
        string? unlockError = null;
        if (draft is { } saved)
        {
            if (!Matching.SamePath(saved.Executable, executable)) screen = null;
            try { var credentials = CredentialProtection.Unprotect(saved.ProtectedCredentials); account.Text = credentials.Account; password.Text = credentials.Password; }
            catch (Exception ex) { unlockError = ex.Message; }
        }
        screenStatus.Text = unlockError ?? (screen is { IsValid: true } ? $"Login screen configured at {screen.Width} × {screen.Height} · {(screen.KeyboardNavigation ? "Tab / Enter" : "custom positions")}." : window is null ? "Open this client first to set up its login screen. You can save its credentials now." : "Set up this client's login screen once before automatic login.");
        login.Enabled = window is not null;
        save.Click += (_, _) => Save(false); login.Click += (_, _) => Save(true);
        remove.Click += (_, _) => { Result = null; DialogResult = DialogResult.OK; Close(); };
        choose.Click += (_, _) =>
        {
            if (window is null) return;
            try
            {
                // Finish this modal dialog before activating the game. Hiding a modal
                // form also ends ShowDialog, leaving async click handlers disposed.
                Result = CreateDraft(); DialogResult = DialogResult.Retry; Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Login screen setup"); }
        };
        foreach (var text in new[] { account, password }) { text.BackColor = AppTheme.Field; text.ForeColor = ForeColor; }
    }
    private static Button MakeButton(string text, int width) => new() { Text = text, Width = width, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = AppTheme.ButtonFace, ForeColor = Color.White, Margin = new(0, 0, 8, 0) };
    private void Save(bool loginNow)
    {
        if (string.IsNullOrWhiteSpace(account.Text) || password.Text.Length == 0) { MessageBox.Show(this, "Enter the account and password for this client.", "Login details"); return; }
        if (account.Text.Any(char.IsControl) || password.Text.Any(char.IsControl)) { MessageBox.Show(this, "Login details cannot contain line breaks or control characters.", "Login details"); return; }
        if (loginNow && screen is not { IsValid: true }) { MessageBox.Show(this, "Set up the login screen first.", "Login details"); return; }
        try
        {
            Result = CreateDraft();
            LoginNow = loginNow; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save login"); }
    }
    private ClientLogin CreateDraft() => new() { Executable = executable, ProtectedCredentials = CredentialProtection.Protect(new(account.Text.Trim(), password.Text)), Screen = screen, OnLaunch = onLaunch.Checked, DelaySeconds = (int)delay.Value };
    protected override void Dispose(bool disposing) { if (disposing) { account.Clear(); password.Clear(); } base.Dispose(disposing); }
}

internal sealed class LoginFieldPicker : Form
{
    private readonly PictureBox picture;
    private readonly Label instructions = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly List<Point> points = new();
    private readonly Bitmap capture;
    private readonly CheckBox custom = new() { Text = "Custom positions (choose Password and Log In too)", AutoSize = true, Margin = new(16, 6, 0, 0) };
    public LoginScreen? Result { get; private set; }
    public LoginFieldPicker(Bitmap capture)
    {
        this.capture = capture;
        Text = "Set up login screen — click the preview"; Size = new(1100, 760); MinimumSize = new(850, 520); StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background; ForeColor = Color.White; Font = new("Segoe UI", 11);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(14), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new(SizeType.Absolute, 60)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 40)); Controls.Add(root);
        root.Controls.Add(instructions, 0, 0); picture = new() { Name = "LoginPreview", Image = capture, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Cursor = Cursors.Cross, BackColor = Color.Black }; root.Controls.Add(picture, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill }; var restart = new Button { Text = "Start over", Width = 120, Height = 32 }; var cancel = new Button { Text = "Cancel", Width = 100, Height = 32, DialogResult = DialogResult.Cancel };
        actions.WrapContents = false; actions.Controls.AddRange(new Control[] { restart, cancel, custom }); root.Controls.Add(actions, 0, 2); CancelButton = cancel;
        restart.Click += (_, _) => { points.Clear(); UpdateStep(); picture.Invalidate(); };
        custom.CheckedChanged += (_, _) => { points.Clear(); UpdateStep(); picture.Invalidate(); };
        picture.MouseClick += (_, e) => Pick(e.Location);
        picture.Paint += (_, e) => { var r = ImageRectangle(); double scale = r.Width / capture.Width; for (int i = 0; i < points.Count; i++) { float x = (float)(r.X + points[i].X * scale), y = (float)(r.Y + points[i].Y * scale); e.Graphics.FillEllipse(Brushes.DarkCyan, x - 12, y - 12, 24, 24); e.Graphics.DrawString((i + 1).ToString(), Font, Brushes.White, x - 5, y - 10); } };
        UpdateStep();
    }
    private RectangleF ImageRectangle()
    {
        float scale = Math.Min(picture.Width / (float)capture.Width, picture.Height / (float)capture.Height);
        return new((picture.Width - capture.Width * scale) / 2, (picture.Height - capture.Height * scale) / 2, capture.Width * scale, capture.Height * scale);
    }
    private void UpdateStep() => instructions.Text = !custom.Checked ? points.Count == 0
        ? "1 of 2 · Click the center of the ACCOUNT / EMAIL field.\nUse this mode when Tab goes to Password and Enter logs in."
        : "2 of 2 · Click a fixed LOGIN label or logo to recognize the screen.\nAvoid animated backgrounds, editable fields, and buttons."
        : points.Count switch
    {
        0 => "1 of 4 · Click the center of the ACCOUNT / EMAIL input field.",
        1 => "2 of 4 · Click the center of the PASSWORD input field.",
        2 => "3 of 4 · Click the LOG IN button.",
        _ => "4 of 4 · Click a fixed label or logo unique to the login screen.\nAvoid animated backgrounds, editable fields, and buttons that change when enabled."
    };
    private void Pick(Point click)
    {
        var r = ImageRectangle(); if (!r.Contains(click)) return;
        var p = new Point(Math.Clamp((int)((click.X - r.X) * capture.Width / r.Width), 0, capture.Width - 1), Math.Clamp((int)((click.Y - r.Y) * capture.Height / r.Height), 0, capture.Height - 1));
        PickClientPoint(p);
    }
    internal void PickClientPoint(Point p)
    {
        if (!new Rectangle(Point.Empty, capture.Size).Contains(p)) return;
        if (points.Count < (custom.Checked ? 3 : 1)) { points.Add(p); UpdateStep(); picture.Invalidate(); return; }
        var marker = new Rectangle(Math.Clamp(p.X - 48, 0, capture.Width - 96), Math.Clamp(p.Y - 16, 0, capture.Height - 32), 96, 32);
        if (points.Take(2).Any(field => marker.IntersectsWith(new Rectangle(field.X - 200, field.Y - 20, 400, 40)))) { instructions.Text = "Choose a fixed label or logo away from the account and password fields."; return; }
        using var patch = capture.Clone(marker, PixelFormat.Format32bppArgb);
        if (!LoginScreenMatch.HasDetail(patch)) { instructions.Text = "Choose a detailed fixed label/logo; this area is too plain to recognize reliably."; return; }
        using var stream = new MemoryStream(); patch.Save(stream, ImageFormat.Png);
        Result = new() { Width = capture.Width, Height = capture.Height, Account = points[0], Password = custom.Checked ? points[1] : default, Submit = custom.Checked ? points[2] : default, KeyboardNavigation = !custom.Checked, Landmark = marker, LandmarkPng = Convert.ToBase64String(stream.ToArray()) };
        if (!Result.IsValid) { points.Clear(); UpdateStep(); return; }
        DialogResult = DialogResult.OK; Close();
    }
}
