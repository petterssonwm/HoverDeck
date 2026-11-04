using System.Runtime.InteropServices;

namespace HoverDeck;

// Connects WoW windows that are already open to the layout's clients: one card per open window with a
// live preview (drawn by Windows itself, so covered or overlapping windows still show their own content)
// and a choice of client. HoverDeck can't see which account is logged in to a running game, so the
// player decides; the choice is pre-filled from the current connection or the nearest spot on the map.
internal sealed class ConnectClientsForm : Form
{
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);
    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbnailProperties
    {
        public uint Flags; public Native.RECT Destination, Source; public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool ClientAreaOnly;
    }
    private const int PreviewWidth = 320, PreviewHeight = 180;
    private sealed record SlotItem(Slot Slot, string Label) { public override string ToString() => Label; }
    private readonly List<(GameWindow Window, Panel Preview, DarkComboBox Choice, nint Thumbnail)> cards = new();
    private readonly Label note = new() { AutoSize = true, MaximumSize = new(700, 0), ForeColor = Color.FromArgb(167, 174, 187), Margin = new(0, 0, 0, 12), Name = "ConnectNote" };
    public List<(GameWindow Window, Slot? Slot)> Choices => cards.Select(c => (c.Window, (c.Choice.SelectedItem as SlotItem)?.Slot)).ToList();

    public ConnectClientsForm(Layout layout, IReadOnlyList<GameWindow> windows, IReadOnlyDictionary<string, GameWindow> assigned, IReadOnlyList<MonitorInfo> monitors, string intro)
    {
        Text = "Connect open clients"; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = MaximizeBox = false;
        Font = new("Segoe UI", 10); BackColor = AppTheme.Surface; ForeColor = AppTheme.Ink;
        int columns = windows.Count == 1 ? 1 : 2, rowsNeeded = (windows.Count + columns - 1) / columns;
        // Room for every card side by side (two per row) without scrolling, up to four rows of windows.
        const int CardWidth = PreviewWidth + 20 + 14, CardHeight = PreviewHeight + 80 + 14;
        ClientSize = new(columns * CardWidth + 44, 140 + Math.Min(rowsNeeded, 2) * CardHeight);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new(20, 18, 20, 16) };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 46));
        note.Text = intro; root.Controls.Add(note, 0, 0);
        var grid = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Margin = Padding.Empty };
        root.Controls.Add(grid, 0, 1);
        var items = layout.Slots.Select(s => new SlotItem(s, s.Name + " · " + (s.LaunchMethod == ClientLaunchMethod.BattleNet ? s.BattleNet?.AccountLabel ?? "Battle.net" : "Direct / private server"))).ToList();
        var taken = new HashSet<Slot>();
        // Pre-fill: the client a window is already connected to, otherwise the nearest free spot on the map.
        var current = windows.ToDictionary(w => w, w => layout.Slots.FirstOrDefault(s => assigned.TryGetValue(s.Id, out var a) && a.Identity == w.Identity));
        foreach (var slot in current.Values.OfType<Slot>()) taken.Add(slot);
        foreach (var (window, index) in windows.Select((w, i) => (w, i)))
        {
            var card = new Panel { Width = PreviewWidth + 20, Height = PreviewHeight + 80, Margin = new(0, 0, 14, 14), BackColor = AppTheme.Field };
            var preview = new Panel { Bounds = new(10, 10, PreviewWidth, PreviewHeight), BackColor = Color.Black };
            int display = monitors.ToList().FindIndex(m => m.Bounds.Contains(new Point(window.Bounds.X + window.Bounds.Width / 2, window.Bounds.Y + window.Bounds.Height / 2)));
            var where = new Label { Text = $"WoW window {index + 1}" + (display >= 0 ? " · Display " + LayoutCanvas.DisplayNumber(monitors[display], display) : "") + $" · {window.Bounds.Width} × {window.Bounds.Height}" + (window.Minimized ? " · minimized" : ""),
                AutoSize = false, Bounds = new(10, PreviewHeight + 16, PreviewWidth, 20), ForeColor = AppTheme.Muted, Font = new("Segoe UI", 9f) };
            var choice = new DarkComboBox { Bounds = new(10, PreviewHeight + 40, PreviewWidth, 30), Name = "ConnectChoice" + index };
            choice.Items.Add("Not connected"); choice.Items.AddRange(items.Cast<object>().ToArray());
            var slot = current[window] ?? layout.Slots.Where(s => !taken.Contains(s) && Matching.SamePath(s.PathFor(layout), window.Executable)).OrderBy(s => Matching.Distance(s.Bounds, window.Bounds)).FirstOrDefault();
            if (slot is not null) taken.Add(slot);
            choice.SelectedItem = items.FirstOrDefault(i => i.Slot == slot) ?? (object)"Not connected";
            card.Controls.AddRange(new Control[] { preview, where, choice }); grid.Controls.Add(card);
            cards.Add((window, preview, choice, 0));
        }
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
        var connect = new Button { Text = "Connect", Width = 120, Height = 34, Name = "ConnectApply" }; AppTheme.Primary(connect);
        var cancel = new Button { Text = "Cancel", Width = 110, Height = 34, DialogResult = DialogResult.Cancel, Margin = new(0, 0, 8, 0) };
        actions.Controls.AddRange(new Control[] { connect, cancel }); root.Controls.Add(actions, 0, 2);
        Controls.Add(root); AcceptButton = connect; CancelButton = cancel;
        connect.Click += (_, _) =>
        {
            var twice = Choices.Where(c => c.Slot is not null).GroupBy(c => c.Slot).FirstOrDefault(g => g.Count() > 1);
            if (twice is not null) { note.Text = $"{twice.Key!.Name} is chosen for more than one window. Pick a different client for one of them."; note.ForeColor = Color.FromArgb(237, 185, 109); return; }
            DialogResult = DialogResult.OK; Close();
        };
        Shown += (_, _) => { ShowPreviews(); grid.Scroll += (_, _) => PlacePreviews(); };
        Resize += (_, _) => PlacePreviews();
        FormClosed += (_, _) => { foreach (var c in cards) if (c.Thumbnail != 0) DwmUnregisterThumbnail(c.Thumbnail); };
        DialogFrame.Apply(this);
    }
    private void ShowPreviews()
    {
        for (int i = 0; i < cards.Count; i++)
            if (DwmRegisterThumbnail(Handle, cards[i].Window.Handle, out var thumbnail) == 0) cards[i] = (cards[i].Window, cards[i].Preview, cards[i].Choice, thumbnail);
        PlacePreviews();
    }
    // Each preview keeps its window's shape inside its black frame.
    private void PlacePreviews()
    {
        foreach (var (window, preview, _, thumbnail) in cards)
        {
            if (thumbnail == 0) continue;
            var frame = RectangleToClient(preview.RectangleToScreen(preview.ClientRectangle));
            double scale = Math.Min(frame.Width / (double)Math.Max(1, window.Bounds.Width), frame.Height / (double)Math.Max(1, window.Bounds.Height));
            int w = (int)(window.Bounds.Width * scale), h = (int)(window.Bounds.Height * scale);
            var target = new Rectangle(frame.X + (frame.Width - w) / 2, frame.Y + (frame.Height - h) / 2, w, h);
            var visible = Rectangle.Intersect(target, RectangleToClient(preview.Parent!.Parent!.RectangleToScreen(preview.Parent.Parent.ClientRectangle)));
            var properties = new ThumbnailProperties { Flags = 0x1 | 0x4 | 0x8 | 0x10, Opacity = 255, Visible = visible == target, ClientAreaOnly = true,
                Destination = new() { Left = target.Left, Top = target.Top, Right = target.Right, Bottom = target.Bottom } };
            DwmUpdateThumbnailProperties(thumbnail, ref properties);
        }
    }

    // Connects each window to its chosen client, and takes it away from any client it belonged to before.
    public static void Apply(Layout layout, Dictionary<string, string> bindings, IEnumerable<(GameWindow Window, Slot? Slot)> choices)
    {
        string Bound(Slot s) => s.LaunchMethod == ClientLaunchMethod.BattleNet ? s.BattleNet?.BoundIdentity ?? "" : bindings.GetValueOrDefault(s.Id) ?? "";
        void Unbind(Slot s) { if (s.BattleNet is { } p) { p.BoundIdentity = ""; p.PendingSinceUtc = ""; p.BeforeLaunch.Clear(); } bindings.Remove(s.Id); }
        var list = choices.ToList();
        foreach (var (window, _) in list) foreach (var old in layout.Slots.Where(s => Bound(s) == window.Identity)) Unbind(old);
        foreach (var (window, slot) in list)
        {
            if (slot is null) continue;
            if (slot.LaunchMethod == ClientLaunchMethod.BattleNet && slot.BattleNet is { } profile) { profile.BoundIdentity = window.Identity; profile.PendingSinceUtc = ""; profile.BeforeLaunch.Clear(); }
            bindings[slot.Id] = window.Identity;
        }
    }
}
