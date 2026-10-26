namespace HoverDeck;

public sealed partial class MainForm
{
    private async Task ConfigureClientLogin()
    {
        if (busy || displayApplying || arrangeDisplays.Checked || CurrentSlot is not { } slot) return;
        var window = launcher.Assign(layout, launcher.Scan(layout)).GetValueOrDefault(slot.Id);
        busy = true; hover?.Suspend(true); mainMenu.Enabled = false; SetWorkspaceEnabled(false); start.Enabled = false;
        try
        {
            await RefreshWoWLibrary();
            using var launch = new ClientLaunchForm(slot, layout, settings, SaveNow);
            var result = launch.ShowDialog(this);
            if (result == DialogResult.OK)
            {
                var draft = launch.Result;
                slot.Name = draft.Name; slot.Executable = draft.Executable; slot.LaunchMethod = draft.LaunchMethod; slot.BattleNet = draft.BattleNet; launcher.Bindings.Remove(slot.Id);
                BindSlots(canvas.Selected); SaveNow(); RefreshClients();
                Log(slot.LaunchMethod == ClientLaunchMethod.BattleNet ? $"{slot.Name}: Battle.net · {slot.BattleNet!.AccountLabel} · {slot.BattleNet.GameVersion}." : $"{slot.Name}: direct/private-server launch.");
                return;
            }
            if (result != DialogResult.Retry) return;
            var directDraft = launch.Result;
            window = Matching.Assign(new Layout { Executable = layout.Executable, Slots = new() { directDraft } }, Native.Windows(new[] { directDraft.PathFor(layout) }), launcher.Bindings).GetValueOrDefault(slot.Id);
            var edit = await ClientLoginEditor.Edit(this, directDraft, layout, window);
            if (edit is null) return;
            slot.Name = directDraft.Name; slot.Executable = directDraft.Executable; slot.LaunchMethod = ClientLaunchMethod.Direct; slot.Login = edit.Login; launcher.Bindings.Remove(slot.Id); BindSlots(canvas.Selected); SaveNow();
            Log(slot.Login is null ? $"{slot.Name}: private-server login cleared." : $"{slot.Name}: private-server login details saved.");
            if (!edit.LoginNow || window is null) return;
            launchCancellation = new(); start.Visible = apply.Visible = false; cancel.Visible = cancel.Enabled = true; SetStatus("Logging in · Escape stops");
            await ClientLoginService.Run(slot, window, false, Log, launchCancellation.Token);
        }
        catch (OperationCanceledException) { Log("Login stopped. No further credentials will be entered."); }
        catch (Exception ex) { Log("Login skipped: " + ex.Message); MessageBox.Show(this, ex.Message, "Client login"); }
        finally
        {
            busy = false; mainMenu.Enabled = true; SetWorkspaceEnabled(true); start.Enabled = start.Visible = true; cancel.Visible = cancel.Enabled = false; SetStatus("");
            hover?.Suspend(false); launchCancellation?.Dispose(); launchCancellation = null; RefreshClients();
        }
    }
}

