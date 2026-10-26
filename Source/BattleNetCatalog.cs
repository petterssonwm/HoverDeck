namespace HoverDeck;

// Discovery belongs to the workspace; an account selection belongs to a slot.
public sealed class BattleNetCatalog
{
    public string LastGameVersion { get; set; } = "";
    public List<string> Versions { get; set; } = new();
    public List<BattleNetGameChoices> Games { get; set; } = new();
    public BattleNetGameChoices? ForVersion(string version) => Games.FirstOrDefault(g => g.GameVersion == version);

    internal void Remember(BattleNetChoices choices)
    {
        Versions = choices.Versions.Append(choices.GameVersion).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
        LastGameVersion = choices.GameVersion;
        Games.RemoveAll(g => g.GameVersion == choices.GameVersion);
        Games.Add(new BattleNetGameChoices
        {
            GameTabId = choices.GameTabId, GameVersion = choices.GameVersion,
            Accounts = choices.Accounts.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList()
        });
    }
}

public sealed class BattleNetGameChoices
{
    public string GameTabId { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public List<string> Accounts { get; set; } = new();
}
