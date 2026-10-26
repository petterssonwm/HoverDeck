using System.Text.Json;

namespace HoverDeck;

internal static class AccountRemovalTests
{
    public static void Run(Action<string, bool> check)
    {
        var first = new BattleNetConnection { Name = "Login A", RefreshedUtc = "fixture", Accounts = new() { new() { Id = "eu-1", Name = "WoW1", Region = "EU" }, new() { Id = "us-1", Name = "WoW1", Region = "US" } } };
        var second = new BattleNetConnection { Name = "Login B", Accounts = new() { new() { Id = "eu-1", Name = "WoW1", Region = "EU" } } };
        Slot SlotFor(BattleNetConnection connection, WoWLicense account) => new()
        {
            LaunchMethod = ClientLaunchMethod.BattleNet, X = -1440, Y = 286, Width = 1440, Height = 1483,
            BattleNet = new() { ConnectionId = connection.Id, ConnectionName = connection.Name, WebAccountId = account.Id, AccountName = account.Label,
                GameTabId = "game-nav-btn-WoW", GameVersion = "Classic", Executable = @"C:\Fixture\Wow.exe", BoundIdentity = "fixture-process", PendingSinceUtc = "fixture-pending", BeforeLaunch = new() { "fixture-before" }, ResolvedAccount = account.Label }
        };
        var affected = SlotFor(first, first.Accounts[0]); var otherRegion = SlotFor(first, first.Accounts[1]); var otherLogin = SlotFor(second, second.Accounts[0]);
        var copy = SlotFor(first, first.Accounts[0]);
        var settings = new Settings { BattleNetConnections = new() { first, second }, Layouts = new() { new() { Slots = new() { affected, otherRegion, otherLogin } }, new() { Name = "Other layout", Slots = new() { copy } } } };
        bool confirm = false, failCleanup = false; int saved = 0, cleared = 0;
        using (var form = new BattleNetConnectionsForm(settings, () => saved++, first.Id, (_, _) => confirm,
            _ => { cleared++; return failCleanup ? Task.FromException(new InvalidOperationException("Synthetic cleanup failure")) : Task.CompletedTask; }))
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show();
            var accounts = (ListBox)form.Controls.Find("ConnectionAccounts", true).Single();
            var connections = (ListBox)form.Controls.Find("Connections", true).Single();
            var removeAccount = (Button)form.Controls.Find("RemoveAccount", true).Single();
            var removeLogin = (Button)form.Controls.Find("RemoveConnection", true).Single();
            var removedView = (CheckBox)form.Controls.Find("ShowRemovedAccounts", true).Single();
            accounts.SelectedIndex = 0; removeAccount.PerformClick();
            check("Canceling account removal keeps the account and all client selections", accounts.Items.Count == 2 && affected.BattleNet!.IsConfigured && saved == 0);
            confirm = true; var deleted = first.Accounts[0]; var imported = first.Accounts.ToList(); removeAccount.PerformClick();
            check("Removing an account updates the management list and saves immediately", accounts.Items.Count == 1 && first.RemovedAccounts.Single().Id == deleted.Id && saved == 1);
            check("Account removal clears selections and window bindings across every saved layout", new[] { affected, copy }.All(s => s.BattleNet is { WebAccountId.Length: 0, AccountName.Length: 0, BoundIdentity.Length: 0, PendingSinceUtc.Length: 0, BeforeLaunch.Count: 0, ResolvedAccount.Length: 0, IsConfigured: false }));
            check("Removed selections cannot fall back to a legacy WoW number", affected.BattleNet!.AccountNumber == 0 && affected.BattleNet.AccountLabel == "Choose an account");
            check("Removal preserves geometry, installed version, other regions and other logins", affected.Bounds == new Rectangle(-1440, 286, 1440, 1483) && affected.BattleNet.GameVersion == "Classic" && otherRegion.BattleNet!.IsConfigured && otherLogin.BattleNet!.IsConfigured);
            using (var editor = new ClientLaunchForm(affected, settings.Layouts[0], settings))
            {
                var choices = (ComboBox)editor.Controls.Find("BattleNetAccounts", true).Single();
                check("Removed accounts disappear from client dropdowns without selecting a replacement", choices.Items.Count == 1 && choices.SelectedIndex == -1 && choices.Items[0] is WoWLicense remaining && remaining.Id != deleted.Id);
            }
            var roundTrip = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings, Storage.Json), Storage.Json)!;
            var restoredConnection = roundTrip.BattleNetConnections[0]; BattleNetConnectionSettings.ApplyImport(restoredConnection, new(imported, 0));
            check("Removed accounts stay excluded after saving, restarting and refreshing", restoredConnection.Accounts.Count == 1 && restoredConnection.Accounts.All(a => a.Id != deleted.Id) && restoredConnection.RemovedAccounts.Count == 1);
            removedView.Checked = true; accounts.SelectedIndex = 0; removeAccount.PerformClick();
            check("Explicit Restore makes the account available without restoring old client selections", first.Accounts.Count == 2 && first.RemovedAccounts.Count == 0 && accounts.Items.Count == 0 && !affected.BattleNet.IsConfigured && saved == 2);
            failCleanup = true; removeLogin.PerformClick();
            check("Failed sign-in cleanup keeps the login and its choices available", settings.BattleNetConnections.Count == 2 && connections.Items.Count == 2 && saved == 2 && cleared == 1);
            failCleanup = false; removeLogin.PerformClick();
            check("Removing a login removes its choices and clears all its client references", settings.BattleNetConnections.Count == 1 && connections.Items.Count == 1 && new[] { affected, copy, otherRegion }.All(s => s.BattleNet is { ConnectionId.Length: 0, ConnectionName.Length: 0, AccountName.Length: 0, WebAccountId.Length: 0, IsConfigured: false }) && saved == 3 && cleared == 2);
            check("Removing a login preserves another login with identical license names and IDs", ReferenceEquals(settings.BattleNetConnections[0], second) && otherLogin.BattleNet!.IsConfigured);
            using (var editor = new ClientLaunchForm(affected, settings.Layouts[0], settings))
            {
                var choices = (ComboBox)editor.Controls.Find("BattleNetConnection", true).Single();
                var licenses = (ComboBox)editor.Controls.Find("BattleNetAccounts", true).Single();
                check("Client editor no longer offers a removed login or a stale legacy selection", choices.Items.Count == 1 && ReferenceEquals(choices.Items[0], second) && licenses.SelectedIndex == -1 && !editor.Result.BattleNet!.IsConfigured);
            }
            removeLogin.PerformClick();
            check("Removing the final login clears both lists and disables removal", connections.Items.Count == 0 && accounts.Items.Count == 0 && !removeLogin.Enabled && !removeAccount.Enabled);
            form.Close();
        }
    }
}
