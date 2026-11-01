using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text;
using System.Text.Json;

namespace HoverDeck;

internal sealed class BattleNetPrivacyTools : Form
{
    // Maintenance clears only this application's browser cache/history. It never reads stored pages or cookies.
    public BattleNetPrivacyTools(string input, string report, bool cleanup)
    {
        ShowInTaskbar = false; Opacity = 0;
        Shown += async (_, _) =>
        {
            var lines = new List<string>();
            try
            {
                if (cleanup)
                {
                    using var metadata = JsonDocument.Parse(File.ReadAllText(input));
                    var ids = metadata.RootElement.GetProperty("Profiles").EnumerateArray().Select(p => p.GetProperty("Name").GetString()!).Distinct().ToList();
                    foreach (string id in ids)
                    {
                        using var web = new WebView2 { Dock = DockStyle.Fill, Visible = false }; Controls.Add(web);
                        var environment = await CoreWebView2Environment.CreateAsync(null, BattleNetAccountData.SessionFolder(id));
                        await web.EnsureCoreWebView2Async(environment);
                        await BattleNetWebForm.ClearCachedPages(web.CoreWebView2);
                        Controls.Remove(web);
                    }
                    lines.Add($"PASS: cleared disk cache, CacheStorage and browsing history for {ids.Count} HoverDeck browser profiles.");
                    lines.Add("Sign-in cookies were preserved. No account pages or stored cookie contents were read.");
                }
                else
                {
                    Directory.CreateDirectory(input);
                    using var web = new WebView2 { Dock = DockStyle.Fill, Visible = false }; Controls.Add(web);
                    var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(input, "synthetic-browser"));
                    await web.EnsureCoreWebView2Async(environment); var core = web.CoreWebView2;
                    const string response = """
                        {"gameAccounts":[
                          {"titleId":5730135,"gameAccountName":"WoW1","gameAccountRegion":"EU","gameAccountUniqueId":{"gameAccountId":100,"gameServiceRegionId":2,"programId":5730135},"accountSubscriptionView":{"paymentMethod":{"name":"DO_NOT_EXPORT","address":"DO_NOT_EXPORT"}}},
                          {"titleId":5730135,"gameAccountName":"WoW8","gameAccountRegion":"PTR","gameAccountUniqueId":{"gameAccountId":800,"gameServiceRegionId":98,"programId":5730135}},
                          {"titleId":999,"gameAccountName":"Other game","gameAccountUniqueId":{"gameAccountId":900,"programId":999}}
                        ]}
                        """;
                    core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    core.WebResourceRequested += (_, e) =>
                    {
                        bool endpoint = BattleNetWebForm.IsAccountsPage(e.Request.Uri);
                        e.Response = environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(endpoint ? response : "")), endpoint ? 200 : 404, endpoint ? "OK" : "Not found", "Content-Type: application/json\r\nCache-Control: no-store");
                    };
                    var loaded = new TaskCompletionSource<bool>(); core.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
                    core.Navigate(BattleNetWebForm.AccountsUrl);
                    if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20))) throw new Exception("Synthetic response did not load.");
                    string projected = await core.ExecuteScriptAsync(BattleNetWebForm.LicenseProjectionScript);
                    if (projected.Contains("DO_NOT_EXPORT") || projected.Contains("paymentMethod") || projected.Contains("accountSubscriptionView")) throw new Exception("Projection included an unrelated field.");
                    var accounts = BattleNetAccountData.ParseDetailed(projected);
                    if (accounts.Accounts.Count != 2 || !accounts.Accounts.Any(a => a.Region == "PTR")) throw new Exception("Synthetic EU/PTR licenses did not import.");
                    if (web.Visible) throw new Exception("Synthetic raw response was visible.");
                    await BattleNetWebForm.ClearCachedPages(core);
                    lines.Add("PASS: browser-side projection returned only WoW license fields and excluded synthetic subscription/payment data.");
                    lines.Add("PASS: EU and PTR accounts imported together; the response view stayed hidden.");
                    lines.Add("PASS: WebView2 cache/history cleanup completed.");
                    async Task CheckImportWindow(string payload, bool success)
                    {
                        CoreWebView2? testCore = null; bool hiddenDuringNavigation = false;
                        using var form = new BattleNetWebForm(new() { Name = "Synthetic login" }, true,
                            Path.Combine(input, success ? "import-success" : "import-failure"), (env, browser) =>
                            {
                                testCore = browser; browser.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                                browser.WebResourceRequested += (_, e) =>
                                {
                                    bool api = BattleNetWebForm.IsAccountsPage(e.Request.Uri);
                                    string body = api ? payload : "<!doctype html><html><body>Local synthetic sign-in fixture</body></html>";
                                    e.Response = env.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(body)), 200, "OK", "Content-Type: " + (api ? "application/json" : "text/html") + "\r\nCache-Control: no-store");
                                };
                            });
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(this);
                        var import = (Button)form.Controls.Find("ImportAccounts", true).Single();
                        // The window opens on the account overview, which the fixture serves as a signed-in page.
                        for (int i = 0; i < 200 && !import.Enabled; i++) await Task.Delay(50);
                        if (!import.Enabled || form.ResponseViewVisible || testCore is null) throw new Exception("Signing in did not hide Blizzard's account page and enable Finish connection.");
                        // On a sign-in page the website is shown and Finish connection waits.
                        var signInPage = new TaskCompletionSource<bool>(); void Loaded(object? s, CoreWebView2NavigationCompletedEventArgs e) => signInPage.TrySetResult(true);
                        testCore.NavigationCompleted += Loaded; testCore.Navigate("https://account.battle.net/login/en/");
                        await signInPage.Task.WaitAsync(TimeSpan.FromSeconds(20)); testCore.NavigationCompleted -= Loaded; await Task.Delay(100);
                        if (import.Enabled || !form.ResponseViewVisible) throw new Exception("Finish connection was available before signing in, or the sign-in page was hidden.");
                        var signedInAgain = new TaskCompletionSource<bool>(); void Back(object? s, CoreWebView2NavigationCompletedEventArgs e) => signedInAgain.TrySetResult(true);
                        testCore.NavigationCompleted += Back; testCore.Navigate("https://account.battle.net/overview");
                        await signedInAgain.Task.WaitAsync(TimeSpan.FromSeconds(20)); testCore.NavigationCompleted -= Back; await Task.Delay(100);
                        if (!import.Enabled || form.ResponseViewVisible) throw new Exception("Returning signed in did not enable Finish connection.");
                        if (success)
                        {
                            using (var picture = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(picture, new Rectangle(Point.Empty, form.Size)); picture.Save(Path.Combine(input, "signed-in.png")); }
                            lines.Add("PASS: Finish connection is off on Blizzard's sign-in page, and turns on with the account page hidden once signed in.");
                        }
                        testCore.NavigationStarting += (_, e) => { if (BattleNetWebForm.IsAccountsPage(e.Uri)) hiddenDuringNavigation = !form.ResponseViewVisible; };
                        import.PerformClick();
                        if (form.ResponseViewVisible) throw new Exception("Import did not hide the website before navigating.");
                        if (success)
                        {
                            var imported = await form.Completion.WaitAsync(TimeSpan.FromSeconds(20));
                            if (imported.Accounts.Count != 2 || !hiddenDuringNavigation) throw new Exception("Interactive import failed to finish privately.");
                            lines.Add("PASS: the real import window hides the response before navigation, imports EU/PTR and closes after cleanup.");
                        }
                        else
                        {
                            var notice = (Label)form.Controls.Find("ImportNotice", true).Single();
                            for (int i = 0; i < 200 && !notice.Text.Contains("none had a usable"); i++) await Task.Delay(50);
                            if (!notice.Text.Contains("none had a usable") || form.ResponseViewVisible || !hiddenDuringNavigation) throw new Exception("Failed import exposed the page or did not show its error.");
                            using var picture = new Bitmap(form.Width, form.Height); form.DrawToBitmap(picture, new Rectangle(Point.Empty, form.Size)); picture.Save(Path.Combine(input, "safe-error.png"));
                            form.Close(); try { await form.Completion.WaitAsync(TimeSpan.FromSeconds(20)); } catch (TaskCanceledException) { }
                            lines.Add("PASS: a failed import keeps the raw response hidden and cleans up on close.");
                        }
                    }
                    await CheckImportWindow(response, true);
                    await CheckImportWindow(response.Replace("\"gameAccountName\":\"WoW1\"", "\"gameAccountName\":\"\"").Replace("\"gameAccountName\":\"WoW8\"", "\"gameAccountName\":\"\""), false);
                    var cookie = core.CookieManager.CreateCookie("synthetic-login", "fixture-only", ".fixture.invalid", "/");
                    core.CookieManager.AddOrUpdateCookie(cookie);
                    if ((await core.CookieManager.GetCookiesAsync("https://fixture.invalid")).Count != 1) throw new Exception("Synthetic session fixture was not created.");
                    await core.ExecuteScriptAsync("localStorage.setItem('synthetic-login', 'fixture-only')");
                    await BattleNetWebForm.ClearSavedSignIn(core);
                    if ((await core.CookieManager.GetCookiesAsync("https://fixture.invalid")).Count != 0 || await core.ExecuteScriptAsync("localStorage.getItem('synthetic-login')") != "null") throw new Exception("Removing a connection did not clear its synthetic sign-in data.");
                    lines.Add("PASS: connection removal clears synthetic cookies and local storage from its isolated web profile.");
                    lines.Add("All requests were intercepted with synthetic responses. No Blizzard services or real accounts were accessed.");
                }
            }
            catch (Exception ex) { lines.Add("FAILED: " + ex.GetType().Name + ": " + ex.Message); Environment.ExitCode = 1; }
            finally { File.WriteAllLines(report, lines); Close(); }
        };
    }
}
