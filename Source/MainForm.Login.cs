namespace HoverDeck;

public sealed partial class MainForm
{
    private Slot? deleteAfterEdit;
    // Asks first; a layout keeps at least one client. Deleting never closes a running game.
    private void DeleteClient(Slot slot)
    {
        if (busy || displayApplying || arrangeDisplays.Checked || !layout.Slots.Contains(slot)) return;
        if (layout.Slots.Count <= 1) { ThemedMessageBox.Show(this, "A layout needs at least one client, so the last client can't be deleted.", "Delete client"); return; }
        if (ThemedMessageBox.Show(this, $"Delete {slot.Name}?\n\nIts position, launch settings and saved login are removed from this layout. A game that is running is not closed.", "Delete client", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        RemoveClient(slot);
    }
    internal void RemoveClient(Slot slot)
    {
        int index = layout.Slots.IndexOf(slot);
        if (index < 0 || layout.Slots.Count <= 1) return;
        layout.Slots.RemoveAt(index); launcher.Bindings.Remove(slot.Id);
        syncing = true; count.Value = layout.Slots.Count; syncing = false;
        BindSlots(Math.Min(index, layout.Slots.Count - 1)); SaveNow(); RefreshClients();
        Log($"{slot.Name} deleted from {layout.Name}.");
    }
    private async Task ConfigureClientLogin()
    {
        if (busy || displayApplying || arrangeDisplays.Checked || CurrentSlot is not { } slot) return;
        launcher.Assign(layout, launcher.Scan(layout)); // keeps the bindings current for the login editor below
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
            if (result == DialogResult.Abort) { deleteAfterEdit = slot; return; }
            if (result != DialogResult.Retry) return;
            var directDraft = launch.Result;
            var window = Matching.Assign(new Layout { Executable = layout.Executable, Slots = new() { directDraft } }, Native.Windows(new[] { directDraft.PathFor(layout) }), launcher.Bindings).GetValueOrDefault(slot.Id);
            var edit = await ClientLoginEditor.Edit(this, directDraft, layout, window);
            if (edit is null) return;
            slot.Name = directDraft.Name; slot.Executable = directDraft.Executable; slot.LaunchMethod = ClientLaunchMethod.Direct; slot.Login = edit.Login; launcher.Bindings.Remove(slot.Id); BindSlots(canvas.Selected); SaveNow();
            Log(slot.Login is null ? $"{slot.Name}: private-server login cleared." : $"{slot.Name}: private-server login details saved.");
            if (!edit.LoginNow || window is null) return;
            launchCancellation = new(); start.Visible = apply.Visible = false; cancel.Visible = cancel.Enabled = true; SetStatus("Logging in · Escape stops");
            await ClientLoginService.Run(slot, window, false, Log, launchCancellation.Token);
        }
        catch (OperationCanceledException) { Log("Login stopped. No further credentials will be entered."); }
        catch (Exception ex) { Log("Login skipped: " + ex.Message); ThemedMessageBox.Show(this, ex.Message, "Client login"); }
        finally
        {
            busy = false; mainMenu.Enabled = true; SetWorkspaceEnabled(true); start.Enabled = start.Visible = true; cancel.Visible = cancel.Enabled = false; SetStatus("");
            hover?.Suspend(false); launchCancellation?.Dispose(); launchCancellation = null; RefreshClients();
            if (deleteAfterEdit is { } doomed) { deleteAfterEdit = null; BeginInvoke(() => DeleteClient(doomed)); }
        }
    }
}

