using Microsoft.Win32;

namespace HoverDeck;

public sealed class BattleNetLaunch
{
    public string ConnectionId { get; set; } = "";
    public string ConnectionName { get; set; } = "";
    public string WebAccountId { get; set; } = "";
    public string Product { get; set; } = "";
    public string GameTabId { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public int AccountNumber { get; set; } = 1;
    public string AccountName { get; set; } = "";
    public string Region { get; set; } = "Auto";
    public string ResolvedAccount { get; set; } = "";
    public string Executable { get; set; } = "";
    public string BoundIdentity { get; set; } = "";
    public string PendingSinceUtc { get; set; } = "";
    public List<string> BeforeLaunch { get; set; } = new();
    public bool IsConfigured => GameTabId.StartsWith("game-nav-btn-WoW", StringComparison.Ordinal)
        && GameVersion.Length > 0 && (AccountName.Length > 0 || AccountNumber is >= 1 and <= 9) && Executable.Length > 0
        && (ConnectionId.Length == 0 || (WebAccountId.Length > 0 && AccountName.Length > 0));
    public string AccountKey => ConnectionId.Length > 0 ? ConnectionId + "/" + WebAccountId : AccountName.Length > 0 ? AccountName.ToUpperInvariant() : $"WOW{AccountNumber}/{Region.ToUpperInvariant()}";
    public string AccountLabel => AccountName.Length > 0 ? AccountName : AccountNumber is >= 1 and <= 9 ? $"WoW{AccountNumber}" + (Region == "Auto" ? " (automatic region)" : $" ({Region})") : "Choose an account";
    public bool SameTarget(BattleNetLaunch? other) => other is not null && GameTabId == other.GameTabId
        && GameVersion == other.GameVersion && AccountKey == other.AccountKey && Matching.SamePath(Executable, other.Executable);
    public void ClearBinding() { BoundIdentity = ""; PendingSinceUtc = ""; BeforeLaunch.Clear(); ResolvedAccount = ""; }
    public void ClearAccountSelection() { WebAccountId = ""; AccountName = ""; AccountNumber = 0; Region = "Auto"; ClearBinding(); }
}

internal static class BattleNetAccounts
{
    public static string Resolve(BattleNetLaunch profile, IEnumerable<string> choices)
    {
        if (profile.AccountName.Length > 0)
        {
            var exact = choices.Where(c => string.Equals(c, profile.AccountName, StringComparison.OrdinalIgnoreCase)
                || (profile.WebAccountId.Length > 0 && c.StartsWith(profile.AccountName + " - ", StringComparison.OrdinalIgnoreCase))).Distinct().ToList();
            if (exact.Count == 1) return exact[0];
            throw new InvalidOperationException(exact.Count == 0
                ? $"{profile.AccountName} was not found in the open Battle.net launcher for {profile.GameVersion}. Sign into the intended Battle.net login or choose another WoW account, then retry."
                : $"Battle.net lists more than one match for {profile.AccountName}. Choose an unambiguous WoW account before starting.");
        }
        var matches = choices.Where(choice =>
        {
            var match = System.Text.RegularExpressions.Regex.Match(choice, @"^WoW\s*#?\s*(\d+)\s*\(([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success && int.Parse(match.Groups[1].Value) == profile.AccountNumber
                && (profile.Region == "Auto" || string.Equals(match.Groups[2].Value, profile.Region, StringComparison.OrdinalIgnoreCase));
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (matches.Count == 0) throw new InvalidOperationException($"{profile.AccountLabel} is not available for {profile.GameVersion} in the signed-in Battle.net account.");
        if (matches.Count > 1) throw new InvalidOperationException($"WoW{profile.AccountNumber} exists in several regions. Choose its region in Edit client before starting.");
        return matches[0];
    }
}

internal sealed record BattleNetChoices(string GameTabId, string GameVersion, string SelectedAccount, List<string> Versions, List<string> Accounts);

internal static class BattleNetInstallation
{
    public static List<string> GameExecutables()
    {
        var result = new List<string>();
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var name in uninstall.GetSubKeyNames())
                try
                {
                    using var key = uninstall.OpenSubKey(name);
                    if (key is null || (key.GetValue("DisplayName") as string)?.StartsWith("World of Warcraft", StringComparison.OrdinalIgnoreCase) != true) continue;
                    var icon = (key.GetValue("DisplayIcon") as string ?? "").Trim('"').Split(",", 2)[0];
                    if (File.Exists(icon) && Path.GetExtension(icon).Equals(".exe", StringComparison.OrdinalIgnoreCase)) result.Add(icon);
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static string SuggestExecutable(string version)
    {
        var paths = GameExecutables();
        string folder = version switch { "World of Warcraft Classic" => "_classic_era_", "Mists of Pandaria Classic" => "_classic_", "Burning Crusade Anniversary" => "_anniversary_", "WoW: Forever" => "_forever_", "World of Warcraft" => "_retail_", _ => "" };
        return folder.Length == 0 ? "" : paths.FirstOrDefault(p => p.Contains(folder, StringComparison.OrdinalIgnoreCase)) ?? "";
    }
}

