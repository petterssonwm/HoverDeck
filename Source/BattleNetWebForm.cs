using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace HoverDeck;

internal sealed class BattleNetWebForm : Form
{
    internal const string AccountsUrl = "https://account.battle.net/api/games-and-subs";
    private const string SignInUrl = "https://account.battle.net/overview";
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Label status = new() { Dock = DockStyle.Fill, Padding = new(10, 4, 10, 4) };
    private readonly Label notice = new() { Name = "ImportNotice", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new("Segoe UI", 12), Text = "Opening Blizzard sign-in…" };
    private readonly BattleNetConnection connection;
    private readonly bool interactive;
    private readonly TaskCompletionSource<BattleNetAccountData.ImportResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private BattleNetAccountData.ImportResult? result;
    private Exception? failure;
    private bool initialized, reading, expectingImport, closing, closeApproved;
    private readonly System.Windows.Forms.Timer timeout = new() { Interval = 45000 };
    protected override bool ShowWithoutActivation => !interactive;
    internal bool ResponseViewVisible => browser.Visible;
    internal Task<BattleNetAccountData.ImportResult> Completion => completion.Task;
    internal BattleNetWebForm(BattleNetConnection connection, bool interactive, string? testProfileDirectory = null, Action<CoreWebView2Environment, CoreWebView2>? testSetup = null)
    {
        this.connection = connection; this.interactive = interactive;
        Text = "Connect Battle.net — " + connection.Name; Size = new(950, 780); StartPosition = FormStartPosition.CenterParent;
        Font = new("Segoe UI", 10); ShowInTaskbar = interactive; if (!interactive) Opacity = 0;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new(SizeType.Absolute, 64)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 54)); Controls.Add(root);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(10) };
        var signIn = new Button { Text = "Back to Blizzard sign-in", AutoSize = true, Height = 36, Enabled = false };
        var import = new Button { Name = "ImportAccounts", Text = "Finish connection — import WoW accounts", AutoSize = true, Height = 36, Enabled = false }; actions.Controls.AddRange(new Control[] { signIn, import });
        var host = new Panel { Dock = DockStyle.Fill }; host.Controls.Add(browser); host.Controls.Add(notice);
        root.Controls.Add(actions, 0, 0); root.Controls.Add(host, 0, 1); root.Controls.Add(status, 0, 2);
        status.Text = "Sign in on Blizzard's website, then choose Finish connection above.";
        void HideResponse(string message) { browser.Visible = false; notice.Text = message; notice.Visible = true; notice.BringToFront(); }
        void BeginImport()
        {
            if (!initialized || reading || closing) return;
            expectingImport = true; HideResponse("Importing your WoW account names and regions…");
            status.Text = "Reading licenses. The raw account response is hidden."; timeout.Start(); browser.CoreWebView2.Navigate(AccountsUrl);
        }
        signIn.Click += (_, _) => { if (initialized && !reading && !closing) { expectingImport = false; timeout.Stop(); HideResponse("Opening Blizzard sign-in…"); browser.CoreWebView2.Navigate(SignInUrl); } };
        import.Click += (_, _) => BeginImport();
        timeout.Tick += (_, _) => Fail("The account request timed out. Return to Blizzard sign-in and try again.");
        Shown += async (_, _) =>
        {
            if (!interactive) timeout.Start();
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, testProfileDirectory ?? BattleNetAccountData.SessionFolder(connection.Id));
                if (IsDisposed) return; await browser.EnsureCoreWebView2Async(environment); if (IsDisposed) return;
                var core = browser.CoreWebView2;
                core.Settings.AreHostObjectsAllowed = false; core.Settings.IsWebMessageEnabled = false;
                core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsGeneralAutofillEnabled = false;
                testSetup?.Invoke(environment, core);
                core.DownloadStarting += (_, e) => e.Cancel = true;
                core.NavigationStarting += (_, e) =>
                {
                    if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https") { e.Cancel = true; return; }
                    // Hide before any API document can paint, including redirects and manual retries.
                    if (uri.AbsolutePath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) HideResponse("Reading WoW licenses…");
                    else status.Text = "Blizzard connection · " + uri.Host;
                };
                core.NewWindowRequested += (_, e) => { e.Handled = true; if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https") core.Navigate(uri.AbsoluteUri); };
                core.NavigationCompleted += async (_, e) =>
                {
                    if (reading || closing || IsDisposed) return;
                    if (!IsAccountsPage(core.Source))
                    {
                        if (!interactive) { Fail("This connection needs you to sign in again. Use Sign in / import in Battle.net connections."); return; }
                        if (Uri.TryCreate(core.Source, UriKind.Absolute, out var current) && !current.AbsolutePath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                        {
                            notice.Visible = false; browser.Visible = true; browser.BringToFront();
                            if (expectingImport) { expectingImport = false; timeout.Stop(); status.Text = "Complete Blizzard sign-in, then choose Finish connection."; }
                        }
                        return;
                    }
                    HideResponse("Importing WoW licenses…");
                    if (!e.IsSuccess || e.HttpStatusCode != 200) { Fail("Blizzard did not return an account list. Return to sign-in, or retry later if its service is unavailable."); return; }
                    reading = true; import.Enabled = signIn.Enabled = false;
                    try
                    {
                        // The browser projects only these five license fields. Subscription/payment/address
                        // objects never cross into application code or enter the visible UI.
                        string projected = await core.ExecuteScriptAsync(LicenseProjectionScript);
                        if (IsDisposed || !IsAccountsPage(core.Source)) return;
                        result = BattleNetAccountData.ParseDetailed(projected); timeout.Stop(); Close();
                    }
                    catch (Exception ex) { Fail(ex is InvalidOperationException ? ex.Message : "The account list could not be read. Return to sign-in and try again."); }
                    finally { reading = false; if (!IsDisposed && !closing) import.Enabled = signIn.Enabled = true; }
                };
                // Removes cached API pages left by older releases without reading them or clearing sign-in cookies.
                await ClearCachedPages(core);
                if (IsDisposed || closing) return; initialized = true; import.Enabled = signIn.Enabled = true;
                if (interactive) core.Navigate(SignInUrl); else BeginImport();
            }
            catch (WebView2RuntimeNotFoundException) { Fail("Microsoft Edge WebView2 Runtime is required. Install it, then reconnect."); }
            catch (Exception) { Fail("The connection browser could not start or clear its old cache. Close other connection windows and try again."); }
        };
        FormClosing += async (_, e) =>
        {
            if (closeApproved || browser.CoreWebView2 is null) return;
            e.Cancel = true; if (closing) return; closing = true; timeout.Stop(); import.Enabled = signIn.Enabled = false;
            HideResponse("Clearing cached account pages…");
            try { await ClearCachedPages(browser.CoreWebView2); }
            catch { failure = new InvalidOperationException("The connection closed, but its browser cache cleanup failed. Reopen Sign in / import to retry cleanup."); result = null; }
            finally { closeApproved = true; if (!IsDisposed) Close(); }
        };
        FormClosed += (_, _) =>
        {
            timeout.Dispose();
            if (failure is not null) completion.TrySetException(failure);
            else if (result is not null) completion.TrySetResult(result);
            else completion.TrySetCanceled();
        };
        AppTheme.Apply(this); AppTheme.Primary(import);
    }
    internal const string LicenseProjectionScript = """
        (() => {
          if (location.origin !== 'https://account.battle.net' || location.pathname !== '/api/games-and-subs' || location.search !== '') return null;
          try {
            const body = JSON.parse(document.body.innerText);
            if (!Array.isArray(body.gameAccounts)) return null;
            const text = value => typeof value === 'string' || typeof value === 'number' ? String(value) : '';
            return {gameAccounts: body.gameAccounts.filter(account => account && (text(account.titleId) === '5730135' || text(account.gameAccountUniqueId?.programId) === '5730135')).map(account => ({
              titleId: '5730135',
              gameAccountName: text(account.gameAccountName),
              gameAccountRegion: text(account.gameAccountRegion),
              gameAccountUniqueId: {
                programId: '5730135',
                gameAccountId: text(account.gameAccountUniqueId?.gameAccountId),
                gameServiceRegionId: text(account.gameAccountUniqueId?.gameServiceRegionId)
              }
            }))};
          } catch { return null; }
        })()
        """;
    internal static Task ClearCachedPages(CoreWebView2 core) => core.Profile.ClearBrowsingDataAsync(
        CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage | CoreWebView2BrowsingDataKinds.BrowsingHistory);
    internal static Task ClearSavedSignIn(CoreWebView2 core) => core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
    internal static async Task ForgetSession(Control owner, BattleNetConnection connection)
    {
        string directory = BattleNetAccountData.SessionFolder(connection.Id);
        if (!Directory.Exists(directory)) return;
        using var web = new WebView2 { Visible = false };
        owner.Controls.Add(web);
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, directory);
            await web.EnsureCoreWebView2Async(environment);
            await ClearSavedSignIn(web.CoreWebView2);
        }
        catch { throw new InvalidOperationException("The saved web sign-in could not be cleared. The connection was kept; close its sign-in window and try removing it again."); }
        finally { owner.Controls.Remove(web); }
    }
    internal static bool IsAccountsPage(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "account.battle.net" && uri.IsDefaultPort && uri.AbsolutePath == "/api/games-and-subs" && uri.Query.Length == 0;
    private void Fail(string message)
    {
        if (IsDisposed || closing) return;
        timeout.Stop(); status.Text = message; browser.Visible = false; notice.Visible = true; notice.Text = message; notice.BringToFront();
        if (!interactive) { failure = new InvalidOperationException(message); Close(); }
    }
    public static async Task<BattleNetAccountData.ImportResult?> Fetch(IWin32Window owner, BattleNetConnection connection, bool signIn)
    {
        using var form = new BattleNetWebForm(connection, signIn);
        if (signIn) form.ShowDialog(owner); else form.Show(owner);
        try { return await form.completion.Task; } catch (TaskCanceledException) { return null; }
    }
}
