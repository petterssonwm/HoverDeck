namespace HoverDeck;

// One client row of the setup guide: the installed WoW version and the WoW account it starts with.
internal sealed record ClientChoice(WoWInstallation? Installation, BattleNetConnection? Connection, WoWLicense? Account);

// The first-run guide: Battle.net accounts, the clients to start, hover focus and minimizing, one
// step at a time. Battle.net, hover focus and minimizing are saved as soon as they change, like in
// Settings. The clients are applied on Finish, and only when that step was changed (or on a new PC),
// so opening the guide later never alters existing clients by accident.
internal sealed class SetupGuideForm : Form
{
    private static readonly string[] Titles = { "Welcome", "Battle.net", "Clients", "Hover focus", "Minimize", "Done" };
    private static readonly Color Body = Color.FromArgb(167, 174, 187);
    private readonly Settings settings;
    private readonly Layout layout;
    private readonly Action save;
    private readonly StepRail rail = new() { Dock = DockStyle.Left, Width = 200, Steps = Titles };
    private readonly Label caption = new() { AutoSize = true, ForeColor = AppTheme.Caption, Font = new("Segoe UI", 8f, FontStyle.Bold), Margin = new(0, 0, 0, 4) };
    private readonly Label heading = new() { AutoSize = true, ForeColor = AppTheme.Ink, Font = new("Segoe UI", 16f, FontStyle.Bold), Margin = new(0, 0, 0, 12) };
    private readonly Panel pages = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Button back = new() { Text = "Back", Width = 110, Height = 36, Name = "GuideBack" }, next = new() { Text = "Next", Width = 130, Height = 36, Name = "GuideNext" };
    private readonly List<Control> steps = new();
    private readonly ChoiceCard battleNetCard = new("Battle.net", "Pick a WoW version and account for each client."), privateCard = new("Private server", "Start a game file of your choice.");
    private readonly NumericUpDown count = new() { Minimum = 1, Maximum = 32, Width = 60, Name = "GuideClientCount", BorderStyle = BorderStyle.FixedSingle };
    private readonly FlowLayoutPanel rows = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
    private readonly TextBox gameFile = new() { Width = 360, Name = "GuidePrivateGame" };
    private readonly Label clientNote = Para("");
    private readonly List<(DarkComboBox Version, DarkComboBox Account)> clientRows = new();
    private int current;
    private bool clientsChanged, filling;
    public bool UsePrivateServer => privateCard.Selected;
    public string PrivateGame => gameFile.Text.Trim().Trim('"');
    // Applied on Finish only when the Clients step was changed, or on a new PC.
    public bool ClientsChanged => clientsChanged || settings.IsNew;
    public List<ClientChoice> Clients => clientRows.Take((int)count.Value).Select(r => r.Version.SelectedItem is WoWInstallation version && r.Account.SelectedItem is AccountItem account
        ? new ClientChoice(version, account.Connection, account.License) : new ClientChoice(r.Version.SelectedItem as WoWInstallation, null, null)).ToList();
    public int ClientCount => (int)count.Value;

    public SetupGuideForm(Settings settings, Layout layout, Action save, Func<string> hotkeyText, Action chooseHotkey, Action<int> setDelay, Action<bool> setMinimizeToTray)
    {
        this.settings = settings; this.layout = layout; this.save = save;
        Text = "HoverDeck setup"; ClientSize = new(860, 560); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = MaximizeBox = false;
        Font = new("Segoe UI", 10); BackColor = AppTheme.Surface; ForeColor = AppTheme.Ink;
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new(30, 24, 30, 20) };
        main.RowStyles.Add(new(SizeType.AutoSize)); main.RowStyles.Add(new(SizeType.AutoSize)); main.RowStyles.Add(new(SizeType.Percent, 100)); main.RowStyles.Add(new(SizeType.Absolute, 48));
        main.Controls.Add(caption, 0, 0); main.Controls.Add(heading, 0, 1); main.Controls.Add(pages, 0, 2);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new(SizeType.AutoSize)); footer.ColumnStyles.Add(new(SizeType.Percent, 100)); footer.ColumnStyles.Add(new(SizeType.AutoSize));
        back.Anchor = AnchorStyles.Left | AnchorStyles.Bottom; next.Anchor = AnchorStyles.Right | AnchorStyles.Bottom; AppTheme.Primary(next);
        footer.Controls.Add(back, 0, 0); footer.Controls.Add(next, 2, 0); main.Controls.Add(footer, 0, 3);
        Controls.Add(main); Controls.Add(rail);

        steps.Add(Page(
            Para("HoverDeck starts all your WoW clients with one click and puts each window exactly where you want it."),
            Para("With hover focus on, you can type into any WoW window just by pointing at it with the mouse, so you never have to click to switch between clients."),
            Note("This takes about two minutes. Everything can be changed later in Settings, and this guide is there too: Settings → Setup guide.")));
        steps.Add(BattleNetStep());
        steps.Add(ClientsStep());
        steps.Add(HoverStep(hotkeyText, chooseHotkey, setDelay));
        steps.Add(MinimizeStep(setMinimizeToTray));
        steps.Add(Page(
            Note("1   Press Start clients to open every client and move it into place. Battle.net is opened for you if needed."),
            Note("2   Drag a client on the map to move it, or drag its bottom-right corner to resize it. Edit client changes its account."),
            Note("3   Turn on Hover focus above Start clients: point at a WoW window and type, no clicking needed."),
            Para("You can open this guide again from Settings → Setup guide.", AppTheme.Caption)));
        foreach (var step in steps) { step.Dock = DockStyle.Fill; step.Visible = false; pages.Controls.Add(step); }

        rail.StepClicked += GoTo;
        back.Click += (_, _) => { if (current == 0) { DialogResult = DialogResult.Cancel; Close(); } else GoTo(current - 1); };
        next.Click += (_, _) => { if (current < steps.Count - 1) GoTo(current + 1); else if (ValidClients()) { DialogResult = DialogResult.OK; Close(); } };
        AcceptButton = next;
        DialogFrame.Apply(this); GoTo(0);
    }

    private void GoTo(int step)
    {
        current = step; rail.GoTo(step);
        for (int i = 0; i < steps.Count; i++) steps[i].Visible = i == step;
        caption.Text = $"STEP {step + 1} OF {steps.Count}";
        heading.Text = step switch { 0 => "Welcome to HoverDeck", 1 => "Connect your Battle.net account", 2 => "Set up your clients", 3 => "Hover focus", 4 => "When you minimize HoverDeck", _ => "You're ready" };
        back.Text = step == 0 ? "Skip setup" : "Back";
        next.Text = step == 0 ? "Get started" : step == steps.Count - 1 ? "Finish" : "Next";
        if (step == 2) FillClients();
    }

    // Battle.net: sign in on Blizzard's website (the same window as Settings → Battle.net connections).
    private Control BattleNetStep()
    {
        var connect = new Button { Text = "Connect Battle.net…", Width = 220, Height = 38, Name = "GuideConnectBattleNet", Margin = new(0, 4, 0, 12) }; AppTheme.Primary(connect);
        var status = Note(""); status.Name = "GuideBattleNetStatus";
        void ShowAccounts()
        {
            var accounts = settings.BattleNetConnections.SelectMany(c => c.Accounts).ToList();
            status.Text = accounts.Count == 0 ? "Not connected yet." : $"Connected · {accounts.Count} WoW account{(accounts.Count == 1 ? "" : "s")}: " + string.Join(", ", accounts.Select(a => a.Label));
            connect.Text = accounts.Count == 0 ? "Connect Battle.net…" : "Sign in again…";
        }
        connect.Click += async (_, _) =>
        {
            connect.Enabled = back.Enabled = next.Enabled = false; status.Text = "Complete the sign-in on Blizzard's page, then choose Finish connection.";
            try
            {
                var connection = settings.BattleNetConnections.FirstOrDefault() ?? new BattleNetConnection { Name = "Battle.net" };
                var imported = await BattleNetWebForm.Fetch(this, connection, true);
                if (imported is not null)
                {
                    if (!settings.BattleNetConnections.Contains(connection)) settings.BattleNetConnections.Add(connection);
                    BattleNetConnectionSettings.ApplyImport(connection, imported); save();
                }
                if (!IsDisposed) ShowAccounts();
            }
            catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
            finally { if (!IsDisposed) connect.Enabled = back.Enabled = next.Enabled = true; }
        };
        ShowAccounts();
        return Page(
            Para("Sign in once on Blizzard's website so HoverDeck can list your WoW accounts. Your password goes to Blizzard only; HoverDeck never sees it. The Battle.net app doesn't need to be open."),
            connect, status,
            Para("Only playing on private servers? Choose Next and pick Private server on the next step.", AppTheme.Caption));
    }

    // Clients: Battle.net first, with the versions found on this PC and the imported accounts.
    private Control ClientsStep()
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
        var privateRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Name = "GuidePrivateRow", Margin = new(0, 4, 0, 0) }; privateRow.Controls.AddRange(new Control[] { gameFile, browse });
        var addFolder = new Button { Text = "Add WoW folder…", Width = 170, Height = 32, Name = "GuideAddFolder", Margin = new(0, 0, 0, 6) };
        addFolder.Click += async (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "Choose your World of Warcraft folder", UseDescriptionForTitle = true, ShowNewFolderButton = false };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            var folders = settings.WoWLibrary.Folders.Append(WoWInstallationScanner.NormalizeRoot(picker.SelectedPath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var found = await Task.Run(() => WoWInstallationScanner.Scan(folders)); if (IsDisposed) return;
            settings.WoWLibrary.Folders = folders; settings.WoWLibrary.Installations = found.Installations; settings.WoWLibrary.RootFoldersInitialized = true; save(); FillClients();
        };
        rows.Tag = privateRow; addFolder.Tag = "add-folder";
        var page = Page(cards, countRow, clientNote, addFolder, rows, privateRow);
        filling = true; count.Value = Math.Clamp(layout.Slots.Count, 1, 32); gameFile.Text = layout.Executable; filling = false;
        return page;
    }
    private sealed record AccountItem(BattleNetConnection Connection, WoWLicense License, bool ShowLogin)
    {
        public override string ToString() => License.Label + (ShowLogin ? " · " + Connection.Name : "");
    }
    private void FillClients()
    {
        filling = true;
        var installations = settings.WoWLibrary.Installations;
        // The login's name is shown only when accounts come from more than one Battle.net login.
        bool severalLogins = settings.BattleNetConnections.Count(c => c.Accounts.Count > 0) > 1;
        var accounts = settings.BattleNetConnections.SelectMany(c => c.Accounts.Select(a => new AccountItem(c, a, severalLogins))).ToList();
        bool battleNet = battleNetCard.Selected;
        var addFolder = steps.Count > 2 ? steps[2].Controls.OfType<Button>().First(b => Equals(b.Tag, "add-folder")) : null;
        if (rows.Tag is Control privateRow) privateRow.Visible = !battleNet;
        rows.Visible = battleNet && installations.Count > 0;
        if (addFolder is not null) addFolder.Visible = battleNet && installations.Count == 0;
        clientNote.Text = !battleNet ? "All clients start this game file. You can give a client its own file and a saved login later in Edit client."
            : installations.Count == 0 ? "No World of Warcraft was found on this PC. If it is installed, choose its folder:"
            : accounts.Count == 0 ? "Connect Battle.net on the previous step to choose an account for each client. You can also do it later in Edit client."
            : "Pick the WoW version and account for each client. Positions are set on the map afterwards.";
        while (clientRows.Count < count.Value)
        {
            int index = clientRows.Count;
            var version = new DarkComboBox { Width = 250, Name = "GuideVersion" + index }; var account = new DarkComboBox { Width = 230, Name = "GuideAccount" + index };
            version.SelectedIndexChanged += (_, _) => { if (!filling) clientsChanged = true; };
            // The version name alone; the folder only when two installed copies share a version.
            version.FormattingEnabled = true; version.Format += (_, e) => { if (e.ListItem is WoWInstallation i) e.Value = i.GameVersion + (installations.Count(x => x.GameVersion == i.GameVersion) > 1 ? " · " + i.RootFolder : ""); }; account.SelectedIndexChanged += (_, _) => { if (!filling) clientsChanged = true; };
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new(0, 0, 0, 8) };
            row.Controls.Add(new Label { Text = $"Client {index + 1}", Width = 80, ForeColor = AppTheme.Ink, Margin = new(0, 6, 8, 0) }); row.Controls.Add(version); row.Controls.Add(account);
            account.Margin = new(10, 0, 0, 0); rows.Controls.Add(row); clientRows.Add((version, account));
            // Start from what the layout's client already uses, otherwise the first version and the next unused account.
            var existing = index < layout.Slots.Count ? layout.Slots[index].BattleNet : null;
            version.Items.AddRange(installations.Cast<object>().ToArray());
            version.SelectedItem = installations.FirstOrDefault(i => existing is not null && Matching.SamePath(i.Executable, existing.Executable)) ?? installations.FirstOrDefault();
            account.Items.AddRange(accounts.Cast<object>().ToArray());
            account.SelectedItem = accounts.FirstOrDefault(a => existing is not null && a.Connection.Id == existing.ConnectionId && a.License.Id == existing.WebAccountId) ?? (index < accounts.Count ? accounts[index] : null);
        }
        for (int i = 0; i < rows.Controls.Count; i++) rows.Controls[i].Visible = i < count.Value;
        filling = false;
    }
    private bool ValidClients()
    {
        if (!ClientsChanged) return true;
        string? problem = UsePrivateServer ? (File.Exists(PrivateGame) ? null : "Choose the game file of your private server on the Clients step.")
            : null; // Battle.net clients without a version or account stay to be finished in Edit client.
        if (problem is null) return true;
        GoTo(2); clientNote.Text = problem; return false;
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
        delayRow.Controls.Add(Para("Delay before focus moves (milliseconds)", AppTheme.Muted, new(0, 6, 12, 0))); delayRow.Controls.Add(new FieldBox(delay, 90));
        return Page(
            Para("Point at a WoW window and it gets keyboard focus, without clicking and without moving it to the front. Turn it on and off with the switch above Start clients or with your shortcut."),
            shortcutRow, delayRow,
            Para("A short delay stops focus from jumping while the mouse crosses another window. 50 ms suits most people.", AppTheme.Caption, new(0, 12, 0, 0)));
    }

    private Control MinimizeStep(Action<bool> setMinimizeToTray)
    {
        var tray = new ChoiceCard("Notification area", "Hides next to the clock. Open it again from the HoverDeck icon there.") { Name = "GuideTray", Selected = settings.MinimizeToTray, Margin = new(0, 0, 12, 0) };
        var taskbar = new ChoiceCard("Taskbar", "Minimizes like a normal window.") { Name = "GuideTaskbar", Selected = !settings.MinimizeToTray, Margin = Padding.Empty };
        foreach (var card in new[] { tray, taskbar }) card.Click += (_, _) => { tray.Selected = card == tray; taskbar.Selected = card == taskbar; settings.MinimizeToTray = tray.Selected; setMinimizeToTray(tray.Selected); save(); };
        var cards = new FlowLayoutPanel { AutoSize = true, WrapContents = false }; cards.Controls.AddRange(new Control[] { tray, taskbar });
        return Page(Para("HoverDeck keeps working in the background either way: clients stay in place and hover focus keeps running."), cards);
    }

    private static FlowLayoutPanel Page(params Control[] content)
    {
        var page = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = Padding.Empty, Padding = Padding.Empty };
        page.Controls.AddRange(content); return page;
    }
    private static Label Para(string text, Color? color = null, Padding? margin = null) =>
        new() { Text = text, AutoSize = true, MaximumSize = new(560, 0), ForeColor = color ?? Body, Margin = margin ?? new(0, 0, 0, 12) };
    private static Label Note(string text) =>
        new() { Text = text, AutoSize = true, MaximumSize = new(560, 0), MinimumSize = new(560, 0), ForeColor = AppTheme.Ink, BackColor = AppTheme.Field, Padding = new(12, 10, 12, 10), Margin = new(0, 0, 0, 8) };

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
