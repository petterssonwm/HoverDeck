using System.Diagnostics;
using System.Drawing.Imaging;

namespace HoverDeck;

// Local fixture only: no server, network request, or real account is involved.
internal sealed class LoginIntegrationTests : Form
{
    private readonly string report;
    private readonly List<string> lines = new();
    private readonly TextBox account = new() { Bounds = new(160, 150, 280, 28), TabIndex = 0 };
    private readonly TextBox password = new() { Bounds = new(160, 200, 280, 28), UseSystemPasswordChar = true, TabIndex = 1 };
    private readonly Label logo = new() { Text = "LOGIN TEST", Bounds = new(170, 35, 300, 55), Font = new("Segoe UI", 28, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.DarkBlue };
    private int submitted;
    public LoginIntegrationTests(string report)
    {
        this.report = report; Text = "HoverDeck local login test — no server"; ClientSize = new(640, 390); BackColor = Color.FromArgb(30, 42, 59);
        StartPosition = FormStartPosition.Manual; var area = Native.Monitors().Last().WorkArea; Location = new(area.X + 60, area.Y + 70);
        var submit = new Button { Text = "Test Log In", Bounds = new(200, 260, 180, 40), TabIndex = 2 }; submit.Click += (_, _) => submitted++;
        AcceptButton = submit;
        Controls.AddRange(new Control[] { logo, account, password, submit });
        Shown += async (_, _) => await Run();
    }
    private void Check(string name, bool ok) { if (!ok) throw new Exception("FAIL: " + name); lines.Add("PASS: " + name); }
    private async Task Run()
    {
        try
        {
            // The test runner starts hidden; wait for explicit activation of this
            // fixture rather than bypassing Windows' foreground-window policy.
            Native.ShowWindowAsync(Handle, 4);
            for (int i = 0; i < 600 && Native.GetForegroundWindow() != Handle; i++) await Task.Delay(100);
            using var process = Process.GetCurrentProcess();
            var window = new GameWindow(Handle, process.Id, process.StartTime.ToUniversalTime().Ticks, Environment.ProcessPath!, Text, Bounds, false);
            await LoginInput.Activate(window, CancellationToken.None);
            using var image = LoginInput.Capture(window, CancellationToken.None);
            var landmark = new Rectangle(logo.Left + 8, logo.Top + 10, 96, 32);
            using var patch = image.Clone(landmark, PixelFormat.Format32bppArgb); using var stream = new MemoryStream(); patch.Save(stream, ImageFormat.Png);
            var screen = new LoginScreen { Width = image.Width, Height = image.Height, Account = new(210, 163), Password = new(210, 213), Submit = new(290, 280), Landmark = landmark, LandmarkPng = Convert.ToBase64String(stream.ToArray()) };
            var credentials = new LoginCredentials("fixture-account", "Fixture+{å}-42");
            var slot = new Slot { Name = "Fixture", Executable = Environment.ProcessPath!, Login = new() { Executable = Environment.ProcessPath!, ProtectedCredentials = CredentialProtection.Protect(credentials), Screen = screen, DelaySeconds = 0 } };
            Check("Fixture login marker is recognizable", LoginScreenMatch.Matches(image, screen));
            account.Text = "remembered account"; password.Text = "old password";
            await ClientLoginService.Run(slot, window, false, _ => { }, CancellationToken.None);
            await Task.Delay(200);
            Check("Login replaces remembered fields with literal account/password text", account.Text == credentials.Account && password.Text == credentials.Password);
            Check("Login clicks Submit once", submitted == 1);

            var simpleScreen = await CheckSetupWorkflow(window, slot, credentials);
            Check("Default setup uses keyboard navigation", simpleScreen.KeyboardNavigation && simpleScreen.IsValid);
            slot.Login.Screen = simpleScreen;
            account.Text = "remembered account"; password.Text = "old password";
            await ClientLoginService.Run(slot, window, false, _ => { }, CancellationToken.None);
            await Task.Delay(200);
            Check("Two-click setup fills password through Tab and submits through Enter", account.Text == credentials.Account && password.Text == credentials.Password && submitted == 2);

            account.Clear(); password.Clear(); logo.Text = "OTHER PAGE"; logo.BackColor = Color.DarkRed;
            bool mismatchStopped = false;
            try { await ClientLoginService.Run(slot, window, false, _ => { }, CancellationToken.None); } catch (InvalidOperationException) { mismatchStopped = true; }
            Check("Wrong-screen recognition stops before entering any credentials", mismatchStopped && account.Text.Length == 0 && password.Text.Length == 0 && submitted == 2);
            logo.Text = "LOGIN TEST"; logo.BackColor = Color.DarkBlue;
            ClientSize = new(700, 390); bool sizeStopped = false;
            try { await ClientLoginService.Run(slot, window, false, _ => { }, CancellationToken.None); } catch (InvalidOperationException) { sizeStopped = true; }
            Check("Changed client size stops before credential entry", sizeStopped && account.Text.Length == 0 && password.Text.Length == 0);
            ClientSize = new(640, 390);
            using var other = new Form { Text = "HoverDeck focus-loss fixture", Size = new(340, 150), StartPosition = FormStartPosition.Manual, Location = new(Left + 650, Top), ShowInTaskbar = false };
            var otherText = new TextBox { Dock = DockStyle.Top }; other.Controls.Add(otherText); other.Show();
            bool stoleFocus = false;
            EventHandler steal = (_, _) => { if (!stoleFocus && account.Text.Length > 0) { stoleFocus = true; other.Activate(); otherText.Focus(); } };
            account.TextChanged += steal; bool focusStopped = false;
            try { await ClientLoginService.Run(slot, window, false, _ => { }, CancellationToken.None); } catch (OperationCanceledException) { focusStopped = true; }
            finally { account.TextChanged -= steal; }
            Check("Losing focus stops before password entry or submission", stoleFocus && focusStopped && password.Text.Length == 0 && submitted == 2 && !otherText.Text.Contains(credentials.Password));
            lines.Add("Local login integration checks passed. No server authentication was attempted.");
        }
        catch (Exception ex) { lines.Add(ex.ToString()); Environment.ExitCode = 1; }
        finally { account.Clear(); password.Clear(); File.WriteAllLines(report, lines); Close(); }
    }

    private async Task<LoginScreen> CheckSetupWorkflow(GameWindow window, Slot slot, LoginCredentials credentials)
    {
        using var owner = new Form { Text = "HoverDeck setup test owner — no server", Size = new(450, 260), StartPosition = FormStartPosition.Manual, Location = new(Left + 680, Top) };
        owner.Show(); owner.Activate();
        var originalLogin = slot.Login;
        ClientLoginForm? firstDialog = null;
        int stage = 0; bool firstDisposed = false;
        using var timer = new System.Windows.Forms.Timer { Interval = 150 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.OfType<ClientLoginForm>().SingleOrDefault();
            var picker = Application.OpenForms.OfType<LoginFieldPicker>().SingleOrDefault();
            if (stage == 0 && dialog is not null)
            {
                firstDialog = dialog; stage = 1;
                ((Button)dialog.Controls.Find("SetupLogin", true).Single()).PerformClick();
            }
            else if (stage == 1 && picker is not null)
            {
                firstDisposed = firstDialog!.IsDisposed; stage = 2;
                picker.PickClientPoint(new(210, 163)); picker.PickClientPoint(new(226, 61));
            }
            else if (stage == 2 && dialog is not null)
            {
                stage = 3; ((Button)dialog.Controls.Find("SaveLogin", true).Single()).PerformClick();
            }
        };
        timer.Start();
        var result = await ClientLoginEditor.Edit(owner, slot, new Layout { Executable = Environment.ProcessPath! }, window);
        timer.Stop();
        Check("Modal setup disposes the first dialog before capture and reopens successfully", stage == 3 && firstDisposed && result?.Login?.Screen is { IsValid: true });
        Check("Encrypted draft survives setup without changing saved slot before Save", CredentialProtection.Unprotect(result!.Login!.ProtectedCredentials) == credentials && ReferenceEquals(slot.Login, originalLogin));
        Check("Setup captures a usable marker without typing or submitting credentials", submitted == 1);

        stage = 0;
        using var cancelTimer = new System.Windows.Forms.Timer { Interval = 150 };
        cancelTimer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.OfType<ClientLoginForm>().SingleOrDefault();
            var picker = Application.OpenForms.OfType<LoginFieldPicker>().SingleOrDefault();
            if (stage == 0 && dialog is not null) { stage = 1; ((Button)dialog.Controls.Find("SetupLogin", true).Single()).PerformClick(); }
            else if (stage == 1 && picker is not null) { stage = 2; picker.DialogResult = DialogResult.Cancel; picker.Close(); }
            else if (stage == 2 && dialog is not null) { stage = 3; ((Button)dialog.Controls.Find("CancelLogin", true).Single()).PerformClick(); }
        };
        cancelTimer.Start();
        var canceled = await ClientLoginEditor.Edit(owner, slot, new Layout { Executable = Environment.ProcessPath! }, window);
        cancelTimer.Stop();
        Check("Canceling picker reopens login details; Cancel leaves saved login unchanged", stage == 3 && canceled is null && ReferenceEquals(slot.Login, originalLogin));
        owner.Close();
        return result.Login.Screen!;
    }
}
