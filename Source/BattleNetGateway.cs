namespace HoverDeck;

internal interface IBattleNetGateway
{
    Task<string> Prepare(BattleNetLaunch profile, CancellationToken token);
    Task Play(BattleNetLaunch profile, string account, CancellationToken token);
}
internal sealed class BattleNetGateway : IBattleNetGateway
{
    public Task<string> Prepare(BattleNetLaunch profile, CancellationToken token) => BattleNetUi.Run(ui => ui.Prepare(profile), token);
    public async Task Play(BattleNetLaunch profile, string account, CancellationToken token) => await BattleNetUi.Run(ui => ui.Play(profile, account), token);
}
