using System.Diagnostics;
using System.Runtime.InteropServices;
using Accessibility;

namespace HoverDeck;

// Uses Battle.net's named accessibility controls, not coordinates or login tokens.
internal sealed class BattleNetUi
{
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(nint window, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);
    [DllImport("oleacc.dll")] private static extern int AccessibleChildren(IAccessible parent, int start, int count, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Struct, SizeParamIndex = 2)] object[] children, out int obtained);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    private readonly nint handle;
    private readonly CancellationToken token;
    private Node? versionSelector, accountSelector;
    private static readonly SemaphoreSlim gate = new(1, 1);
    internal static Action<string>? Diagnostic;
    private BattleNetUi(nint handle, CancellationToken token) { this.handle = handle; this.token = token; }
    private sealed record Node(IAccessible Accessible, object Child)
    {
        public string Name => Accessible.get_accName(Child) ?? "";
        public AccessibleRole Role => (AccessibleRole)Convert.ToInt32(Accessible.get_accRole(Child));
        public int State => Convert.ToInt32(Accessible.get_accState(Child));
    }
    public static async Task<T> Run<T>(Func<BattleNetUi, T> action, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { return await Task.Run(() => action(Connect(token)), token); }
        finally { gate.Release(); }
    }
    private static BattleNetUi Connect(CancellationToken token)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcessesByName("Battle.net"))
            using (process) { var path = Native.ProcessPath((uint)process.Id); if (Path.GetFileName(path).Equals("Battle.net.exe", StringComparison.OrdinalIgnoreCase)) paths.Add(path); }
        var windows = Native.Windows(paths).Where(w => w.Title == "Battle.net").ToList();
        if (windows.Count != 1) throw new InvalidOperationException("Open one Battle.net launcher, sign in there, and select World of Warcraft before continuing.");
        if (windows[0].Minimized) Native.ShowWindowAsync(windows[0].Handle, 4);
        SetForegroundWindow(windows[0].Handle);
        if (token.WaitHandle.WaitOne(250)) token.ThrowIfCancellationRequested();
        return new(windows[0].Handle, token);
    }
    private void Check()
    {
        token.ThrowIfCancellationRequested();
        if (!Native.IsWindow(handle)) throw new InvalidOperationException("Battle.net closed. Open it and try again.");
        if ((Native.GetAsyncKeyState(0x1B) & 0x8000) != 0) throw new OperationCanceledException("Battle.net launch stopped with Escape.");
    }
    private Node Root()
    {
        var iid = typeof(IAccessible).GUID;
        int status = AccessibleObjectFromWindow(handle, 0xFFFFFFFC, ref iid, out var root);
        if (status < 0 || root is null) throw new InvalidOperationException("Battle.net did not expose its game controls. Open its WoW page and try again.");
        return new(root, 0);
    }
    private static List<Node> Children(Node parent)
    {
        if (parent.Child is not int id || id != 0) return new();
        int count = Math.Min(parent.Accessible.accChildCount, 600);
        if (count <= 0) return new();
        var buffer = new object[count];
        if (AccessibleChildren(parent.Accessible, 0, count, buffer, out int obtained) < 0) return new();
        var result = new List<Node>();
        foreach (var child in buffer.Take(obtained))
        {
            if (child is IAccessible accessible) result.Add(new(accessible, 0));
            else if (child is int childId)
            {
                object? expanded = null;
                try { expanded = parent.Accessible.get_accChild(childId); } catch (COMException) { }
                result.Add(expanded is IAccessible full ? new(full, 0) : new(parent.Accessible, childId));
            }
        }
        return result;
    }
    private List<Node> Find(AccessibleRole role, Func<string, bool> predicate, Node? scope = null)
    {
        var level = new List<Node> { scope ?? Root() };
        for (int depth = 0; depth < 48 && level.Count > 0; depth++)
        {
            Check(); var matches = new List<Node>(); var next = new List<Node>();
            foreach (var parent in level)
                try
                {
                    foreach (var child in Children(parent))
                    {
                        var childRole = child.Role;
                        if (Diagnostic is not null)
                        {
                            string name = child.Name;
                            if (name.StartsWith("Game Version ") || name.StartsWith("Game Account,")) Diagnostic($"Selector at level {depth}: role {childRole}, state {child.State}");
                        }
                        // Some launcher containers report offscreen even while their children are visible.
                        if (childRole == role && predicate(child.Name) && (child.State & 0x8000) == 0) matches.Add(child);
                        next.Add(child);
                    }
                }
                catch (COMException) { }
            Diagnostic?.Invoke($"Accessibility level {depth}: {next.Count} controls, {matches.Count} requested controls");
            if (matches.Count > 0) return matches;
            if (next.Count > 600) break;
            level = next;
        }
        return new();
    }
    private Node Unique(AccessibleRole role, Func<string, bool> predicate, string missing)
    {
        var matches = Find(role, predicate);
        return matches.Count == 1 ? matches[0] : throw new InvalidOperationException(missing);
    }
    private Node Selector(bool account)
    {
        ref Node? cached = ref (account ? ref accountSelector : ref versionSelector);
        try { if (cached is not null && cached.Name.StartsWith(account ? "Game Account, Currently Selected " : "Game Version ", StringComparison.Ordinal)) return cached; }
        catch (COMException) { cached = null; }
        return cached = Unique(AccessibleRole.ButtonMenu,
            name => name.StartsWith(account ? "Game Account, Currently Selected " : "Game Version ", StringComparison.Ordinal),
            account ? "Battle.net's WoW account selector is unavailable. Sign in and select the installed WoW game first." : "Open Battle.net's World of Warcraft page. This integration currently uses the English launcher controls.");
    }
    private string Selected(bool account) => Selector(account).Name[(account ? "Game Account, Currently Selected " : "Game Version ").Length..].Trim();
    private void Act(Node control)
    {
        Check();
        if (Native.GetForegroundWindow() != handle) throw new OperationCanceledException("Battle.net selection stopped because another window took focus.");
        if ((control.State & 1) != 0) throw new InvalidOperationException("Battle.net is still busy. Wait for it to finish, then retry.");
        if (string.IsNullOrEmpty(control.Accessible.get_accDefaultAction(control.Child))) throw new InvalidOperationException("Battle.net did not expose an action for the requested control.");
        control.Accessible.accDoDefaultAction(control.Child);
    }
    private T Wait<T>(Func<T?> read, string error) where T : class
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(8))
        {
            Check(); var value = read(); if (value is not null) return value;
            if (token.WaitHandle.WaitOne(100)) token.ThrowIfCancellationRequested();
        }
        throw new InvalidOperationException(error);
    }
    private Node OpenMenu(bool account)
    {
        var selector = Selector(account); string name = selector.Name;
        if ((selector.State & 0x200) == 0) Act(selector);
        return Wait(() => Find(AccessibleRole.MenuPopup, n => n == name).SingleOrDefault(), "Battle.net did not open its selection menu.");
    }
    private List<string> Choices(bool account)
    {
        var menu = OpenMenu(account);
        var choices = Find(AccessibleRole.MenuItem, n => n.Length > 0, menu).Select(n => n.Name).ToList();
        CloseMenu(account); return choices;
    }
    private void CloseMenu(bool account)
    {
        var selector = Selector(account);
        if ((selector.State & 0x200) != 0) Act(selector);
        Wait(() => (Selector(account).State & 0x200) == 0 ? "closed" : null, "Battle.net did not close its selection menu.");
    }
    private void Select(bool account, string name)
    {
        if (Selected(account) == name) return;
        var menu = OpenMenu(account); var matches = Find(AccessibleRole.MenuItem, n => n == name, menu);
        if (matches.Count != 1) { CloseMenu(account); throw new InvalidOperationException($"Battle.net no longer lists '{name}'. Refresh the launch settings."); }
        Act(matches[0]);
        if (!account) { versionSelector = null; accountSelector = null; }
        Wait(() => { try { return Selected(account) == name ? "selected" : null; } catch (InvalidOperationException) { return null; } }, "Battle.net did not confirm the requested selection.");
    }
    private static string TabForVersion(string version) => version.Contains("Forever", StringComparison.OrdinalIgnoreCase) ? "game-nav-btn-WoWF" : version.Contains("Classic", StringComparison.OrdinalIgnoreCase) || version.Contains("Anniversary", StringComparison.OrdinalIgnoreCase) ? "game-nav-btn-WoWC" : "game-nav-btn-WoW";
    public BattleNetChoices ReadChoices(string? requestedVersion = null)
    {
        if (!string.IsNullOrEmpty(requestedVersion)) Select(false, requestedVersion);
        string version = Selected(false), account = Selected(true);
        return new(TabForVersion(version), version, account, Choices(false), Choices(true));
    }
    private void Navigate(string tabId)
    {
        string name = tabId switch { "game-nav-btn-WoWC" => "World of Warcraft Classic", "game-nav-btn-WoWF" => "WoW: Forever", "game-nav-btn-WoW" => "World of Warcraft", _ => throw new InvalidOperationException("Choose a World of Warcraft game version.") };
        Act(Unique(AccessibleRole.PageTab, n => n == name, "The saved WoW page is not visible in Battle.net. Add it to Favorites or open it manually."));
        Wait(() => { try { return Selector(false); } catch (InvalidOperationException) { return null; } }, "Battle.net did not show its game controls.");
    }
    public string Prepare(BattleNetLaunch profile)
    {
        try { Selected(false); } catch (InvalidOperationException) { Navigate(profile.GameTabId); }
        Select(false, profile.GameVersion);
        string account = BattleNetAccounts.Resolve(profile, Choices(true)); Select(true, account);
        Verify(profile, account); return account;
    }
    private void Verify(BattleNetLaunch profile, string account)
    {
        Check();
        if (Selected(false) != profile.GameVersion || Selected(true) != account) throw new InvalidOperationException("Battle.net's version/account changed during launch. No Play action was sent.");
    }
    public bool Play(BattleNetLaunch profile, string account)
    {
        Verify(profile, account);
        var play = Unique(AccessibleRole.PushButton, name => name.StartsWith("Play: " + profile.GameVersion + ", Version:", StringComparison.Ordinal), "Battle.net is not ready to Play this game. Finish installation/update or any launcher prompts, then retry.");
        Verify(profile, account); Act(play); return true;
    }
}

