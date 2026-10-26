using System.Text.Json;

namespace HoverDeck;

public enum ClientLaunchMethod { Direct, BattleNet }
public enum ClientWindowMode { Windowed, FullscreenWindowed }

public sealed class Slot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Client";
    public string Executable { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ClientLogin? Login { get; set; }
    public ClientLaunchMethod LaunchMethod { get; set; }
    public ClientWindowMode WindowMode { get; set; }
    // Where clients overlap, a higher layer stays in front. 1 is the back.
    public int Layer { get; set; } = 1;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BattleNetLaunch? BattleNet { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 960;
    public int Height { get; set; } = 540;
    [System.Text.Json.Serialization.JsonIgnore] public Rectangle Bounds => new(X, Y, Width, Height);
    public void SetBounds(Rectangle r) { X = r.X; Y = r.Y; Width = r.Width; Height = r.Height; }
    public string PathFor(Layout layout) => LaunchMethod == ClientLaunchMethod.BattleNet ? BattleNet?.Executable ?? "" : string.IsNullOrWhiteSpace(Executable) ? layout.Executable : Executable;
    public override string ToString() => Name;
}
public sealed class Layout
{
    public string Name { get; set; } = "My WoW layout";
    public string Executable { get; set; } = "";
    public string Arguments { get; set; } = "";
    public List<Slot> Slots { get; set; } = new();
    public override string ToString() => Name;
}
public sealed class Settings
{
    public int DelayMs { get; set; } = 50;
    // True: minimizing hides HoverDeck to the notification area. False: a normal taskbar minimize.
    public bool MinimizeToTray { get; set; } = true;
    public uint HotkeyModifiers { get; set; } = 3;
    public int HotkeyKey { get; set; } = (int)Keys.H;
    public string ActiveLayout { get; set; } = "My WoW layout";
    public List<Layout> Layouts { get; set; } = new();
    public BattleNetCatalog BattleNetCatalog { get; set; } = new();
    public WoWLibrary WoWLibrary { get; set; } = new();
    public List<BattleNetConnection> BattleNetConnections { get; set; } = new();
}
public sealed record MonitorInfo(string Name, bool Primary, Rectangle Bounds, Rectangle WorkArea);
public sealed record GameWindow(nint Handle, int ProcessId, long Started, string Executable, string Title, Rectangle Bounds, bool Minimized)
{
    public string Identity => $"{ProcessId}:{Started}";
}
public static class Storage
{
    // Installed copies keep settings in the user profile because each update replaces the
    // app folder. The portable ZIP keeps Data beside HoverDeck.exe, as it always has.
    public static readonly string DirectoryPath = InstalledBySetup ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoverDeck", "Data") : System.IO.Path.Combine(AppContext.BaseDirectory, "Data");
    public static bool InstalledBySetup
    {
        get
        {
            try { var locator = Velopack.Locators.VelopackLocator.CreateDefaultForPlatform(null!, null!); return locator.CurrentlyInstalledVersion is not null && !locator.IsPortable; }
            catch { return false; }
        }
    }
    public static readonly string SettingsPath = System.IO.Path.Combine(DirectoryPath, "settings.json");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    // The file is flushed to disk before it replaces the old one, so a power cut cannot leave it empty.
    // With backup, the previous version is kept as .bak for Load to fall back on.
    public static void Write<T>(string path, T value, bool backup = false)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(file, value, Json); file.Flush(true); }
        if (backup && File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
        else File.Move(temp, path, true);
    }
    public static Settings Load()
    {
        // A damaged file, or one missing because a save was interrupted, falls back to the previous save.
        string backup = SettingsPath + ".bak";
        if (!File.Exists(SettingsPath)) return File.Exists(backup) ? Normalize(Read(backup)) : new();
        Settings s;
        try { s = Read(SettingsPath); }
        catch (JsonException) when (File.Exists(backup)) { s = Read(backup); }
        return Normalize(s);
    }
    private static Settings Normalize(Settings s)
    {
        s.DelayMs = Math.Clamp(s.DelayMs, 0, 5000);
        s.Layouts ??= new();
        s.BattleNetCatalog ??= new();
        s.WoWLibrary ??= new(); s.BattleNetConnections ??= new();
        foreach (var l in s.Layouts)
        {
            l.Slots ??= new();
            foreach (var slot in l.Slots) { slot.Width = Math.Clamp(slot.Width, 160, 32000); slot.Height = Math.Clamp(slot.Height, 120, 32000); slot.Layer = Math.Clamp(slot.Layer, 1, 9); if (!Enum.IsDefined(slot.WindowMode)) slot.WindowMode = ClientWindowMode.Windowed; }
        }
        return s;
    }
    private static Settings Read(string path) => JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new();
}
public static class Matching
{
    public static bool SamePath(string a, string b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    // Live bindings keep a slot attached to its process even after a user moves its window.
    // For unbound clients, nearest saved geometry provides a deterministic initial assignment.
    public static Dictionary<string, GameWindow> Assign(Layout layout, IReadOnlyList<GameWindow> windows, IReadOnlyDictionary<string, string> bindings)
    {
        var result = new Dictionary<string, GameWindow>();
        var available = windows.DistinctBy(w => w.ProcessId).ToList();
        foreach (var slot in layout.Slots)
        {
            string? identity = slot.LaunchMethod == ClientLaunchMethod.BattleNet ? slot.BattleNet?.BoundIdentity : bindings.GetValueOrDefault(slot.Id);
            if (string.IsNullOrEmpty(identity)) continue;
            var match = available.FirstOrDefault(w => w.Identity == identity && SamePath(w.Executable, slot.PathFor(layout)));
            if (match is not null) { result[slot.Id] = match; available.Remove(match); }
        }
        var edges = (from slot in layout.Slots where !result.ContainsKey(slot.Id) && slot.LaunchMethod == ClientLaunchMethod.Direct
                     from window in available where SamePath(window.Executable, slot.PathFor(layout))
                     orderby Distance(slot.Bounds, window.Bounds), slot.Id, window.ProcessId
                     select (slot, window)).ToList();
        foreach (var edge in edges)
            if (!result.ContainsKey(edge.slot.Id) && available.Contains(edge.window))
            { result[edge.slot.Id] = edge.window; available.Remove(edge.window); }
        return result;
    }
    private static double Distance(Rectangle a, Rectangle b) => Math.Pow(a.X - (double)b.X, 2) + Math.Pow(a.Y - (double)b.Y, 2) + .25 * (Math.Pow(a.Width - (double)b.Width, 2) + Math.Pow(a.Height - (double)b.Height, 2));
    public static bool OnScreen(Rectangle r, IReadOnlyList<MonitorInfo> monitors) => monitors.Any(m => { var hit = Rectangle.Intersect(m.Bounds, r); return hit.Width >= 80 && hit.Height >= 40; });
}
