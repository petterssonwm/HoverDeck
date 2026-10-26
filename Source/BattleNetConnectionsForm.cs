namespace HoverDeck;

internal sealed class BattleNetConnectionsForm : Form
{
    public string SelectedId { get; private set; } = "";
    public BattleNetConnectionsForm(Settings settings, Action save, string selectedId,
        Func<string, string, bool>? confirmRemoval = null, Func<BattleNetConnection, Task>? forgetSession = null)
    {
        var connections = settings.BattleNetConnections;
        confirmRemoval ??= (message, title) => MessageBox.Show(this, message, title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
        forgetSession ??= connection => BattleNetWebForm.ForgetSession(this, connection);
        Text = "Battle.net connections"; ClientSize = new(860, 570); StartPosition = FormStartPosition.CenterParent; Font = new("Segoe UI", 10);
        FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = MaximizeBox = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(18), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new(SizeType.Absolute, 65)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 65)); root.RowStyles.Add(new(SizeType.Absolute, 40)); Controls.Add(root);
        root.Controls.Add(new Label { Text = "Choose a Battle.net login to manage its WoW accounts. Changes are shared by all client slots.\nRemoving items here only changes HoverDeck. Your Blizzard accounts are not deleted.", Dock = DockStyle.Fill }, 0, 0);
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        columns.ColumnStyles.Add(new(SizeType.Percent, 50)); columns.ColumnStyles.Add(new(SizeType.Percent, 50)); root.Controls.Add(columns, 0, 1);
        TableLayoutPanel Group(string title, int column)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = column == 0 ? new(0, 0, 12, 0) : Padding.Empty };
            panel.RowStyles.Add(new(SizeType.Absolute, 28)); panel.RowStyles.Add(new(SizeType.Percent, 100)); panel.RowStyles.Add(new(SizeType.Absolute, 42)); panel.RowStyles.Add(new(SizeType.Absolute, 42));
            panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill }, 0, 0); columns.Controls.Add(panel, column, 0); return panel;
        }
        var left = Group("Battle.net logins", 0); var right = Group("WoW accounts for the selected login", 1);
        var list = new ListBox { Name = "Connections", Dock = DockStyle.Fill, IntegralHeight = false };
        var accounts = new ListBox { Name = "ConnectionAccounts", Dock = DockStyle.Fill, IntegralHeight = false };
        left.Controls.Add(list, 0, 1); right.Controls.Add(accounts, 0, 1);
        var loginActions = Row(); var loginEdit = Row(); left.Controls.Add(loginActions, 0, 2); left.Controls.Add(loginEdit, 0, 3);
        var add = Button("Add login…", "AddConnection", 105); var connect = Button("Sign in / import…", "ConnectLogin", 155); var refresh = Button("Refresh", "RefreshConnection", 90);
        loginActions.Controls.AddRange(new Control[] { add, connect, refresh });
        var rename = Button("Rename…", "RenameConnection", 105); var removeLogin = Button("Remove login…", "RemoveConnection", 155); loginEdit.Controls.AddRange(new Control[] { rename, removeLogin });
        var showRemoved = new CheckBox { Name = "ShowRemovedAccounts", Text = "Show removed accounts", AutoSize = true, Dock = DockStyle.Fill };
        right.Controls.Add(showRemoved, 0, 2); var accountActions = Row(); right.Controls.Add(accountActions, 0, 3);
        var removeAccount = Button("Remove account…", "RemoveAccount", 180); accountActions.Controls.Add(removeAccount);
        var status = new Label { Name = "ConnectionStatus", Dock = DockStyle.Fill, Padding = new(0, 8, 0, 0) }; root.Controls.Add(status, 0, 2);
        var close = Button("Done", "CloseConnections", 110); root.Controls.Add(close, 0, 3); close.Click += (_, _) => Close(); CancelButton = close;
        bool busy = false;
        void UpdateActions()
        {
            bool selected = list.SelectedItem is BattleNetConnection;
            connect.Enabled = refresh.Enabled = rename.Enabled = removeLogin.Enabled = selected;
            removeAccount.Enabled = accounts.SelectedItem is WoWLicense; showRemoved.Enabled = selected;
            removeAccount.Text = showRemoved.Checked ? "Restore account" : "Remove account…";
        }
        void PopulateAccounts(string accountId = "")
        {
            accounts.Items.Clear(); SelectedId = (list.SelectedItem as BattleNetConnection)?.Id ?? "";
            if (list.SelectedItem is BattleNetConnection selected)
            {
                var source = showRemoved.Checked ? selected.RemovedAccounts : selected.Accounts;
                accounts.Items.AddRange(source.Cast<object>().ToArray()); accounts.SelectedItem = source.FirstOrDefault(a => a.Id == accountId);
                status.Text = selected.RefreshedUtc.Length == 0 ? "Connection saved. Use Sign in / import to finish connecting."
                    : $"{selected.Accounts.Count} available · {selected.RemovedAccounts.Count} removed. Removed accounts stay out of client choices after Refresh."
                    + (selected.SkippedAccounts > 0 ? $"\n{selected.SkippedAccounts} incomplete license(s) could not be imported." : "");
                if (showRemoved.Checked) status.Text = "Select a removed account and choose Restore account to make it available again. Client selections stay empty until you choose an account.";
            }
            else status.Text = "Add a Battle.net login to import its WoW accounts.";
            UpdateActions();
        }
        void Populate(string id)
        {
            list.Items.Clear(); list.Items.AddRange(connections.Cast<object>().ToArray()); list.SelectedItem = connections.FirstOrDefault(c => c.Id == id);
            if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
            PopulateAccounts();
        }
        list.SelectedIndexChanged += (_, _) => PopulateAccounts(); accounts.SelectedIndexChanged += (_, _) => UpdateActions(); showRemoved.CheckedChanged += (_, _) => PopulateAccounts();
        async Task RunOperation(Func<Task> action)
        {
            if (busy) return; busy = true; columns.Enabled = close.Enabled = false;
            try { await action(); }
            catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
            finally { busy = false; if (!IsDisposed) { columns.Enabled = close.Enabled = true; UpdateActions(); } }
        }
        async Task Import(BattleNetConnection connection, bool signIn, bool newlyAdded)
        {
            if (newlyAdded) { connections.Add(connection); save(); Populate(connection.Id); }
            status.Text = signIn ? "Complete sign-in in Blizzard's page, then import accounts." : "Refreshing this connection in the background…";
            var imported = await BattleNetWebForm.Fetch(this, connection, signIn); if (IsDisposed || imported is null) return;
            BattleNetConnectionSettings.ApplyImport(connection, imported); save(); Populate(connection.Id);
            if (imported.Accounts.Count == 0) status.Text = "Connected, but Blizzard returned no WoW licenses for this login.";
        }
        add.Click += async (_, _) => { string? name = AskName("Name this Battle.net login", "Battle.net " + (connections.Count + 1)); if (name is not null) await RunOperation(() => Import(new() { Name = name }, true, true)); };
        connect.Click += async (_, _) => { if (list.SelectedItem is BattleNetConnection selected) await RunOperation(() => Import(selected, true, false)); };
        refresh.Click += async (_, _) => { if (list.SelectedItem is BattleNetConnection selected) await RunOperation(() => Import(selected, false, false)); };
        rename.Click += (_, _) =>
        {
            if (list.SelectedItem is not BattleNetConnection selected) return;
            string? name = AskName("Connection name", selected.Name);
            if (name is not null)
            {
                selected.Name = name;
                foreach (var profile in settings.Layouts.SelectMany(l => l.Slots).Select(s => s.BattleNet).OfType<BattleNetLaunch>().Where(p => p.ConnectionId == selected.Id)) profile.ConnectionName = name;
                save(); Populate(selected.Id);
            }
        };
        removeAccount.Click += (_, _) =>
        {
            if (list.SelectedItem is not BattleNetConnection selected || accounts.SelectedItem is not WoWLicense account) return;
            if (showRemoved.Checked) BattleNetConnectionSettings.RestoreAccount(selected, account);
            else
            {
                int count = BattleNetConnectionSettings.UsageCount(settings, selected.Id, account.Id);
                if (!confirmRemoval($"Remove {account.Label} from HoverDeck?\n\nThis clears the account selection in {count} saved client(s). It stays removed after refreshing; use Show removed accounts to restore it.", "Remove WoW account")) return;
                BattleNetConnectionSettings.RemoveAccount(settings, selected, account);
            }
            save(); PopulateAccounts();
        };
        removeLogin.Click += async (_, _) =>
        {
            if (list.SelectedItem is not BattleNetConnection selected) return;
            int count = BattleNetConnectionSettings.UsageCount(settings, selected.Id);
            if (!confirmRemoval($"Remove '{selected.Name}' from HoverDeck?\n\nIts WoW accounts and saved web sign-in will be removed. This clears the login and account selections in {count} saved client(s).", "Remove Battle.net login")) return;
            await RunOperation(async () =>
            {
                status.Text = "Removing this login's saved web sign-in…"; await forgetSession(selected);
                BattleNetConnectionSettings.RemoveConnection(settings, selected); save(); Populate("");
            });
        };
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        Populate(selectedId); AppTheme.Apply(this);
    }
    private static FlowLayoutPanel Row() => new() { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
    private static Button Button(string text, string name, int width) => new() { Text = text, Name = name, Width = width, Height = 32, Margin = new(0, 4, 7, 4) };
    private string? AskName(string title, string value)
    {
        using var form = new Form { Text = title, ClientSize = new(420, 130), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        var name = new TextBox { Text = value, Bounds = new(18, 20, 380, 28), MaxLength = 80 }; var ok = new Button { Text = "Continue", Bounds = new(18, 70, 110, 32) }; var cancel = new Button { Text = "Cancel", Bounds = new(140, 70, 100, 32), DialogResult = DialogResult.Cancel };
        form.Controls.AddRange(new Control[] { name, ok, cancel }); form.AcceptButton = ok; form.CancelButton = cancel; AppTheme.Apply(form);
        ok.Click += (_, _) => { if (name.Text.Trim().Length > 0) form.DialogResult = DialogResult.OK; };
        return form.ShowDialog(this) == DialogResult.OK ? name.Text.Trim() : null;
    }
}

