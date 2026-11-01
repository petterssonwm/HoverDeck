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
                        CoreWebView2? testCore = null; bool hiddenDuringNavigation = false, overviewShown = false;
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
                            }, "https://account.battle.net/login/en/");
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(this);
                        // The window opens on a sign-in page: the website is shown and nothing is imported yet.
                        for (int i = 0; i < 200 && !form.ResponseViewVisible; i++) await Task.Delay(50);
                        if (!form.ResponseViewVisible || testCore is null) throw new Exception("Synthetic sign-in view did not become ready.");
                        await Task.Delay(500); if (form.Completion.IsCompleted) throw new Exception("Accounts were imported before signing in.");
                        // Signing in lands on the account overview, which is never shown: the import starts by itself.
                        testCore.NavigationStarting += (_, e) =>
                        {
                            if (BattleNetWebForm.IsAccountsPage(e.Uri)) hiddenDuringNavigation = !form.ResponseViewVisible;
                            if (BattleNetWebForm.IsSignedInPage(e.Uri) && form.ResponseViewVisible) overviewShown = true;
                        };
                        testCore.NavigationCompleted += (_, _) => { if (BattleNetWebForm.IsSignedInPage(testCore.Source) && form.ResponseViewVisible) overviewShown = true; };
                        testCore.Navigate("https://account.battle.net/overview");
                        if (success)
                        {
                            var imported = await form.Completion.WaitAsync(TimeSpan.FromSeconds(20));
                            if (imported.Accounts.Count != 2 || !hiddenDuringNavigation || overviewShown) throw new Exception("Interactive import failed to finish privately and by itself.");
                            lines.Add("PASS: after signing in, the import starts by itself without showing the account page, keeps the response hidden, imports EU/PTR and closes after cleanup.");
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
