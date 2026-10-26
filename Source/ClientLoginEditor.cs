namespace HoverDeck;

internal sealed record LoginEditResult(ClientLogin? Login, bool LoginNow);

internal static class ClientLoginEditor
{
    public static async Task<LoginEditResult?> Edit(Form owner, Slot slot, Layout layout, GameWindow? window)
    {
        var draft = slot.Login;
        while (!owner.IsDisposed && !owner.Disposing)
        {
            DialogResult action;
            using (var dialog = new ClientLoginForm(slot, layout, window, draft))
            {
                action = dialog.ShowDialog(owner);
                if (action == DialogResult.OK) return new(dialog.Result, dialog.LoginNow);
                if (action != DialogResult.Retry || dialog.Result is null || window is null) return null;
                draft = dialog.Result;
            }

            // ShowDialog has returned and the old form is disposed before any
            // asynchronous work. Keep the draft encrypted until the next dialog.
            try
            {
                await Task.Delay(150);
                if (owner.IsDisposed || owner.Disposing) return null;
                await LoginInput.Activate(window, CancellationToken.None);
                using var capture = LoginInput.Capture(window, CancellationToken.None);
                using var picker = new LoginFieldPicker(capture);
                if (picker.ShowDialog(owner) == DialogResult.OK) draft.Screen = picker.Result;
            }
            catch (Exception ex)
            {
                if (owner.IsDisposed || owner.Disposing) return null;
                MessageBox.Show(owner, ex.Message, "Login screen setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        return null;
    }
}
