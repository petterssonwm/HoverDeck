namespace HoverDeck;

internal interface IBattleNetGateway
{
    Task<string> Prepare(BattleNetLaunch profile, Action<string> log, CancellationToken token);
    Task Play(BattleNetLaunch profile, string account, CancellationToken token);
}
internal sealed class BattleNetGateway : IBattleNetGateway
{
    public Task<string> Prepare(BattleNetLaunch profile, Action<string> log, CancellationToken token) => BattleNetUi.Run(ui => ui.Prepare(profile), token, log);
    public async Task Play(BattleNetLaunch profile, string account, CancellationToken token) => await BattleNetUi.Run(ui => ui.Play(profile, account), token);
}
