using System.Diagnostics;
using System.Runtime.InteropServices;
using Accessibility;

namespace HoverDeck;

// Uses Battle.net's named accessibility controls, not coordinates or login tokens.
internal sealed class BattleNetUi
{
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(nint window, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);
    [DllImport("oleacc.dll")] private static extern int AccessibleChildren(IAccessible parent, int start, int count, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Struct, SizeParamIndex = 2)] object[] children, out int obtained);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);
    private const string SignInNotice = "Battle.net isn't signed in. Sign in to Battle.net, then press Start clients again.";
    private static readonly Dictionary<string, string> TabNames = new() { ["game-nav-btn-WoW"] = "World of Warcraft", ["game-nav-btn-WoWC"] = "World of Warcraft Classic", ["game-nav-btn-WoWF"] = "WoW: Forever" };
    private static readonly ICollection<string> WoWTabs = TabNames.Values;
    private readonly nint handle;
    private readonly CancellationToken token;
    private Node? versionSelector, accountSelector;
    private static readonly SemaphoreSlim gate = new(1, 1);
    internal static Action<string>? Diagnostic;
    private BattleNetUi(nint handle, CancellationToken token) { this.handle = handle; this.token = token; }
    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider { [PreserveSig] int QueryService(ref Guid service, ref Guid riid, out nint result); }
    private static readonly Guid IAccessible2 = new("E89F726E-C4F4-4c19-BB19-B647D7FA8478");
    private sealed record Node(IAccessible Accessible, object Child)
    {
        public string Name => Accessible.get_accName(Child) ?? "";
        // Roles and states are integers in practice; anything else counts as "no role" instead of ending a tree walk.
        public AccessibleRole Role => Accessible.get_accRole(Child) is int role ? (AccessibleRole)role : AccessibleRole.None;
        public int State => Accessible.get_accState(Child) is int state ? state : 0;
    }
    public static async Task<T> Run<T>(Func<BattleNetUi, T> action, CancellationToken token, Action<string>? log = null)
    {
        await gate.WaitAsync(token);
        try
        {
            return await Task.Run(() =>
            {
                lock (stowLock) { stowTimer?.Dispose(); stowTimer = null; }
                var ui = Connect(token, log);
                try { return action(ui); }
                finally { ScheduleStow(ui.handle); }
            }, token);
        }
        finally { gate.Release(); }
    }
    // Battle.net stays invisible for a few seconds after each step, so the next step (Play, or
    // the next client's account) starts at once instead of restoring it again.
    private static readonly object stowLock = new();
    private static System.Threading.Timer? stowTimer;
    private static nint concealedWindow;
    private static void ScheduleStow(nint window)
    {
        lock (stowLock) { concealedWindow = window; stowTimer?.Dispose(); stowTimer = new System.Threading.Timer(_ => PutAwayNow(false), null, 4000, Timeout.Infinite); }
    }
    // Puts back a Battle.net that a HoverDeck which was killed left invisible and click-through.
    public static void RecoverOnStartup()
    {
        foreach (var window in Native.Windows(LauncherPaths()).Where(w => w.Title == "Battle.net"))
            if ((GetWindowLong(window.Handle, -20) & (Layered | ClickThrough)) == (Layered | ClickThrough)) Stow(window.Handle);
    }
    // Minimizes Battle.net and makes it visible again if HoverDeck still has it hidden; also called when HoverDeck exits.
    public static void PutAwayNow() => PutAwayNow(true);
    private static void PutAwayNow(bool exiting)
    {
        // The timer skips while a step runs, since that step schedules its own put-away. On exit
        // it waits for the step, but an abandoned step must not keep Battle.net hidden.
        bool held = gate.Wait(exiting ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
        if (!held && !exiting) return;
        try
        {
            nint window;
            lock (stowLock) { window = concealedWindow; concealedWindow = 0; stowTimer?.Dispose(); stowTimer = null; }
            if (window != 0) Stow(window);
        }
        finally { if (held) gate.Release(); }
    }
    // Battle.net is worked through its accessibility controls while it is invisible and
    // click-through, so it never takes focus and the user can keep using other windows; it is
    // minimized afterwards. Its version menu does not open while the window is minimized, so it
    // is restored invisibly for the work. A closed launcher (or one in the notification area)
    // is opened first and put away once it shows its games.
    private static BattleNetUi Connect(CancellationToken token, Action<string>? log)
    {
        nint kept;
        lock (stowLock) kept = concealedWindow;
        if (kept != 0 && Native.IsWindow(kept)) return new BattleNetUi(kept, token);
        var timer = Stopwatch.StartNew(); TimeSpan? opened = null; nint concealed = 0;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var paths = LauncherPaths();
                var windows = Native.Windows(paths);
                if (windows.FirstOrDefault(w => w.Title.Contains("Login", StringComparison.OrdinalIgnoreCase)) is { } login) { Reveal(login.Handle); throw new InvalidOperationException(SignInNotice); }
                var main = windows.Where(w => w.Title == "Battle.net").ToList();
                if (main.Count > 1) throw new InvalidOperationException("More than one Battle.net launcher is open. Close the extra one and try again.");
                if (main.Count == 1)
                {
                    // Hidden as soon as it appears; it is shown again only if it needs the user.
                    var ui = new BattleNetUi(main[0].Handle, token);
                    if (concealed != ui.handle) { concealed = ui.handle; ui.Conceal(); }
                    bool? signedIn = ui.SignedIn();
                    if (signedIn is null) { ui.Wake(); signedIn = ui.SignedIn(); }
                    if (signedIn == true) return ui;
                    if (signedIn == false) throw new InvalidOperationException(SignInNotice);
                }
                else if (opened is null || timer.Elapsed - opened.Value > TimeSpan.FromSeconds(15))
                {
                    // Starting Battle.net.exe again also brings a running launcher back from the notification area.
                    log?.Invoke("Opening Battle.net…");
                    Process.Start(new ProcessStartInfo(paths.FirstOrDefault() ?? InstalledPath()) { UseShellExecute = true })?.Dispose();
                    opened = timer.Elapsed;
                }
                if (timer.Elapsed > TimeSpan.FromSeconds(90))
                    throw new InvalidOperationException("Battle.net did not show World of Warcraft within 90 seconds. If it's asking you to sign in or update, finish that, then press Start clients again.");
                if (token.WaitHandle.WaitOne(main.Count == 0 ? 250 : 1000)) token.ThrowIfCancellationRequested();
            }
        }
        catch (Exception ex) when (concealed != 0 && Native.IsWindow(concealed))
        {
            if (ex is OperationCanceledException) Stow(concealed); else Reveal(concealed);
            throw;
        }
    }
    private static HashSet<string> LauncherPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcessesByName("Battle.net"))
            using (process) { var path = Native.ProcessPath((uint)process.Id); if (Path.GetFileName(path).Equals("Battle.net.exe", StringComparison.OrdinalIgnoreCase)) paths.Add(path); }
        return paths;
    }
    private static string InstalledPath()
    {
        foreach (var key in new[] { @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net" })
            try
            {
                using var uninstall = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
                var path = (uninstall?.GetValue("DisplayIcon") as string ?? "").Trim('"').Split(",", 2)[0];
                if (File.Exists(path)) return path;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net", "Battle.net.exe");
        return File.Exists(fallback) ? fallback : throw new InvalidOperationException("Battle.net is not installed. Install Battle.net, sign in, and try again.");
    }
    // True once the launcher shows its games, false when it shows a sign-in page, null while it is still loading.
    private bool? SignedIn()
    {
        Node root;
        try { root = Root(); } catch (InvalidOperationException) { return null; }
        var level = new List<Node> { root };
        for (int depth = 0; depth < 48 && level.Count > 0; depth++)
        {
            Check(); var next = new List<Node>();
            foreach (var parent in level)
                try
                {
                    foreach (var child in Children(parent))
                    {
                        var role = child.Role; next.Add(child);
                        if (role is not (AccessibleRole.ButtonMenu or AccessibleRole.PageTab or AccessibleRole.PushButton)) continue;
                        string name = child.Name;
                        if ((role == AccessibleRole.ButtonMenu && name.StartsWith(Prefix(false), StringComparison.Ordinal)) || (role == AccessibleRole.PageTab && WoWTabs.Contains(name))) return true;
                        if (role == AccessibleRole.PushButton && (name.Equals("Log In", StringComparison.OrdinalIgnoreCase) || name.Equals("Sign In", StringComparison.OrdinalIgnoreCase))) return false;
                    }
                }
                catch (Exception ex) when (ex is COMException or ArgumentException) { }
            if (next.Count > 2000) break;
            level = next;
        }
        return null;
    }
    // A freshly started Battle.net exposes only empty containers until an assistive tool asks for
    // IAccessible2, as screen readers do; Chromium then builds the launcher's controls.
    private void Wake()
    {
        Node root;
        try { root = Root(); } catch (InvalidOperationException) { return; }
        var service = typeof(IAccessible).GUID; var ia2 = IAccessible2;
        var level = new List<Node> { root };
        for (int depth = 0; depth < 8 && level.Count is > 0 and < 50; depth++)
        {
            var next = new List<Node>();
            foreach (var node in level)
                try
                {
                    if (node.Accessible is IServiceProvider provider && provider.QueryService(ref service, ref ia2, out var result) >= 0 && result != 0) Marshal.Release(result);
                    next.AddRange(Children(node));
                }
                catch (Exception ex) when (ex is COMException or ArgumentException or InvalidCastException) { }
            level = next;
        }
        if (token.WaitHandle.WaitOne(1000)) token.ThrowIfCancellationRequested();
    }
    private const int Layered = 0x80000, ClickThrough = 0x20;
    private void Conceal()
    {
        SetWindowLong(handle, -20, GetWindowLong(handle, -20) | Layered | ClickThrough);
        SetLayeredWindowAttributes(handle, 0, 0, 2);
        if (Native.IsIconic(handle)) Native.ShowWindowAsync(handle, 4); // restore without activating
        if (token.WaitHandle.WaitOne(500)) token.ThrowIfCancellationRequested();
    }
    private static void Stow(nint window)
    {
        if (!Native.IsWindow(window)) return;
        // SW_MINIMIZE when Battle.net is in front hands focus back to the previous window; otherwise minimize without activating anything.
        Native.ShowWindowAsync(window, Native.GetForegroundWindow() == window ? 6 : 7);
        Thread.Sleep(400);
        Opaque(window);
    }
    // Battle.net's window is not layered on its own, so removing both styles restores it exactly,
    // including after an earlier run that was interrupted.
    private static void Opaque(nint window)
    {
        SetLayeredWindowAttributes(window, 0, 255, 2);
        SetWindowLong(window, -20, GetWindowLong(window, -20) & ~(Layered | ClickThrough));
    }
    private static void Reveal(nint window) { Opaque(window); Native.ShowWindowAsync(window, 9); }
    private void Check()
    {
        token.ThrowIfCancellationRequested();
        if (!Native.IsWindow(handle)) throw new InvalidOperationException("Battle.net closed. Open it and try again.");
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
                try { expanded = parent.Accessible.get_accChild(childId); } catch (Exception ex) when (ex is COMException or ArgumentException) { }
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
                catch (Exception ex) when (ex is COMException or ArgumentException) { }
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
    private static string Prefix(bool account) => account ? "Game Account, Currently Selected " : "Game Version ";
    private Node Selector(bool account)
    {
        ref Node? cached = ref (account ? ref accountSelector : ref versionSelector);
        try { if (cached is not null && cached.Name.StartsWith(Prefix(account), StringComparison.Ordinal)) return cached; }
        catch (Exception ex) when (ex is COMException or ArgumentException) { cached = null; }
        return cached = Unique(AccessibleRole.ButtonMenu, name => name.StartsWith(Prefix(account), StringComparison.Ordinal),
            account ? "Battle.net's WoW account selector is unavailable. Sign in and select the installed WoW game first." : "Open Battle.net's World of Warcraft page. This integration currently uses the English launcher controls.");
    }
    private string Selected(bool account) => Selector(account).Name[Prefix(account).Length..].Trim();
    // sending runs at the last moment before the control is pressed, after every check that can still refuse.
    private void Act(Node control, Action? sending = null)
    {
        Check();
        if ((control.State & 1) != 0) throw new InvalidOperationException("Battle.net is still busy. Wait for it to finish, then retry.");
        if (string.IsNullOrEmpty(control.Accessible.get_accDefaultAction(control.Child))) throw new InvalidOperationException("Battle.net did not expose an action for the requested control.");
        sending?.Invoke();
        control.Accessible.accDoDefaultAction(control.Child);
    }
    private T Wait<T>(Func<T?> read, string error, int seconds = 8) where T : class
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(seconds))
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
        Wait(() => { try { return Selected(account) == name ? "selected" : null; } catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException) { return null; } }, "Battle.net did not confirm the requested selection.");
    }
    public BattleNetChoices ReadChoices(string? requestedVersion = null)
    {
        if (!string.IsNullOrEmpty(requestedVersion)) Select(false, requestedVersion);
        string version = Selected(false), account = Selected(true);
        return new(BattleNetInstallation.TabForVersion(version), version, account, Choices(false), Choices(true));
    }
    private void Navigate(string tabId)
    {
        if (!TabNames.TryGetValue(tabId, out var name)) throw new InvalidOperationException("Choose a World of Warcraft game version.");
        // Right after Battle.net wakes up its tabs are listed before they can be clicked.
        var tab = Wait(() =>
        {
            try { var tabs = Find(AccessibleRole.PageTab, n => n == name); return tabs.Count == 1 && !string.IsNullOrEmpty(tabs[0].Accessible.get_accDefaultAction(tabs[0].Child)) ? tabs[0] : null; }
            catch (Exception ex) when (ex is COMException or ArgumentException) { return null; }
        }, "The saved WoW page is not visible in Battle.net. Add it to Favorites or open it manually.");
        Act(tab);
        Wait(() => { try { return Selector(false); } catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException) { return null; } }, "Battle.net did not show its game controls.");
    }
    public string Prepare(BattleNetLaunch profile)
    {
        try { Selected(false); } catch (InvalidOperationException) { Navigate(profile.GameTabId); }
        Select(false, profile.GameVersion);
        string account = SelectAccount(profile);
        Verify(profile, account); return account;
    }
    // Reads the account list and picks the account in one opening of the menu.
    private string SelectAccount(BattleNetLaunch profile)
    {
        var menu = OpenMenu(true);
        string account; Node choice;
        try
        {
            var items = Find(AccessibleRole.MenuItem, n => n.Length > 0, menu).Select(n => (Node: n, n.Name)).ToList();
            account = BattleNetAccounts.Resolve(profile, items.Select(i => i.Name));
            if (Selected(true) == account) { CloseMenu(true); return account; }
            var match = items.Where(i => i.Name == account).Select(i => i.Node).ToList();
            if (match.Count != 1) throw new InvalidOperationException($"Battle.net no longer lists '{account}'. Refresh the launch settings.");
            choice = match[0];
        }
        catch { try { CloseMenu(true); } catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException) { } throw; }
        Act(choice);
        Wait(() => { try { return Selected(true) == account ? "selected" : null; } catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException) { return null; } }, "Battle.net did not confirm the requested selection.");
        return account;
    }
    private void Verify(BattleNetLaunch profile, string account)
    {
        Check();
        if (Selected(false) != profile.GameVersion || Selected(true) != account) throw new InvalidOperationException("Battle.net's version/account changed during launch. No Play action was sent.");
    }
    public void Play(BattleNetLaunch profile, string account, Action sending)
    {
        Verify(profile, account);
        // Just after starting another client Battle.net shows Play as busy for a moment.
        var play = Wait(() =>
        {
            try { var found = Find(AccessibleRole.PushButton, name => name.StartsWith("Play: " + profile.GameVersion + ", Version:", StringComparison.Ordinal)); return found.Count == 1 && (found[0].State & 1) == 0 ? found[0] : null; }
            catch (Exception ex) when (ex is COMException or ArgumentException) { return null; }
        }, "Battle.net is not ready to Play this game. Finish installation/update or any launcher prompts, then retry.", 20);
        Verify(profile, account); Act(play, sending);
    }
}

