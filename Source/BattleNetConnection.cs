using System.Text.Json;

namespace HoverDeck;

public sealed class BattleNetConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Battle.net";
    public List<WoWLicense> Accounts { get; set; } = new();
    public List<WoWLicense> RemovedAccounts { get; set; } = new();
    public string RefreshedUtc { get; set; } = "";
    public int SkippedAccounts { get; set; }
    public override string ToString() => Name;
}

internal static class BattleNetConnectionSettings
{
    public static void ValidateSelection(Settings settings, BattleNetLaunch profile)
    {
        if (profile.ConnectionId.Length == 0) return;
        var connection = settings.BattleNetConnections.SingleOrDefault(c => c.Id == profile.ConnectionId)
            ?? throw new InvalidOperationException("This client's Battle.net connection is missing. Select a connection in Edit client.");
        if (!connection.Accounts.Any(a => a.Id == profile.WebAccountId && a.Label == profile.AccountName))
            throw new InvalidOperationException("This client's license changed or is no longer in its connection. Refresh and select its account again.");
    }

    public static int UsageCount(Settings settings, string connectionId, string? accountId = null) =>
        Profiles(settings, connectionId).Count(p => accountId is null || p.WebAccountId == accountId);

    public static void RemoveAccount(Settings settings, BattleNetConnection connection, WoWLicense account)
    {
        if (connection.Accounts.RemoveAll(a => a.Id == account.Id) == 0) return;
        if (!connection.RemovedAccounts.Any(a => a.Id == account.Id)) connection.RemovedAccounts.Add(account);
        foreach (var profile in Profiles(settings, connection.Id).Where(p => p.WebAccountId == account.Id)) profile.ClearAccountSelection();
    }

    public static void RestoreAccount(BattleNetConnection connection, WoWLicense account)
    {
        if (connection.RemovedAccounts.RemoveAll(a => a.Id == account.Id) == 0) return;
        if (!connection.Accounts.Any(a => a.Id == account.Id)) connection.Accounts.Add(account);
        connection.Accounts = connection.Accounts.OrderBy(a => a.Name).ThenBy(a => a.Region).ToList();
    }

    public static void RemoveConnection(Settings settings, BattleNetConnection connection)
    {
        settings.BattleNetConnections.RemoveAll(c => c.Id == connection.Id);
        foreach (var profile in Profiles(settings, connection.Id))
        {
            profile.ClearAccountSelection(); profile.ConnectionId = ""; profile.ConnectionName = "";
        }
    }

    public static void ApplyImport(BattleNetConnection connection, BattleNetAccountData.ImportResult imported)
    {
        var removed = connection.RemovedAccounts.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        // Keep removal choices across refreshes, including when a license temporarily disappears.
        connection.RemovedAccounts = connection.RemovedAccounts.Select(a => imported.Accounts.FirstOrDefault(fresh => fresh.Id == a.Id) ?? a).ToList();
        connection.Accounts = imported.Accounts.Where(a => !removed.Contains(a.Id)).ToList();
        connection.SkippedAccounts = imported.SkippedAccounts;
        connection.RefreshedUtc = DateTimeOffset.UtcNow.ToString("O");
    }

    private static IEnumerable<BattleNetLaunch> Profiles(Settings settings, string connectionId) =>
        settings.Layouts.SelectMany(l => l.Slots).Select(s => s.BattleNet).OfType<BattleNetLaunch>().Where(p => p.ConnectionId == connectionId);
}

public sealed class WoWLicense
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public string Label => $"{Name} ({Region})";
    public override string ToString() => Label;
}

internal static class BattleNetAccountData
{
    internal sealed record ImportResult(List<WoWLicense> Accounts, int SkippedAccounts);
    public static string SessionFolder(string connectionId)
    {
        if (!Guid.TryParseExact(connectionId, "N", out var id)) throw new InvalidOperationException("Invalid Battle.net connection. Add it again.");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoverDeck", "WebProfiles", id.ToString("N"));
    }
    public static List<WoWLicense> Parse(string json) => ParseDetailed(json).Accounts;
    public static ImportResult ParseDetailed(string json)
    {
        if (json.Length > 2_000_000) throw new InvalidOperationException("Battle.net returned an unexpectedly large account response.");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("gameAccounts", out var accounts) || accounts.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Battle.net did not return its account list. Sign in again, or retry later if its service is unavailable.");
        var result = new List<WoWLicense>(); int skipped = 0;
        foreach (var account in accounts.EnumerateArray())
        {
            if (account.ValueKind != JsonValueKind.Object) continue;
            account.TryGetProperty("gameAccountUniqueId", out var identity);
            if (Text(account, "titleId") != "5730135" && Text(identity, "programId") != "5730135") continue;
            string name = Text(account, "gameAccountName"), region = NormalizeRegion(Text(account, "gameAccountRegion"));
            string id = Text(identity, "gameAccountId"), regionId = Text(identity, "gameServiceRegionId");
            if (name.Length is 0 or > 128 || name.Any(char.IsControl) || region.Length == 0
                || !ulong.TryParse(id, out ulong accountId) || accountId == 0 || !uint.TryParse(regionId, out uint serviceRegion) || serviceRegion == 0)
            { skipped++; continue; }
            result.Add(new() { Id = "5730135:" + regionId + ":" + id, Name = name, Region = region });
        }
        if (result.Count == 0 && skipped > 0) throw new InvalidOperationException("Blizzard returned WoW licenses, but none had a usable account name, region and ID. Your existing account list was kept.");
        return new(result.DistinctBy(a => a.Id).OrderBy(a => a.Name).ThenBy(a => a.Region).ToList(), skipped);
    }
    private static string Text(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item) && item.ValueKind is JsonValueKind.String or JsonValueKind.Number ? item.ToString().Trim() : "";
    internal static string NormalizeRegion(string value) => value.ToUpperInvariant() switch
    {
        "EU" or "EUROPE" => "EU", "US" or "AMERICAS" or "NORTH AMERICA" or "AMERICAS & OCEANIA" => "US",
        "KR" or "KOREA" => "KR", "TW" or "TAIWAN" => "TW", "CN" or "CHINA" => "CN",
        "PTR" or "PUBLIC TEST REALM" => "PTR", "BETA" => "BETA",
        _ => value.Length <= 48 && value.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_') ? value.ToUpperInvariant() : ""
    };
}
