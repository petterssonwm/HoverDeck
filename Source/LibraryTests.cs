using System.Text.Json;

namespace HoverDeck;

// Synthetic metadata and account responses only. No personal settings, sessions or credentials.
internal static class LibraryTests
{
    public static void Run(Action<string, bool> check, BattleNetCatalog catalog, BattleNetChoices choices)
    {
        AccountRemovalTests.Run(check);
        var oldSlot = new Slot { BattleNet = new() { GameTabId = choices.GameTabId, GameVersion = choices.GameVersion, AccountName = "WoW2 (US)", Executable = @"C:\Games\Classic\WowClassic.exe", BoundIdentity = "saved-process" } };
        using (var form = new ClientLaunchForm(oldSlot, new(), new() { BattleNetCatalog = catalog }))
        {
            var accounts = (ComboBox)form.Controls.Find("BattleNetAccounts", true).Single();
            check("Existing launcher profiles retain selected account and live assignment", accounts.Text == "WoW2 (US)" && form.Result.BattleNet!.BoundIdentity == "saved-process");
            accounts.SelectedItem = "WoW1 (EU)";
            check("Editing a legacy slot clears only its draft assignment", form.Result.BattleNet!.BoundIdentity == "" && oldSlot.BattleNet!.BoundIdentity == "saved-process");
        }
        var root = Path.Combine(Path.GetTempPath(), "HoverDeck-library-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string AddInstallation(string name, string product, string folder, string build)
            {
                string install = Path.Combine(root, name), version = Path.Combine(install, folder); Directory.CreateDirectory(version);
                File.WriteAllText(Path.Combine(install, ".build.info"), $"Active!DEC:1|Product!STRING:0|Version!STRING:0\n1|{product}|{build}\n0|wow_classic|inactive\n1|wow_classic_ptr|not-installed\n");
                File.WriteAllText(Path.Combine(version, ".flavor.info"), "Product Flavor!STRING:0\n" + product + "\n");
                File.WriteAllText(Path.Combine(version, "WowClassic.exe"), "fixture, not executable"); return install;
            }
            string first = AddInstallation("DriveA", "wow_classic_era", "_classic_era_", "1.15.9.12345");
            string second = AddInstallation("DriveB", "wow", "_retail_", "12.0.0.12345");
            string third = AddInstallation("AnotherCopy", "wow_classic_era", "_classic_era_", "1.15.9.56789");
            var scan = WoWInstallationScanner.Scan(new[] { first, second, third, first.ToUpperInvariant(), Path.Combine(root, "missing") });
            check("Multiple folders discover installed versions and retain distinct copies", scan.Installations.Count == 3 && scan.Installations.Select(i => i.Executable).Distinct().Count() == 3);
            check("Metadata headers and active rows are parsed; missing executables are excluded", scan.Installations.All(i => i.Build != "inactive" && i.Build != "not-installed") && scan.Installations.Any(i => i.Product == "wow_classic_era" && i.Build == "1.15.9.12345"));
            check("Unavailable folders are reported without losing valid installations", scan.Messages.Any(m => m.Contains("unavailable")) && scan.Installations.Count == 3);
            check("Selecting a version subfolder locates its parent metadata", WoWInstallationScanner.Scan(new[] { Path.Combine(first, "_classic_era_") }).Installations.Count == 1);
            check("A containing folder finds installation roots without a whole-drive scan", WoWInstallationScanner.Scan(new[] { root }).Installations.Count == 3);
            string shared = AddInstallation("World of Warcraft", "wow_classic_era", "_classic_era_", "1.15.9.12345");
            foreach (var entry in new[] { (Product: "wow", Folder: "_retail_"), (Product: "wow_future_test", Folder: "NewVersion") })
            {
                File.AppendAllText(Path.Combine(shared, ".build.info"), $"1|{entry.Product}|new-build\n");
                string directory = Path.Combine(shared, entry.Folder); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, ".flavor.info"), "Product Flavor!STRING:0\n" + entry.Product);
                File.WriteAllText(Path.Combine(directory, "Wow.exe"), "fixture, not executable");
            }
            var siblings = WoWInstallationScanner.Scan(new[] { shared }).Installations;
            check("Choosing one WoW root discovers every installed sibling version", siblings.Count == 3 && siblings.Select(i => i.Product).Order().SequenceEqual(new[] { "wow", "wow_classic_era", "wow_future_test" }));
            check("All discovered versions retain the selected root folder", siblings.All(i => i.RootFolder == shared));
            string classicFolder = Path.Combine(shared, "_classic_era_");
            check("Choosing Classic Era's subfolder expands to its WoW root and all siblings", WoWInstallationScanner.NormalizeRoot(classicFolder) == shared && WoWInstallationScanner.Scan(new[] { classicFolder }).Installations.Count == 3);
            string classicExecutable = Path.Combine(classicFolder, "WowClassic.exe");
            check("An older per-client executable seeds its parent WoW root once", WoWInstallationScanner.LibraryRoots(new(), new[] { classicExecutable, classicExecutable }).SequenceEqual(new[] { shared }));
            check("Intentionally removing all roots does not re-add old installations", WoWInstallationScanner.LibraryRoots(new() { RootFoldersInitialized = true }, new[] { classicExecutable }).Count == 0);
            using (var rootForm = new ClientLaunchForm(new() { LaunchMethod = ClientLaunchMethod.BattleNet }, new(), new() { WoWLibrary = new() { Folders = new() { shared }, Installations = siblings } }))
            {
                var versionOptions = (ComboBox)rootForm.Controls.Find("BattleNetVersions", true).Single();
                check("The client editor offers detected versions without duplicating shared folder controls", rootForm.Controls.Find("WoWRootFolders", true).Length == 0 && versionOptions.Items.Count == 3 && versionOptions.Items.Cast<WoWInstallation>().All(i => !i.ToString().Contains("_classic_era_")));
            }
            const string response = """
                {"gameAccounts":[
                  {"titleId":5730135,"gameAccountName":"WoW1","gameAccountRegion":"EU","gameAccountUniqueId":{"gameAccountId":100,"gameServiceRegionId":3,"programId":5730135}},
                  {"titleId":5730135,"gameAccountName":"WoW1","gameAccountRegion":"Americas","gameAccountUniqueId":{"gameAccountId":100,"gameServiceRegionId":1,"programId":5730135}},
                  {"titleId":999,"gameAccountName":"Other game","gameAccountRegion":"EU","gameAccountUniqueId":{"gameAccountId":101,"gameServiceRegionId":3,"programId":999}},
                  null
                ]}
                """;
            var licenses = BattleNetAccountData.Parse(response);
            check("Web account import filters to WoW and preserves distinct regional IDs", licenses.Count == 2 && licenses.Select(a => a.Id).Distinct().Count() == 2 && licenses.Select(a => a.Label).Order().SequenceEqual(new[] { "WoW1 (EU)", "WoW1 (US)" }));
            bool rejected = false; try { BattleNetAccountData.Parse("{\"authenticated\":false}"); } catch (InvalidOperationException) { rejected = true; }
            check("An expired web session is not accepted as an empty account list", rejected);
            var ptr = BattleNetAccountData.Parse(response.Replace("\"EU\"", "\"PTR\""));
            check("PTR accounts import alongside ordinary regional accounts", ptr.Count == 2 && ptr.Any(a => a.Region == "PTR") && ptr.Any(a => a.Region == "US"));
            var unknown = BattleNetAccountData.Parse(response.Replace("\"EU\"", "\"new_region\""));
            check("New region labels remain distinct instead of being mapped to another region", unknown.Any(a => a.Region == "NEW_REGION") && !unknown.Any(a => a.Region == "EU"));
            var partial = BattleNetAccountData.ParseDetailed(response.Replace("\"gameAccountId\":100,\"gameServiceRegionId\":3", "\"gameAccountId\":0,\"gameServiceRegionId\":3"));
            check("An incomplete license is reported without blocking other valid licenses", partial.Accounts.Count == 1 && partial.SkippedAccounts == 1);
            rejected = false; try { BattleNetAccountData.Parse(response.Replace("\"gameAccountName\":\"WoW1\"", "\"gameAccountName\":\"\"")); } catch (InvalidOperationException) { rejected = true; }
            check("Entirely invalid responses fail instead of replacing a saved list with nothing", rejected);
            var one = new BattleNetConnection { Name = "Login A", Accounts = licenses };
            var two = new BattleNetConnection { Name = "Login B", Accounts = new() { new() { Id = "5730135:3:200", Name = "WoW1", Region = "EU" } } };
            check("Separate logins have separate web profile folders", BattleNetAccountData.SessionFolder(one.Id) != BattleNetAccountData.SessionFolder(two.Id));
            rejected = false; try { BattleNetAccountData.SessionFolder("../../elsewhere"); } catch (InvalidOperationException) { rejected = true; }
            check("Invalid connection IDs cannot select arbitrary browser profile paths", rejected);
            check("Only the fixed HTTPS account endpoint may be read", BattleNetWebForm.IsAccountsPage("https://account.battle.net/api/games-and-subs") && !BattleNetWebForm.IsAccountsPage("http://account.battle.net/api/games-and-subs") && !BattleNetWebForm.IsAccountsPage("https://other.example/api/games-and-subs") && !BattleNetWebForm.IsAccountsPage("https://account.battle.net/api/games-and-subs?next=elsewhere"));
            var settings = new Settings { WoWLibrary = new() { Folders = new() { first, second, third }, Installations = scan.Installations }, BattleNetConnections = new() { one, two } };
            var installed = scan.Installations[0]; var chosen = licenses[0];
            var slot = new Slot { LaunchMethod = ClientLaunchMethod.BattleNet, BattleNet = new() { GameTabId = installed.GameTabId, GameVersion = installed.GameVersion, Product = installed.Product, Executable = installed.Executable, ConnectionId = one.Id, ConnectionName = one.Name, AccountName = chosen.Label, WebAccountId = chosen.Id, BoundIdentity = "known-process" } };
            using (var form = new ClientLaunchForm(slot, new(), settings))
            {
                var pages = (TabControl)form.Controls.Find("ClientEditorTabs", true).Single();
                check("Client editor offers only Launch and Advanced tabs", pages.TabPages.Cast<TabPage>().Select(p => p.Text).SequenceEqual(new[] { "Launch", "Advanced" }));
                var accounts = (ComboBox)form.Controls.Find("BattleNetAccounts", true).Single();
                var connections = (ComboBox)form.Controls.Find("BattleNetConnection", true).Single();
                var versions = (ComboBox)form.Controls.Find("BattleNetVersions", true).Single();
                check("Slot editor preserves its connection, account and assignment", ReferenceEquals(connections.SelectedItem, one) && ReferenceEquals(accounts.SelectedItem, chosen) && form.Result.BattleNet!.BoundIdentity == "known-process");
                check("Version selector contains only configured installations", versions.Items.Count == 3 && versions.Items.Cast<WoWInstallation>().All(i => File.Exists(i.Executable)));
                connections.SelectedItem = two;
                check("Switching logins replaces the account list and requires a new selection", accounts.Items.Count == 1 && ReferenceEquals(accounts.Items[0], two.Accounts[0]) && accounts.SelectedIndex == -1 && form.Result.BattleNet!.WebAccountId == "" && form.Result.BattleNet.BoundIdentity == "");
                accounts.SelectedIndex = 0;
                check("Identical labels under different logins retain different identities", form.Result.BattleNet!.AccountLabel == slot.BattleNet!.AccountLabel && form.Result.BattleNet.AccountKey != slot.BattleNet.AccountKey && slot.BattleNet.BoundIdentity == "known-process");
            }
            using (var form = new ClientLaunchForm(new(), new(), settings))
            {
                var connections = (ComboBox)form.Controls.Find("BattleNetConnection", true).Single(); connections.SelectedItem = one;
                var accounts = (ComboBox)form.Controls.Find("BattleNetAccounts", true).Single();
                check("A second slot shares cached accounts without inheriting the first selection", accounts.Items.Count == 2 && accounts.SelectedIndex == -1);
            }
            settings.Layouts.Add(new() { Slots = new() { slot } });
            var restored = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings, Storage.Json), Storage.Json)!;
            check("Folder lists, separate connections and per-slot license IDs persist", restored.WoWLibrary.Installations.Count == 3 && restored.BattleNetConnections.Count == 2 && restored.Layouts[0].Slots[0].BattleNet!.WebAccountId == chosen.Id);
            var web = new BattleNetLaunch { AccountName = "WoW1 (EU)", WebAccountId = chosen.Id };
            check("Web license names match the launcher's Starter Edition suffix", BattleNetAccounts.Resolve(web, new[] { "WoW1 (EU) - Starter Edition", "WoW10 (EU)", "WoW1 (US)" }) == "WoW1 (EU) - Starter Edition");
            rejected = false; try { BattleNetAccounts.Resolve(web, new[] { "WoW10 (EU)", "WoW1 (US)" }); } catch (InvalidOperationException) { rejected = true; }
            check("Web license matching never substitutes another number or region", rejected);
            var directSlot = new Slot { Name = "Original", Executable = classicExecutable };
            using (var editor = new ClientLaunchForm(directSlot, new(), settings))
            {
                var pages = (TabControl)editor.Controls.Find("ClientEditorTabs", true).Single();
                check("Direct clients only show relevant launch and login controls", pages.TabPages.Count == 1 && editor.Controls.Find("DirectExecutable", true).Length == 1);
                ((TextBox)editor.Controls.Find("ClientName", true).Single()).Text = "Renamed";
                editor.ShowInTaskbar = false; editor.Opacity = 0; editor.Show();
                ((Button)editor.Controls.Find("SaveLaunch", true).Single()).PerformClick();
                check("Client editor saves name and direct executable to a draft without changing the original", editor.Result.Name == "Renamed" && editor.Result.Executable == classicExecutable && directSlot.Name == "Original");
            }
        }
        finally
        {
            // The unique test directory is created here and contains only synthetic fixtures.
            Directory.Delete(root, true);
        }
    }
}
