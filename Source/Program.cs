using System.Diagnostics;
using System.Text.Json;

namespace HoverDeck;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Lets the installer and updater run their install/update/uninstall hooks; returns immediately otherwise.
        Velopack.VelopackApp.Build().Run();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length >= 3 && args[0] == "--watch") { HoverService.Watch(int.Parse(args[1]), long.Parse(args[2])); return; }
        if (args.Length >= 2 && args[0] == "--display-watch") { DisplayChangeSession.Watch(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--inspect")
        {
            var data = new { Monitors = Native.Monitors(), Windows = Native.Windows(discover: true).Select(w => new { Handle = w.Handle.ToInt64(), w.ProcessId, w.Started, w.Executable, w.Title, w.Bounds, w.Minimized }), Hover = new { Tracking = Native.Setting(0x1000), Raise = Native.Setting(0x100C), Delay = Native.Setting(0x2002) } };
            File.WriteAllText(args[1], JsonSerializer.Serialize(data, Storage.Json)); return;
        }
        if (args.Length >= 2 && args[0] == "--self-test") { SelfTests.Run(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--coverage-test") { Application.Run(new TaskbarCoverageIntegrationTests(args[1], args.Length >= 3 ? args[2] : null)); return; }
        if (args.Length >= 3 && args[0] == "--privacy-test") { Application.Run(new BattleNetPrivacyTools(args[1], args[2], false)); return; }
        if (args.Length >= 3 && args[0] == "--clear-bnet-cache") { Application.Run(new BattleNetPrivacyTools(args[1], args[2], true)); return; }
        if (args.Length >= 3 && args[0] == "--scan-wow") { File.WriteAllText(args[2], JsonSerializer.Serialize(WoWInstallationScanner.Scan(new[] { args[1] }), Storage.Json)); return; }
        if (args.Length >= 2 && args[0] == "--library-ui-check") { Application.Run(new LibraryUiTests(args[1])); return; }
        if (args.Length >= 3 && args[0] == "--bnet-test") { Application.Run(new BattleNetIntegrationTests(args[1], args[2])); return; }
        if (args.Length >= 2 && args[0] == "--bnet-check")
        {
            // Read-only check against the real launcher: reads the version and account lists, never presses Play.
            var lines = new List<string>(); var foreground = Native.GetForegroundWindow();
            try
            {
                var choices = BattleNetUi.Run(ui => ui.ReadChoices(), CancellationToken.None, lines.Add).GetAwaiter().GetResult();
                Thread.Sleep(5500); // Battle.net is put away once HoverDeck has left it idle
                var launcher = Native.Windows(Process.GetProcessesByName("Battle.net").Select(p => Native.ProcessPath((uint)p.Id))).FirstOrDefault(w => w.Title == "Battle.net");
                lines.Add($"PASS: read {choices.Versions.Count} versions and {choices.Accounts.Count} accounts");
                lines.Add((launcher is not null && Native.IsIconic(launcher.Handle) ? "PASS" : "FAIL") + ": Battle.net is minimized");
                lines.Add((Native.GetForegroundWindow() != launcher?.Handle ? "PASS" : "FAIL") + ": Battle.net did not take focus" + (Native.GetForegroundWindow() == foreground ? " (focus unchanged)" : ""));
            }
            catch (Exception ex) { lines.Add("FAIL: " + ex.Message); }
            if (args.Contains("--timing"))
            {
                // Times the account/version selection for each Battle.net client of the active layout. Never presses Play.
                int searches = 0; BattleNetUi.Diagnostic = m => { if (m.StartsWith("Accessibility level 0:")) searches++; };
                var settings = Storage.Load(); var layout = settings.Layouts.FirstOrDefault(l => l.Name == settings.ActiveLayout);
                foreach (var slot in layout?.Slots.Where(s => s.BattleNet is { IsConfigured: true }) ?? Enumerable.Empty<Slot>())
                {
                    var watch = Stopwatch.StartNew(); searches = 0;
                    try { var account = BattleNetUi.Run(ui => ui.Prepare(slot.BattleNet!), CancellationToken.None).GetAwaiter().GetResult(); lines.Add($"{slot.Name}: selected {account} in {watch.ElapsedMilliseconds} ms with {searches} searches"); }
                    catch (Exception ex) { lines.Add($"{slot.Name}: FAIL after {watch.ElapsedMilliseconds} ms: {ex.Message}"); }
                }
                BattleNetUi.PutAwayNow();
            }
            File.WriteAllLines(args[1], lines); return;
        }
        if (args.Contains("--launch-settings-preview"))
        {
            BattleNetUi.Diagnostic = message => File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "ui-diagnostic.txt"), DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
            Application.Run(new ClientLaunchForm(new Slot { Name = "Launch settings preview", LaunchMethod = ClientLaunchMethod.BattleNet }, new Layout())); return;
        }
        if (args.Length >= 2 && args[0] == "--login-test") { Application.Run(new LoginIntegrationTests(args[1])); return; }
        if (args.Length >= 2 && args[0] == "--login-ui-check")
        {
            using var form = new ClientLoginForm(new Slot { Name = "Client 1" }, new Layout(), null);
            form.Show(); form.Update();
            using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, form.ClientRectangle);
            bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png); form.Close(); return;
        }
        if (args.Length >= 2 && args[0] == "--display-check")
        {
            try
            {
                var before = Native.Monitors(); var backend = new WindowsDisplaySettings(); var backup = backend.Capture();
                backend.Test(DisplayArrangement.Normalize(before));
                if (before.Count > 1)
                {
                    string alternate = before.First(m => !m.Primary).Name;
                    backend.Test(DisplayArrangement.Normalize(before.Select(m => m with { Primary = m.Name == alternate }).ToList()));
                }
                if (!DisplayArrangement.SamePositions(before, Native.Monitors())) throw new InvalidOperationException("The desktop changed during the read-only check.");
                File.WriteAllText(args[1], "PASS: Native display settings read successfully.\nPASS: Current and alternative-primary geometry pass validation.\nPASS: CDS_TEST accepts the existing graphics modes.\nPASS: Real monitor positions and primary remained unchanged.\n" + JsonSerializer.Serialize(backup, Storage.Json));
            }
            catch (Exception ex) { File.WriteAllText(args[1], ex.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length >= 3 && args[0] == "--integration-test") { Application.Run(new IntegrationTests(args[1], args[2])); return; }
        using var mutex = new Mutex(true, "Local\\HoverDeck.Application", out bool first);
        if (!first) { ThemedMessageBox.Show("HoverDeck is already running. Open it from the notification area.", "HoverDeck"); return; }
        try { if (!args.Contains("--preview")) DisplayChangeSession.RecoverOnStartup(); Application.Run(new MainForm(args.Contains("--preview"))); BattleNetUi.PutAwayNow(); }
        catch (Exception ex)
        {
            try { Directory.CreateDirectory(Storage.DirectoryPath); File.AppendAllText(Path.Combine(Storage.DirectoryPath, "error.log"), DateTimeOffset.Now + " " + ex + Environment.NewLine); } catch { }
            ThemedMessageBox.Show(ex.Message, "HoverDeck could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
