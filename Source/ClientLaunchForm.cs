using System.Text.Json;

namespace HoverDeck;

internal sealed class ClientLaunchForm : Form
{
    private readonly Slot draft;
    private readonly ComboBox method = Combo("LaunchMethod");
    private readonly ComboBox installation = Combo("BattleNetVersions");
    private readonly ComboBox connection = Combo("BattleNetConnection");
    private readonly ComboBox account = Combo("BattleNetAccounts");
    private readonly TextBox versionName = new() { Width = 570, Name = "LauncherVersionName" };
    private readonly Label info = new() { Dock = DockStyle.Fill, ForeColor = AppTheme.BlueText, Padding = new(0, 8, 0, 0) };
    private readonly CancellationTokenSource cancellation = new();
    private bool loading = true, reading;
    public Slot Result => draft;
    public ClientLaunchForm(Slot slot, Layout layout, Settings? settings = null, Action? saveShared = null)
    {
        settings ??= new(); saveShared ??= () => { };
        draft = JsonSerializer.Deserialize<Slot>(JsonSerializer.Serialize(slot, Storage.Json), Storage.Json)!;
        draft.BattleNet ??= new(); var profile = draft.BattleNet;
        Text = "Edit client — " + slot.Name; ClientSize = new(700, 620); StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = MaximizeBox = false;
        Font = new("Segoe UI", 10); BackColor = AppTheme.Surface; ForeColor = Color.White;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new(SizeType.Absolute, 45)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 65)); root.RowStyles.Add(new(SizeType.Absolute, 40)); Controls.Add(root);
        var nameRow = Row(); nameRow.Controls.Add(Label("Client name", 30, 105));
        var name = new TextBox { Text = draft.Name, Width = 460, MaxLength = 80, Name = "ClientName" }; nameRow.Controls.Add(name); root.Controls.Add(nameRow, 0, 0);
        var tabs = new DarkTabControl { Dock = DockStyle.Fill, Name = "ClientEditorTabs", Padding = new(16, 7) }; root.Controls.Add(tabs, 0, 1);
        var launch = Page("Launch"); var advanced = Page("Advanced"); tabs.TabPages.Add(launch.Page);
        launch.Body.Controls.Add(Label("Launch method")); method.Items.AddRange(new object[] { "Direct / private server", "Battle.net" }); method.SelectedIndex = (int)slot.LaunchMethod; launch.Body.Controls.Add(method);
        var host = new Panel { Width = 600, Height = 280, Margin = Padding.Empty }; launch.Body.Controls.Add(host);
        var direct = Body(); var bnet = Body(); host.Controls.Add(direct); host.Controls.Add(bnet);
        var useDefault = new CheckBox { Text = "Use this layout's default executable", Checked = string.IsNullOrWhiteSpace(draft.Executable), AutoSize = true, Name = "UseLayoutExecutable", Margin = new(0, 4, 0, 10) }; direct.Controls.Add(useDefault);
        direct.Controls.Add(Label("Game executable")); var pathRow = Row(); var directPath = new TextBox { Text = draft.PathFor(new Layout { Executable = layout.Executable }), Width = 450, Name = "DirectExecutable" };
        // A slot being changed from Battle.net should use its saved direct override or the layout default.
        directPath.Text = useDefault.Checked ? layout.Executable : draft.Executable;
        var browse = Button("Browse…", 108); pathRow.Controls.AddRange(new Control[] { directPath, browse }); direct.Controls.Add(pathRow);
        direct.Controls.Add(Label("Layout → Direct-launch defaults sets the shared executable and arguments.", 43));
        var credentials = Button("Configure private-server login…", 310); credentials.Name = "PrivateLogin"; direct.Controls.Add(credentials);
        direct.Controls.Add(Label(slot.Login is null ? "No private-server login saved." : "A private-server login is saved for this client.", 40));
        void ShowDirectPath() { directPath.Enabled = browse.Enabled = !useDefault.Checked; if (useDefault.Checked) directPath.Text = layout.Executable; }
        useDefault.CheckedChanged += (_, _) => ShowDirectPath(); ShowDirectPath();
        browse.Click += (_, _) => { using var picker = new OpenFileDialog { Title = "Choose this client's game executable", Filter = "Windows executables (*.exe)|*.exe", CheckFileExists = true }; if (File.Exists(directPath.Text)) picker.FileName = directPath.Text; if (picker.ShowDialog(this) == DialogResult.OK) directPath.Text = picker.FileName; };
        bnet.Controls.Add(Label("Installed WoW version")); bnet.Controls.Add(installation);
        bnet.Controls.Add(Label("Battle.net connection")); bnet.Controls.Add(connection);
        bnet.Controls.Add(Label("WoW account and region")); bnet.Controls.Add(account);
        bnet.Controls.Add(Label("Manage shared folders and logins in Settings → WoW folders / Battle.net connections.", 52));
        installation.AccessibleName = "Installed WoW version"; connection.AccessibleName = "Battle.net connection"; account.AccessibleName = "WoW account";
        installation.FormattingEnabled = true; installation.Format += (_, e) =>
        {
            if (e.ListItem is WoWInstallation i) e.Value = i.GameVersion + (i.Build.Length > 0 ? " · " + i.Build : "")
                + (installation.Items.Cast<WoWInstallation>().Count(x => x.GameVersion == i.GameVersion) > 1 ? " · " + i.RootFolder : "");
        };
        advanced.Body.Controls.Add(Label("Battle.net version label")); advanced.Body.Controls.Add(versionName);
        advanced.Body.Controls.Add(Label("Change this only if the detected version name differs from the name shown in the English Battle.net launcher.", 55));
        var check = Button("Check launcher selection", 260); advanced.Body.Controls.Add(check);
        advanced.Body.Controls.Add(Label("Selects the version and account in Battle.net without pressing Play.", 48));
        var legacyRefresh = Button("Refresh legacy launcher choices", 310); advanced.Body.Controls.Add(legacyRefresh);
        root.Controls.Add(info, 0, 2);
        var actions = Row(); actions.FlowDirection = FlowDirection.RightToLeft;
        var save = Button("Save", 110); save.Name = "SaveLaunch"; var cancel = Button("Cancel", 110); cancel.DialogResult = DialogResult.Cancel;
        var delete = Button("Delete", 110); delete.Name = "DeleteClient"; delete.ForeColor = Color.FromArgb(255, 128, 118); delete.Enabled = layout.Slots.Count > 1;
        delete.Click += (_, _) => { DialogResult = DialogResult.Abort; Close(); }; // Abort = delete this client (the main window asks to confirm)
        actions.Controls.AddRange(new Control[] { save, cancel, delete }); root.Controls.Add(actions, 0, 3); AcceptButton = save; CancelButton = cancel; AppTheme.Style(this); DialogFrame.Apply(this);
        void ShowMethod()
        {
            bool battleNet = method.SelectedIndex == 1; bnet.Visible = battleNet; direct.Visible = !battleNet; (battleNet ? (Control)bnet : direct).BringToFront();
            if (battleNet) { if (!tabs.TabPages.Contains(advanced.Page)) tabs.TabPages.Add(advanced.Page); }
            else tabs.TabPages.Remove(advanced.Page);
        }
        void ReadFields()
        {
            draft.Name = name.Text.Trim(); draft.Executable = useDefault.Checked ? "" : directPath.Text.Trim().Trim('"');
            draft.LaunchMethod = (ClientLaunchMethod)method.SelectedIndex;
            if (installation.SelectedItem is WoWInstallation selected) { profile.Executable = selected.Executable; profile.GameTabId = selected.GameTabId; profile.Product = selected.Product; }
            profile.GameVersion = versionName.Text.Trim();
            if (connection.SelectedItem is BattleNetConnection connected) { profile.ConnectionId = connected.Id; profile.ConnectionName = connected.Name; }
            else { profile.ConnectionId = ""; profile.ConnectionName = ""; }
            if (account.SelectedItem is WoWLicense license) { profile.AccountName = license.Label; profile.WebAccountId = license.Id; }
            else { profile.AccountName = account.SelectedItem as string ?? ""; profile.WebAccountId = ""; }
        }
        void Changed()
        {
            if (loading) return;
            string before = profile.GameVersion + "|" + profile.AccountKey + "|" + profile.Executable;
            ReadFields(); if (before != profile.GameVersion + "|" + profile.AccountKey + "|" + profile.Executable) profile.ClearBinding();
            legacyRefresh.Visible = connection.SelectedItem is string; info.Text = "Changes apply to this client when you save.";
        }
        void PopulateInstallations(string path)
        {
            installation.Items.Clear(); installation.Items.AddRange(settings.WoWLibrary.Installations.Cast<object>().ToArray());
            var selected = settings.WoWLibrary.Installations.FirstOrDefault(i => Matching.SamePath(i.Executable, path));
            if (selected is null && path.Length > 0) { selected = new() { Executable = path, Product = profile.Product, GameVersion = profile.GameVersion, Build = "saved selection", RootFolder = WoWInstallationScanner.RootForExecutable(path) ?? "" }; installation.Items.Add(selected); }
            installation.SelectedItem = selected; if (selected is null && installation.Items.Count == 1) installation.SelectedIndex = 0;
            if (installation.SelectedItem is WoWInstallation current) { versionName.Text = current.GameVersion; if (profile.GameVersion.Length > 0 && Matching.SamePath(path, current.Executable)) versionName.Text = profile.GameVersion; }
        }
        void PopulateAccounts(string selectedId, string selectedLabel)
        {
            account.Items.Clear();
            if (connection.SelectedItem is BattleNetConnection selected) { account.Items.AddRange(selected.Accounts.Cast<object>().ToArray()); account.SelectedItem = selected.Accounts.FirstOrDefault(a => a.Id == selectedId); }
            else if (connection.SelectedItem is string)
            {
                if (settings.BattleNetCatalog.ForVersion(versionName.Text) is { } legacy) account.Items.AddRange(legacy.Accounts.Cast<object>().ToArray());
                if (selectedLabel.Length > 0 && !account.Items.Contains(selectedLabel)) account.Items.Add(selectedLabel);
                if (selectedLabel.Length > 0) account.SelectedItem = selectedLabel;
            }
        }
        void PopulateConnections(string id)
        {
            connection.Items.Clear(); connection.Items.AddRange(settings.BattleNetConnections.Cast<object>().ToArray());
            if (profile.ConnectionId.Length == 0 && profile.AccountName.Length > 0) connection.Items.Add("Current launcher · existing setup");
            connection.SelectedItem = settings.BattleNetConnections.FirstOrDefault(c => c.Id == id);
            if (connection.SelectedIndex < 0 && id.Length == 0 && profile.AccountName.Length > 0) connection.SelectedItem = "Current launcher · existing setup";
            else if (connection.SelectedIndex < 0 && connection.Items.Count == 1) connection.SelectedIndex = 0;
        }
        async Task ReadLauncher(bool refreshChoices)
        {
            if (reading) return; ReadFields();
            if (!refreshChoices && profile.AccountName.Length == 0) { info.Text = "Choose an account on the Launch tab first."; return; }
            reading = true; save.Enabled = tabs.Enabled = false;
            try
            {
                if (!refreshChoices) BattleNetConnectionSettings.ValidateSelection(settings, profile);
                if (refreshChoices)
                {
                    var choices = await BattleNetUi.Run(ui => ui.ReadChoices(profile.GameVersion), cancellation.Token); if (IsDisposed) return;
                    settings.BattleNetCatalog.Remember(choices); saveShared(); loading = true; PopulateAccounts(profile.WebAccountId, profile.AccountName); loading = false; Changed(); info.Text = "Legacy choices refreshed. Choose this client's account on the Launch tab.";
                }
                else { string selected = await BattleNetUi.Run(ui => ui.Prepare(profile), cancellation.Token); if (!IsDisposed) info.Text = "Matched " + selected + ". No game was launched."; }
            }
            catch (Exception ex) { if (!IsDisposed) info.Text = ex.Message; }
            finally { reading = false; if (!IsDisposed) { save.Enabled = tabs.Enabled = true; Activate(); } else cancellation.Dispose(); }
        }
        method.SelectedIndexChanged += (_, _) => ShowMethod();
        installation.SelectedIndexChanged += (_, _) => { if (loading) return; if (installation.SelectedItem is WoWInstallation selected) { loading = true; versionName.Text = selected.GameVersion; if (connection.SelectedItem is string) PopulateAccounts("", ""); loading = false; Changed(); } };
        versionName.TextChanged += (_, _) => Changed(); account.SelectedIndexChanged += (_, _) => Changed();
        connection.SelectedIndexChanged += (_, _) => { if (loading) return; loading = true; PopulateAccounts("", ""); loading = false; Changed(); };
        check.Click += async (_, _) => await ReadLauncher(false); legacyRefresh.Click += async (_, _) => await ReadLauncher(true);
        credentials.Click += (_, _) => { ReadFields(); if (!File.Exists(draft.PathFor(layout))) { info.Text = "Choose an existing executable before configuring login."; return; } draft.LaunchMethod = ClientLaunchMethod.Direct; DialogResult = DialogResult.Retry; Close(); };
        save.Click += (_, _) =>
        {
            ReadFields();
            if (draft.Name.Length == 0) { info.Text = "Give this client a name."; return; }
            if (draft.LaunchMethod == ClientLaunchMethod.BattleNet && (!profile.IsConfigured || profile.AccountName.Length == 0 || !File.Exists(profile.Executable))) { info.Text = "Choose a version, connection and account. Add missing shared options through the Settings menu."; tabs.SelectedTab = launch.Page; return; }
            if (draft.LaunchMethod == ClientLaunchMethod.Direct && !File.Exists(draft.PathFor(layout))) { info.Text = "Choose a game executable or set the layout's direct-launch default."; return; }
            DialogResult = DialogResult.OK; Close();
        };
        FormClosing += (_, _) => cancellation.Cancel(); FormClosed += (_, _) => { if (!reading) cancellation.Dispose(); };
        PopulateInstallations(profile.Executable); PopulateConnections(profile.ConnectionId); PopulateAccounts(profile.WebAccountId, profile.AccountName); ShowMethod(); legacyRefresh.Visible = connection.SelectedItem is string; loading = false;
        info.Text = "Choose how this client starts. Shared setup lives in Settings.";
    }
    private static Label Label(string text, int height = 25, int width = 570) => new() { Text = text, Width = width, Height = height, ForeColor = Color.FromArgb(200, 206, 216), Margin = new(0, 0, 0, 4) };
    private static ComboBox Combo(string name) => new DarkComboBox { Name = name, Width = 570, Margin = new(0, 0, 0, 12) };
    private static FlowLayoutPanel Row() => new() { Width = 595, Height = 40, WrapContents = false, Margin = Padding.Empty };
    private static FlowLayoutPanel Body() => new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    private static (TabPage Page, FlowLayoutPanel Body) Page(string title)
    {
        var page = new TabPage(title) { BackColor = AppTheme.Surface, Padding = new(14) }; var body = Body(); page.Controls.Add(body); return (page, body);
    }
    private static Button Button(string text, int width) => new() { Text = text, Width = width, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = AppTheme.ButtonFace, ForeColor = Color.White, Margin = new(0, 0, 8, 8) };
}
