using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace HoverDeck;

// Updates for installed copies, published as GitHub releases by release.ps1. HoverDeck
// asks before updating and never updates while clients are being launched or placed.
public sealed partial class MainForm
{
    private bool updateCheckRunning;
    private static string? UpdateRepository => typeof(MainForm).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "UpdateRepository")?.Value is { Length: > 0 } value ? value : null;
    private static IUpdateSource UpdateSource(string repository) =>
        Directory.Exists(repository) ? new SimpleFileSource(new DirectoryInfo(repository)) : new GithubSource(repository, null, false);
    private async Task CheckForUpdates(bool userAsked)
    {
        if (preview || updateCheckRunning || closing) return;
        string version = Application.ProductVersion.Split('+')[0];
        if (UpdateRepository is not { } repository) { if (userAsked) MessageBox.Show(this, $"HoverDeck {version}\n\nThis copy was built without an update address, so it can't check for updates.", "Check for updates"); return; }
        var manager = new UpdateManager(UpdateSource(repository));
        if (!manager.IsInstalled || manager.IsPortable)
        {
            if (userAsked) MessageBox.Show(this, $"HoverDeck {version}\n\nThis copy wasn't installed with the HoverDeck installer, so it can't update itself. Download the latest installer from:\n{repository}/releases", "Check for updates");
            return;
        }
        updateCheckRunning = true;
        try
        {
            var update = await manager.CheckForUpdatesAsync();
            if (update is null) { if (userAsked) MessageBox.Show(this, $"You have the latest version of HoverDeck ({manager.CurrentVersion}).", "Check for updates"); return; }
            var next = update.TargetFullRelease.Version;
            Log($"HoverDeck {next} is available (installed: {manager.CurrentVersion}).");
            if (MessageBox.Show(this, $"HoverDeck {next} is available. You have {manager.CurrentVersion}.\n\nUpdate now? HoverDeck closes, updates and opens again. Your layouts and settings are kept.", "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            if (busy || displayApplying || arrangeDisplays.Checked) { MessageBox.Show(this, "Finish what HoverDeck is doing first (launching, placing clients or arranging displays), then choose Settings → Check for updates.", "Update available"); return; }
            busy = true; mainMenu.Enabled = false; SetWorkspaceEnabled(false); start.Visible = apply.Visible = false; SetStatus("Downloading update…");
            try { await manager.DownloadUpdatesAsync(update, percent => BeginInvoke(() => SetStatus($"Downloading update… {percent}%"))); }
            finally { busy = false; mainMenu.Enabled = true; SetWorkspaceEnabled(true); start.Visible = true; SetStatus(""); UpdateApplyButton(); }
            Log($"Installing HoverDeck {next}; HoverDeck will reopen.");
            // The updater waits for HoverDeck to exit normally, so Windows hover settings are restored first.
            manager.WaitExitThenApplyUpdates(update.TargetFullRelease, false, true, Array.Empty<string>());
            Close();
        }
        catch (Exception ex)
        {
            Log("Update check failed: " + ex.Message);
            if (userAsked) MessageBox.Show(this, "HoverDeck couldn't check for updates right now. Check your internet connection and try again.\n\n" + ex.Message, "Check for updates");
        }
        finally { updateCheckRunning = false; }
    }
}
