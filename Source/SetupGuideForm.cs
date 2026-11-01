namespace HoverDeck;

// One client row of the setup guide: the installed WoW version and the WoW account it starts with.
internal sealed record ClientChoice(WoWInstallation? Installation, BattleNetConnection? Connection, WoWLicense? Account);

// The first-run guide: Battle.net accounts, the clients to start, hover focus and minimizing, one
// step at a time. Battle.net, hover focus and minimizing are saved as soon as they change, like in
// Settings. The clients are applied on Finish, unless that step was skipped, and only when it was
// changed (or on a new PC), so opening the guide later never alters existing clients by accident.
internal sealed class SetupGuideForm : Form
{
    private static readonly string[] Titles = { "Welcome", "Battle.net", "Clients", "Hover focus", "Minimize" };
    private static readonly Color Body = Color.FromArgb(167, 174, 187), Problem = Color.FromArgb(237, 185, 109);
    private const int ClientsStep = 2;
    private readonly Settings settings;
    private readonly Layout layout;
    private readonly Action save;
    private readonly Func<string, bool> confirm;
    private readonly StepRail rail = new() { Dock = DockStyle.Left, Width = 200, Steps = Titles };
    private readonly Label caption = new() { AutoSize = true, ForeColor = AppTheme.Caption, Font = new("Segoe UI", 8f, FontStyle.Bold), Margin = new(0, 0, 0, 4) };
    private readonly Label heading = new() { AutoSize = true, ForeColor = AppTheme.Ink, Font = new("Segoe UI", 16f, FontStyle.Bold), Margin = new(0, 0, 0, 12) };
    private readonly Panel pages = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Button back = new() { Text = "Back", Width = 110, Height = 36, Name = "GuideBack" }, next = new() { Text = "Next", Width = 130, Height = 36, Name = "GuideNext" },
        skip = new() { Text = "Skip for now", Width = 130, Height = 36, Name = "GuideSkipClients" };
    private readonly List<Control> steps = new();
    private readonly ChoiceCard battleNetCard = new("Battle.net", "Pick a WoW version and account for each client."), privateCard = new("Private server", "Start a game file of your choice.");
    private readonly NumericUpDown count = new() { Minimum = 1, Maximum = 32, Width = 60, Name = "GuideClientCount", BorderStyle = BorderStyle.FixedSingle };
    private readonly FlowLayoutPanel rows = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
    private readonly FlowLayoutPanel privateRow = new() { AutoSize = true, WrapContents = false, Name = "GuidePrivateRow", Margin = new(0, 4, 0, 0) };
    private readonly TextBox gameFile = new() { Width = 360, Name = "GuidePrivateGame" };
    private readonly Label clientNote = Para(""), battleNetStatus = Para("", AppTheme.Ink);
    private readonly Button connectStep = new() { Text = "Connect Battle.net…", Width = 220, Height = 38, Name = "GuideConnectBattleNet", Margin = new(0, 4, 0, 12) };
    private readonly Button connectHere = new() { Text = "Connect Battle.net…", Width = 220, Height = 36, Name = "GuideConnectClients", Margin = new(0, 0, 0, 12) };
    private readonly Button addFolder = new() { Text = "Add WoW folder…", Width = 170, Height = 32, Name = "GuideAddFolder", Margin = new(0, 0, 0, 6) };
    private readonly List<(DarkComboBox Version, DarkComboBox Account)> clientRows = new();
    private int current;
    private bool clientsChanged, clientsSkipped, filling;
    public bool UsePrivateServer => privateCard.Selected;
    public string PrivateGame => gameFile.Text.Trim().Trim('"');
    // Applied on Finish unless skipped, and only when the Clients step was changed, or on a new PC.
    public bool ClientsChanged => !clientsSkipped && (clientsChanged || settings.IsNew);
    public int ClientCount => (int)count.Value;
    public List<ClientChoice> Clients => clientRows.Take(ClientCount).Select(r => r.Account.SelectedItem is AccountItem account
        ? new ClientChoice(r.Version.SelectedItem as WoWInstallation, account.Connection, account.License) : new ClientChoice(r.Version.SelectedItem as WoWInstallation, null, null)).ToList();
    private List<WoWLicense> Accounts => settings.BattleNetConnections.SelectMany(c => c.Accounts).ToList();

    public SetupGuideForm(Settings settings, Layout layout, Action save, Func<string> hotkeyText, Action chooseHotkey, Action<int> setDelay, Action<bool> setMinimizeToTray, Func<string, bool>? confirm = null)
    {
        this.settings = settings; this.layout = layout; this.save = save;
        this.confirm = confirm ?? (message => ThemedMessageBox.Show(this, message, "HoverDeck setup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
        Text = "HoverDeck setup"; ClientSize = new(860, 560); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = MaximizeBox = false;
        Font = new("Segoe UI", 10); BackColor = AppTheme.Surface; ForeColor = AppTheme.Ink;
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new(30, 24, 30, 20) };
        main.RowStyles.Add(new(SizeType.AutoSize)); main.RowStyles.Add(new(SizeType.AutoSize)); main.RowStyles.Add(new(SizeType.Percent, 100)); main.RowStyles.Add(new(SizeType.Absolute, 48));
        main.Controls.Add(caption, 0, 0); main.Controls.Add(heading, 0, 1); main.Controls.Add(pages, 0, 2);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new(SizeType.AutoSize)); footer.ColumnStyles.Add(new(SizeType.Percent, 100)); footer.ColumnStyles.Add(new(SizeType.AutoSize)); footer.ColumnStyles.Add(new(SizeType.AutoSize));
        back.Anchor = AnchorStyles.Left | AnchorStyles.Bottom; skip.Anchor = next.Anchor = AnchorStyles.Right | AnchorStyles.Bottom; skip.Margin = new(0, 0, 10, 0); AppTheme.Primary(next);
        footer.Controls.Add(back, 0, 0); footer.Controls.Add(skip, 2, 0); footer.Controls.Add(next, 3, 0); main.Controls.Add(footer, 0, 3);
        Controls.Add(main); Controls.Add(rail);

        steps.Add(Page(
            Para("HoverDeck starts all your WoW clients with one click and puts each window exactly where you want it."),
            Para("With hover focus turned on, a WoW client gets your keyboard as soon as you move the mouse over it, so you can switch between clients without clicking."),
            Para("Setup takes about two minutes. Everything can be changed later in Settings, where you also find this guide again (Settings → Setup guide).", AppTheme.Caption)));
        steps.Add(BattleNetStep());
        steps.Add(BuildClientsStep());
        steps.Add(HoverStep(hotkeyText, chooseHotkey, setDelay));
        steps.Add(MinimizeStep(setMinimizeToTray));
        foreach (var step in steps) { step.Dock = DockStyle.Fill; step.Visible = false; pages.Controls.Add(step); }

        rail.StepClicked += GoTo;
        back.Click += (_, _) => { if (current == 0) { DialogResult = DialogResult.Cancel; Close(); } else GoTo(current - 1); };
        next.Click += (_, _) => Next();
        skip.Click += (_, _) =>
        {
            if (!this.confirm("Without clients set up, Start clients can't open any WoW.\n\nYou can set them up after this guide: select a client and choose Edit client.\n\nSkip setting up clients for now?")) return;
            clientsSkipped = true; GoTo(ClientsStep + 1);
        };
        AcceptButton = next;
        DialogFrame.Apply(this); GoTo(0);
    }

    private void Next()
    {
        if (current == 1 && Accounts.Count == 0
            && !confirm("You're not signed in to Battle.net.\n\nWithout it, HoverDeck can't start Blizzard's WoW versions for you. You can still set up private-server clients, and sign in later in Settings → Battle.net connections.\n\nContinue without signing in?")) return;
        if (current == ClientsStep) { if (ClientsProblem() is { } problem) { ShowProblem(problem); return; } clientsSkipped = false; }
        if (current < steps.Count - 1) { GoTo(current + 1); return; }
        if (ClientsChanged && ClientsProblem() is { } unfinished) { GoTo(ClientsStep); ShowProblem(unfinished); return; }
        DialogResult = DialogResult.OK; Close();
    }
    private void ShowProblem(string problem) { clientNote.Text = problem; clientNote.ForeColor = Problem; }

    private void GoTo(int step)
    {
        current = step; rail.GoTo(step);
        for (int i = 0; i < steps.Count; i++) steps[i].Visible = i == step;
        caption.Text = $"STEP {step + 1} OF {steps.Count}";
        heading.Text = step switch { 0 => "Welcome to HoverDeck", 1 => "Connect your Battle.net account", 2 => "Set up your clients", 3 => "Hover focus", _ => "When you minimize HoverDeck" };
        back.Text = step == 0 ? "Skip setup" : "Back";
        next.Text = step == 0 ? "Get started" : step == steps.Count - 1 ? "Finish" : "Next";
        skip.Visible = step == ClientsStep;
        if (step == 1) ShowAccounts();
        if (step == ClientsStep) FillClients();
    }

    // Sign in on Blizzard's website (the same window as Settings → Battle.net connections); the accounts are imported by itself.
    private async Task Connect(Button button, Label status)
    {
        button.Enabled = back.Enabled = next.Enabled = skip.Enabled = false; status.Text = "Sign in on Blizzard's page. Your WoW accounts are imported as soon as you're signed in."; status.ForeColor = AppTheme.Ink;
        try
        {
            var connection = settings.BattleNetConnections.FirstOrDefault() ?? new BattleNetConnection { Name = "Battle.net" };
            var imported = await BattleNetWebForm.Fetch(this, connection, true);
            if (imported is not null)
            {
                if (!settings.BattleNetConnections.Contains(connection)) settings.BattleNetConnections.Add(connection);
                BattleNetConnectionSettings.ApplyImport(connection, imported); save();
            }
        }
        catch (Exception ex) { if (!IsDisposed) { status.Text = ex.Message; status.ForeColor = Problem; return; } }
        finally { if (!IsDisposed) button.Enabled = back.Enabled = next.Enabled = skip.Enabled = true; }
        if (IsDisposed) return;
        if (current == 1) ShowAccounts(); else FillClients();
    }
    private void ShowAccounts()
    {
        var accounts = Accounts;
        battleNetStatus.Text = accounts.Count == 0 ? "Not signed in yet." : $"✓  Signed in · {accounts.Count} WoW account{(accounts.Count == 1 ? "" : "s")}: " + string.Join(", ", accounts.Select(a => a.Label));
        battleNetStatus.ForeColor = AppTheme.Ink;
        connectStep.Text = accounts.Count == 0 ? "Connect Battle.net…" : "Sign in again…";
    }
    private Control BattleNetStep()
    {
        AppTheme.Primary(connectStep); battleNetStatus.Name = "GuideBattleNetStatus";
        connectStep.Click += async (_, _) => await Connect(connectStep, battleNetStatus);
        return Page(
            Para("Sign in once on Blizzard's website so HoverDeck can list your WoW accounts. Your password goes to Blizzard only; HoverDeck never sees it."),
            connectStep, battleNetStatus,
            Para("Only playing on private servers? Choose Next and pick Private server on the next step.", AppTheme.Caption));
    }

    // Clients: Battle.net first, with the versions found on this PC and the imported accounts.
    private Control BuildClientsStep()
    {
        battleNetCard.Selected = true;
        foreach (var card in new[] { battleNetCard, privateCard }) card.Click += (_, _) => { battleNetCard.Selected = card == battleNetCard; privateCard.Selected = card == privateCard; clientsChanged = true; FillClients(); };
        var cards = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new(0, 0, 0, 12) }; battleNetCard.Margin = new(0, 0, 12, 0); privateCard.Margin = Padding.Empty;
        cards.Controls.AddRange(new Control[] { battleNetCard, privateCard });
        var countRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new(0, 0, 0, 8) };
        countRow.Controls.Add(Para("Number of clients", AppTheme.Muted, new(0, 6, 10, 0))); countRow.Controls.Add(new FieldBox(count, 60));
        count.ValueChanged += (_, _) => { if (!filling) { clientsChanged = true; FillClients(); } };
        var browse = new Button { Text = "Browse…", Width = 100, Height = 30, Margin = new(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Title = "Choose the game file of your private server (for example Wow.exe)", Filter = "Windows executables (*.exe)|*.exe", CheckFileExists = true };
            if (picker.ShowDialog(this) == DialogResult.OK) { gameFile.Text = picker.FileName; clientsChanged = true; }
        };
        gameFile.TextChanged += (_, _) => { if (!filling) clientsChanged = true; };
        privateRow.Controls.AddRange(new Control[] { gameFile, browse });
        AppTheme.Primary(connectHere); connectHere.Click += async (_, _) => await Connect(connectHere, clientNote);
        addFolder.Click += async (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "Choose your World of Warcraft folder", UseDescriptionForTitle = true, ShowNewFolderButton = false };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            var folders = settings.WoWLibrary.Folders.Append(WoWInstallationScanner.NormalizeRoot(picker.SelectedPath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var found = await Task.Run(() => WoWInstallationScanner.Scan(folders)); if (IsDisposed) return;
            settings.WoWLibrary.Folders = folders; settings.WoWLibrary.Installations = found.Installations; settings.WoWLibrary.RootFoldersInitialized = true; save(); FillClients();
        };
        filling = true; count.Value = Math.Clamp(layout.Slots.Count, 1, 32); gameFile.Text = layout.Executable; filling = false;
        return Page(cards, countRow, clientNote, connectHere, addFolder, rows, privateRow);
    }
    private sealed record AccountItem(BattleNetConnection Connection, WoWLicense License, bool ShowLogin)
    {
        public override string ToString() => License.Label + (ShowLogin ? " · " + Connection.Name : "");
    }
    // Rebuilds the client rows (after signing in, adding a folder or changing the count), keeping what was already picked.
    private void FillClients()
    {
        filling = true;
        var picked = clientRows.Select(r => (Version: r.Version.SelectedItem as WoWInstallation, Account: r.Account.SelectedItem as AccountItem)).ToList();
        var installations = settings.WoWLibrary.Installations;
        bool severalLogins = settings.BattleNetConnections.Count(c => c.Accounts.Count > 0) > 1; // the login's name only when it tells accounts apart
        var accounts = settings.BattleNetConnections.SelectMany(c => c.Accounts.Select(a => new AccountItem(c, a, severalLogins))).ToList();
        bool battleNet = battleNetCard.Selected;
        privateRow.Visible = !battleNet;
        connectHere.Visible = battleNet && accounts.Count == 0;
        addFolder.Visible = battleNet && accounts.Count > 0 && installations.Count == 0;
        rows.Visible = battleNet && installations.Count > 0 && accounts.Count > 0;
        clientNote.ForeColor = Body;
        clientNote.Text = !battleNet ? "All clients start this game file. You can give a client its own file and a saved login later in Edit client."
            : accounts.Count == 0 ? "Sign in to Battle.net to choose a WoW account for each client."
            : installations.Count == 0 ? "No World of Warcraft was found on this PC. If it is installed, choose its folder:"
            : "Pick the WoW version and account for each client. Positions are set on the map afterwards.";
        rows.Controls.Clear(); clientRows.Clear();
        for (int index = 0; index < ClientCount; index++)
        {
            var version = new DarkComboBox { Width = 250, Name = "GuideVersion" + index }; var account = new DarkComboBox { Width = 230, Name = "GuideAccount" + index, Margin = new(10, 0, 0, 0) };
            version.FormattingEnabled = true; version.Format += (_, e) => { if (e.ListItem is WoWInstallation i) e.Value = i.GameVersion + (installations.Count(x => x.GameVersion == i.GameVersion) > 1 ? " · " + i.RootFolder : ""); };
            version.Items.AddRange(installations.Cast<object>().ToArray()); account.Items.AddRange(accounts.Cast<object>().ToArray());
            // What was picked here before, otherwise what the layout's client uses, otherwise the first version and the next account.
            var existing = index < layout.Slots.Count ? layout.Slots[index].BattleNet : null;
            version.SelectedItem = index < picked.Count && picked[index].Version is { } v && installations.Contains(v) ? v
                : installations.FirstOrDefault(i => existing is not null && Matching.SamePath(i.Executable, existing.Executable)) ?? installations.FirstOrDefault();
            account.SelectedItem = index < picked.Count && picked[index].Account is { } a && accounts.Contains(a) ? a
                : accounts.FirstOrDefault(x => existing is not null && x.Connection.Id == existing.ConnectionId && x.License.Id == existing.WebAccountId) ?? (index < accounts.Count ? accounts[index] : null);
            version.SelectedIndexChanged += (_, _) => { if (!filling) clientsChanged = true; }; account.SelectedIndexChanged += (_, _) => { if (!filling) clientsChanged = true; };
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new(0, 0, 0, 8) };
            row.Controls.Add(new Label { Text = $"Client {index + 1}", Width = 80, ForeColor = AppTheme.Ink, Margin = new(0, 6, 8, 0) }); row.Controls.Add(version); row.Controls.Add(account);
            rows.Controls.Add(row); clientRows.Add((version, account));
        }
        filling = false;
    }
    // Next works only once every client can start: a version and an account each, or the private server's game file.
    private string? ClientsProblem()
    {
        if (UsePrivateServer) return File.Exists(PrivateGame) ? null : "Choose the game file of your private server, or Skip for now.";
        if (Accounts.Count == 0) return "Sign in to Battle.net first, or Skip for now.";
        if (settings.WoWLibrary.Installations.Count == 0) return "Add your World of Warcraft folder first, or Skip for now.";
        return Clients.Any(c => c.Installation is null || c.Account is null) ? "Choose a WoW version and an account for every client, or Skip for now." : null;
    }

    private Control HoverStep(Func<string> hotkeyText, Action chooseHotkey, Action<int> setDelay)
    {
        var shortcut = new Button { Text = hotkeyText(), Width = 200, Height = 34, Name = "GuideShortcut", Margin = new(0, 0, 0, 0) };
        shortcut.Click += (_, _) => { chooseHotkey(); shortcut.Text = hotkeyText(); };
        var delay = new NumericUpDown { Minimum = 0, Maximum = 5000, Increment = 10, Width = 90, Value = Math.Clamp(settings.DelayMs, 0, 5000), Name = "GuideDelay", BorderStyle = BorderStyle.FixedSingle };
        delay.ValueChanged += (_, _) => { settings.DelayMs = (int)delay.Value; setDelay(settings.DelayMs); save(); };
        var shortcutRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new(0, 0, 0, 10) };
        shortcutRow.Controls.Add(Para("Shortcut to turn it on and off", AppTheme.Muted, new(0, 8, 12, 0))); shortcutRow.Controls.Add(shortcut);
        var delayRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        delayRow.Controls.Add(Para("Switch delay (milliseconds)", AppTheme.Muted, new(0, 6, 12, 0))); delayRow.Controls.Add(new FieldBox(delay, 90));
        return Page(
            Para("With hover focus turned on, a WoW client gets your keyboard as soon as you move the mouse over it. Turn it on and off with the switch above Start clients or with your shortcut."),
            shortcutRow, delayRow,
            Para("The switch delay is how long the mouse has to rest on a client before it gets your keyboard. 0 switches instantly; 10 is a good start. Raise it if focus changes when you only pass over a client.", AppTheme.Caption, new(0, 12, 0, 0)));
    }

    private Control MinimizeStep(Action<bool> setMinimizeToTray)
    {
        var taskbar = new ChoiceCard("Taskbar", "Minimizes like a normal window.") { Name = "GuideTaskbar", Selected = !settings.MinimizeToTray, Margin = new(0, 0, 12, 0) };
        var tray = new ChoiceCard("Notification area", "Hides next to the clock. Open it again from the HoverDeck icon there.") { Name = "GuideTray", Selected = settings.MinimizeToTray, Margin = Padding.Empty };
        foreach (var card in new[] { taskbar, tray }) card.Click += (_, _) => { tray.Selected = card == tray; taskbar.Selected = card == taskbar; settings.MinimizeToTray = tray.Selected; setMinimizeToTray(tray.Selected); save(); };
        var cards = new FlowLayoutPanel { AutoSize = true, WrapContents = false }; cards.Controls.AddRange(new Control[] { taskbar, tray });
        return Page(Para("HoverDeck keeps working in the background either way: clients stay in place and hover focus keeps running."), cards);
    }

    private static FlowLayoutPanel Page(params Control[] content)
    {
        var page = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = Padding.Empty, Padding = Padding.Empty };
        page.Controls.AddRange(content); return page;
    }
    private static Label Para(string text, Color? color = null, Padding? margin = null) =>
        new() { Text = text, AutoSize = true, MaximumSize = new(560, 0), ForeColor = color ?? Body, Margin = margin ?? new(0, 0, 0, 12) };

    // Writes the guide's client choices into the layout. Unchanged Battle.net clients keep their saved
    // launch state; changed ones start fresh. New clients come from newSlot (default size and position).
    public static void ApplyClients(Layout layout, bool privateServer, string privateGame, int clientCount, IReadOnlyList<ClientChoice> clients, Func<int, Slot> newSlot)
    {
        while (layout.Slots.Count < clientCount) layout.Slots.Add(newSlot(layout.Slots.Count));
        while (layout.Slots.Count > clientCount) layout.Slots.RemoveAt(layout.Slots.Count - 1);
        if (privateServer)
        {
            layout.Executable = privateGame;
            foreach (var slot in layout.Slots) { slot.LaunchMethod = ClientLaunchMethod.Direct; slot.Executable = ""; }
            return;
        }
        for (int i = 0; i < layout.Slots.Count && i < clients.Count; i++)
        {
            var slot = layout.Slots[i]; var choice = clients[i]; slot.LaunchMethod = ClientLaunchMethod.BattleNet;
            if (choice.Installation is not { } version) continue;
            var profile = new BattleNetLaunch { Executable = version.Executable, GameVersion = version.GameVersion, GameTabId = version.GameTabId, Product = version.Product };
            if (choice is { Connection: { } connection, Account: { } account }) { profile.ConnectionId = connection.Id; profile.ConnectionName = connection.Name; profile.WebAccountId = account.Id; profile.AccountName = account.Label; }
            var old = slot.BattleNet;
            bool same = old is not null && Matching.SamePath(old.Executable, profile.Executable) && old.GameVersion == profile.GameVersion && old.ConnectionId == profile.ConnectionId && old.WebAccountId == profile.WebAccountId && old.AccountName == profile.AccountName;
            if (!same) slot.BattleNet = profile;
        }
    }
}
