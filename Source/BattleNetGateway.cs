namespace HoverDeck;

internal interface IBattleNetGateway
{
    Task<string> Prepare(BattleNetLaunch profile, Action<string> log, CancellationToken token);
    Task Play(BattleNetLaunch profile, string account, CancellationToken token);
}
internal sealed class BattleNetGateway : IBattleNetGateway
{
    public Task<string> Prepare(BattleNetLaunch profile, Action<string> log, CancellationToken token) => BattleNetUi.Run(ui => ui.Prepare(profile), token, log);
    public async Task Play(BattleNetLaunch profile, string account, CancellationToken token)
    {
        bool sent = false;
        try { await BattleNetUi.Run(ui => { ui.Play(profile, account, () => sent = true); return true; }, token); }
        catch (Exception ex) when (!sent) { throw new PlayNotSentException(ex); }
    }
}
// Play was certainly not pressed, so the launch must not stay pending: a retry may press it.
internal sealed class PlayNotSentException(Exception inner) : Exception(inner.Message, inner);
