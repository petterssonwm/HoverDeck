using System.Diagnostics;

namespace HoverDeck;

public sealed record LaunchedClient(string SlotId, string Identity);

public sealed class Launcher
{
    public readonly Dictionary<string, string> Bindings = new();
    // Keep processes that were started but did not produce a window yet. A retry
    // waits for them instead of silently creating a duplicate client.
    private readonly Dictionary<string, Process> pending = new();
    private readonly HashSet<string> startedHere = new();
    private readonly IBattleNetGateway battleNet;
    public Launcher() : this(new BattleNetGateway()) { }
    internal Launcher(IBattleNetGateway battleNet) { this.battleNet = battleNet; }
    public List<GameWindow> Scan(Layout layout) => Native.Windows(layout.Slots.Select(s => s.PathFor(layout)));
    public Dictionary<string, GameWindow> Assign(Layout layout, IReadOnlyList<GameWindow> windows)
    {
        var assigned = Matching.Assign(layout, windows, Bindings);
        foreach (var pair in assigned) Bindings[pair.Key] = pair.Value.Identity;
        return assigned;
    }
    public async Task<List<LaunchedClient>> Run(Layout layout, IReadOnlyList<MonitorInfo> monitors, bool launchMissing, Action<string> log, CancellationToken token, Action? saveBindings = null)
    {
        var newClients = new List<LaunchedClient>();
        if (layout.Slots.Count == 0) throw new InvalidOperationException("Add at least one client slot.");
        foreach (var slot in layout.Slots)
        {
            var path = slot.PathFor(layout);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Choose a valid game executable for {slot.Name}.");
            if (!Matching.OnScreen(slot.Bounds, monitors)) throw new InvalidOperationException($"{slot.Name} is outside the connected displays. Move it onto a display before starting.");
            if (slot.LaunchMethod == ClientLaunchMethod.BattleNet && slot.BattleNet is not { IsConfigured: true }) throw new InvalidOperationException($"Configure Battle.net in Edit client for {slot.Name}.");
        }
        var bnetSlots = layout.Slots.Where(s => s.LaunchMethod == ClientLaunchMethod.BattleNet).ToList();
        if (bnetSlots.GroupBy(s => (s.BattleNet!.GameVersion, s.BattleNet.AccountKey)).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Two slots request the same WoW account and game version. Choose a different WoW account for each client.");
        if (bnetSlots.Any(b => layout.Slots.Any(d => d.LaunchMethod == ClientLaunchMethod.Direct && Matching.SamePath(d.PathFor(layout), b.PathFor(layout)))))
            throw new InvalidOperationException("Slots using the same installed game must use the same launch method. Configure its accounts through Battle.net for each slot.");
        var assigned = Assign(layout, Scan(layout));
        var initial = assigned.Count;
        int launched = 0;
        // Open clients are placed first, so a stuck launch cannot hold them up. A launch still pending from an
        // earlier attempt comes next, so its window, once open, is not mistaken for an untracked client by a
        // new launch. Otherwise the layout's order is kept.
        var open = assigned.Keys.ToHashSet();
        foreach (var slot in layout.Slots.OrderBy(s => open.Contains(s.Id) ? 0 : s.BattleNet is { PendingSinceUtc.Length: > 0 } ? 1 : 2))
        {
            token.ThrowIfCancellationRequested();
            // Reconcile again so a client opened by the user during launch is reused.
            var windows = Scan(layout);
            assigned = Assign(layout, windows);
            if (!assigned.TryGetValue(slot.Id, out var window))
            {
                if (!launchMissing) { log($"{slot.Name}: no matching open client."); continue; }
                if (slot.LaunchMethod == ClientLaunchMethod.BattleNet)
                {
                    // Battle.net logs a starting game in with the account selected at that moment, so the
                    // next client's account is chosen only after this window has opened and settled.
                    bool fresh = string.IsNullOrEmpty(slot.BattleNet!.PendingSinceUtc);
                    window = await OpenBattleNet(slot, layout, windows, log, token, saveBindings);
                    if (fresh) launched++;
                    newClients.Add(new(slot.Id, window.Identity));
                    await Task.Delay(1200, token);
                }
                else
                {
                var path = Path.GetFullPath(slot.PathFor(layout));
                var before = windows.Select(w => w.Identity).ToHashSet();
                if (!pending.TryGetValue(slot.Id, out var process) || HasExited(process))
                {
                    process?.Dispose();
                    // An existing process may still be loading its first window.
                    process = FindLoadingProcess(path, before);
                    if (process is not null) log($"Waiting for an already-starting client: {slot.Name}…");
                    else
                    {
                        log($"Opening {slot.Name}…");
                        process = Process.Start(new ProcessStartInfo(path, layout.Arguments) { WorkingDirectory = Path.GetDirectoryName(path)!, UseShellExecute = false })
                            ?? throw new InvalidOperationException("Windows did not start the executable.");
                        startedHere.Add($"{process.Id}:{process.StartTime.ToUniversalTime().Ticks}");
                        launched++;
                    }
                    pending[slot.Id] = process;
                }
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromSeconds(90))
                {
                    token.ThrowIfCancellationRequested();
                    var candidates = Scan(layout);
                    var used = assigned.Values.Select(w => w.Identity).ToHashSet();
                    window = candidates.FirstOrDefault(w => w.ProcessId == process.Id && !used.Contains(w.Identity));
                    window ??= candidates.FirstOrDefault(w => Matching.SamePath(w.Executable, path) && !used.Contains(w.Identity) && !before.Contains(w.Identity));
                    if (window is not null) break;
                    if (HasExited(process)) throw new InvalidOperationException($"{slot.Name} exited without a game window. Select the game's executable (for example Wow.exe), or open it in its launcher first.");
                    await Task.Delay(250, token);
                }
                if (window is null) throw new TimeoutException($"{slot.Name} has no game window after 90 seconds. Its process was left running; Start will wait for it again. Finish any launcher or login steps manually.");
                Bindings[slot.Id] = window.Identity;
                if (startedHere.Remove(window.Identity)) newClients.Add(new(slot.Id, window.Identity));
                pending.Remove(slot.Id); process.Dispose();
                // Give a newly created game window time to apply its saved graphics settings.
                await Task.Delay(1200, token);
                }
            }
            Native.Place(window, slot.Bounds);
            await Task.Delay(350, token);
            Native.Place(window, slot.Bounds);
            log($"{slot.Name}: placed at {slot.X}, {slot.Y} · {slot.Width} × {slot.Height}.");
        }
        // Games can replace or resize their HWND during initialization. Reacquire
        // current windows and reapply the layout once at the end, without activating.
        await Task.Delay(500, token);
        assigned = Assign(layout, Scan(layout));
        int mismatched = 0;
        foreach (var slot in layout.Slots)
        {
            if (!assigned.TryGetValue(slot.Id, out var w)) continue;
            Native.Place(w, slot.Bounds);
        }
        await Task.Delay(400, token);
        assigned = Assign(layout, Scan(layout));
        foreach (var slot in layout.Slots)
            if (assigned.TryGetValue(slot.Id, out var w) && w.Bounds != slot.Bounds) mismatched++;
        log($"Ready · reused {initial}, opened {launched}, matched {assigned.Count}/{layout.Slots.Count}." + (mismatched > 0 ? $" {mismatched} client(s) did not accept the exact size; use windowed mode and Apply positions." : ""));
        return newClients;
    }
    private async Task<GameWindow> OpenBattleNet(Slot slot, Layout layout, List<GameWindow> windows, Action<string> log, CancellationToken token, Action? saveBindings)
    {
        var profile = slot.BattleNet!; string path = slot.PathFor(layout);
        if (string.IsNullOrEmpty(profile.PendingSinceUtc))
        {
            var assigned = Assign(layout, windows);
            var known = assigned.Values.Select(w => w.Identity).ToHashSet();
            if (windows.Any(w => Matching.SamePath(w.Executable, path) && !known.Contains(w.Identity)))
                throw new InvalidOperationException($"{slot.Name}: an open WoW client is not tracked by HoverDeck. Close that client, then use Start clients to launch the selected account.");
            using var loading = FindLoadingProcess(path, windows.Select(w => w.Identity).ToHashSet());
            if (loading is not null) throw new InvalidOperationException("A WoW client is already starting outside HoverDeck. Wait for it to open, close it, then use Start clients.");
            log($"{slot.Name}: selecting {profile.AccountLabel} · {profile.GameVersion} in Battle.net…");
            string resolved = await battleNet.Prepare(profile, log, token);
            if (layout.Slots.Any(s => s.Id != slot.Id && s.LaunchMethod == ClientLaunchMethod.BattleNet && s.BattleNet is { } other
                && other.ConnectionId == profile.ConnectionId && other.GameVersion == profile.GameVersion && other.ResolvedAccount == resolved && (other.BoundIdentity.Length > 0 || other.PendingSinceUtc.Length > 0)))
                throw new InvalidOperationException($"{resolved} is already assigned to another slot for this game version.");
            token.ThrowIfCancellationRequested();
            profile.ResolvedAccount = resolved; profile.BeforeLaunch = Scan(layout).Select(w => w.Identity).ToList();
            profile.PendingSinceUtc = DateTimeOffset.UtcNow.ToString("O"); profile.BoundIdentity = ""; saveBindings?.Invoke();
            // Save before Play. An uncertain outcome remains pending so a retry
            // waits instead of sending a second launch request.
            try { await battleNet.Play(profile, resolved, token); }
            catch (PlayNotSentException ex)
            {
                profile.ClearBinding(); saveBindings?.Invoke();
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerException!);
            }
            log($"{slot.Name}: Play sent for {resolved}. Waiting for its game window…");
        }
        else log($"{slot.Name}: waiting for its pending Battle.net launch; Play will not be repeated.");
        var before = profile.BeforeLaunch.ToHashSet();
        DateTimeOffset.TryParse(profile.PendingSinceUtc, out var since);
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(90))
        {
            token.ThrowIfCancellationRequested();
            var known = layout.Slots.Where(s => s.Id != slot.Id).Select(s => s.BattleNet?.BoundIdentity ?? "").ToHashSet();
            var candidates = Scan(layout).Where(w => Matching.SamePath(w.Executable, path) && !before.Contains(w.Identity) && !known.Contains(w.Identity)
                && w.Started >= since.UtcDateTime.AddSeconds(-2).Ticks).ToList();
            if (candidates.Count > 1) throw new InvalidOperationException("Several new WoW clients appeared. Close the extra clients, leaving only the one requested through HoverDeck, then use Start clients to resume.");
            if (candidates.Count == 1)
            {
                var window = candidates[0]; profile.BoundIdentity = window.Identity; profile.PendingSinceUtc = ""; profile.BeforeLaunch.Clear();
                Bindings[slot.Id] = window.Identity; saveBindings?.Invoke(); return window;
            }
            await Task.Delay(250, token);
        }
        throw new TimeoutException($"{slot.Name}: Battle.net did not produce the configured game window. Complete any launcher prompts, then use Start clients to keep waiting for this launch.");
    }
    private static bool HasExited(Process p) { try { return p.HasExited; } catch { return true; } }
    // Asks each open client of the layout to close, exactly like clicking its window's X,
    // and waits for the game processes to exit. Nothing is force-killed: clients that are
    // still running after the timeout are returned so the caller can report them.
    public async Task<List<Slot>> Close(Layout layout, Action<string> log, CancellationToken token, TimeSpan? timeout = null)
    {
        var open = Assign(layout, Scan(layout));
        var waiting = layout.Slots.Where(s => open.ContainsKey(s.Id)).Select(s => (Slot: s, Window: open[s.Id])).ToList();
        foreach (var (slot, window) in waiting) { log($"Closing {slot.Name}…"); Native.PostMessage(window.Handle, 0x0010, 0, 0); } // WM_CLOSE
        var watch = Stopwatch.StartNew(); var limit = timeout ?? TimeSpan.FromSeconds(30);
        while (waiting.Count > 0 && watch.Elapsed < limit)
        {
            await Task.Delay(250, token);
            foreach (var done in waiting.Where(w => !Running(w.Window)).ToList()) { waiting.Remove(done); Bindings.Remove(done.Slot.Id); log($"{done.Slot.Name}: closed."); }
        }
        return waiting.Select(w => w.Slot).ToList();
    }
    private static bool Running(GameWindow window)
    {
        try { using var p = Process.GetProcessById(window.ProcessId); return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == window.Started; }
        catch { return false; }
    }
    private static Process? FindLoadingProcess(string path, HashSet<string> visible)
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
        {
            try
            {
                if (Matching.SamePath(Native.ProcessPath((uint)p.Id), path) && !visible.Contains($"{p.Id}:{p.StartTime.ToUniversalTime().Ticks}") && !p.HasExited) return p;
            }
            catch { }
            p.Dispose();
        }
        return null;
    }
}

