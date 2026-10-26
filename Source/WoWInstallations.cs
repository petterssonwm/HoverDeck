namespace HoverDeck;

public sealed class WoWLibrary
{
    public List<string> Folders { get; set; } = new();
    public List<WoWInstallation> Installations { get; set; } = new();
    public bool RootFoldersInitialized { get; set; }
}

public sealed class WoWInstallation
{
    public string Product { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string Build { get; set; } = "";
    public string Executable { get; set; } = "";
    public string RootFolder { get; set; } = "";
    public string GameTabId => GameVersion.Contains("Forever", StringComparison.OrdinalIgnoreCase) || Product.StartsWith("wowf") ? "game-nav-btn-WoWF"
        : Product.Contains("classic") || Product.Contains("anniversary") || GameVersion.Contains("Classic", StringComparison.OrdinalIgnoreCase) || GameVersion.Contains("Anniversary", StringComparison.OrdinalIgnoreCase) ? "game-nav-btn-WoWC" : "game-nav-btn-WoW";
    public override string ToString() => $"{GameVersion} · {Build}" + (RootFolder.Length > 0 ? " · " + RootFolder : "");
}

internal static class WoWInstallationScanner
{
    private static readonly Dictionary<string, (string Folder, string Name)> products = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wow"] = ("_retail_", "World of Warcraft"),
        ["wow_classic"] = ("_classic_", "Mists of Pandaria Classic"),
        ["wow_classic_era"] = ("_classic_era_", "World of Warcraft Classic"),
        ["wow_classic_beta"] = ("_classic_beta_", "WoW: Forever - Beta"),
        ["wow_anniversary"] = ("_anniversary_", "Burning Crusade Anniversary"),
        ["wow_classic_era_ptr"] = ("_classic_era_ptr_", "PTR (Classic Era)"),
        ["wow_classic_ptr"] = ("_classic_ptr_", "PTR (MoP Classic)"),
        ["wowf"] = ("_forever_", "WoW: Forever"),
        ["wowf_beta"] = ("_forever_beta_", "WoW: Forever - Beta")
    };
    internal sealed record ScanResult(List<WoWInstallation> Installations, List<string> Messages);
    public static string NormalizeRoot(string folder)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (File.Exists(Path.Combine(full, ".build.info"))) return full;
        string? parent = Directory.GetParent(full)?.FullName;
        if (parent is not null && File.Exists(Path.Combine(parent, ".build.info"))
            && (File.Exists(Path.Combine(full, ".flavor.info")) || Path.GetFileName(full).StartsWith('_'))) return parent;
        return full;
    }
    public static string? RootForExecutable(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return null;
        string? directory = Path.GetDirectoryName(Path.GetFullPath(executable));
        if (directory is null) return null;
        string root = NormalizeRoot(directory);
        return File.Exists(Path.Combine(root, ".build.info")) ? root : null;
    }
    public static List<string> LibraryRoots(WoWLibrary library, IEnumerable<string> savedExecutables)
    {
        var folders = library.Folders.Select(NormalizeRoot).ToList();
        // Adopt an older per-slot executable once. An intentionally emptied library stays empty.
        if (!library.RootFoldersInitialized && folders.Count == 0)
            folders.AddRange(savedExecutables.Select(RootForExecutable).OfType<string>());
        return folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static ScanResult Scan(IEnumerable<string> folders)
    {
        var found = new Dictionary<string, WoWInstallation>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var messages = new List<string>();
        foreach (string folder in folders)
        {
            try
            {
                string root = NormalizeRoot(folder);
                if (!Directory.Exists(root)) { messages.Add("Folder is unavailable: " + folder); continue; }
                var candidates = new List<string> { root };
                // A selected version folder is normalized to its root, so all sibling versions are scanned.
                candidates.AddRange(Directory.EnumerateDirectories(root).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0 && !Path.GetFileName(p).Equals("Data", StringComparison.OrdinalIgnoreCase)).Take(200));
                int before = found.Count;
                foreach (var candidate in candidates)
                {
                    if (!seen.Add(candidate)) continue;
                    string metadata = Path.Combine(candidate, ".build.info");
                    if (!File.Exists(metadata)) continue;
                    foreach (var build in ReadBuilds(File.ReadLines(metadata)))
                    {
                        var definition = products.GetValueOrDefault(build.Product);
                        var versionFolders = Directory.EnumerateDirectories(candidate).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0
                            && (Path.GetFileName(p).StartsWith('_') || File.Exists(Path.Combine(p, ".flavor.info")))).Take(200);
                        foreach (string versionFolder in versionFolders.Append(candidate))
                        {
                            string flavorPath = Path.Combine(versionFolder, ".flavor.info");
                            string flavor = File.Exists(flavorPath) ? File.ReadLines(flavorPath).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("wow", StringComparison.OrdinalIgnoreCase)) ?? "" : "";
                            bool matches = flavor.Length > 0 ? flavor.Equals(build.Product, StringComparison.OrdinalIgnoreCase) : definition.Folder is not null && Path.GetFileName(versionFolder).Equals(definition.Folder, StringComparison.OrdinalIgnoreCase);
                            if (!matches) continue;
                            string? executable = Directory.EnumerateFiles(versionFolder, "*.exe").Where(p => Path.GetFileName(p).StartsWith("Wow", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(p).Contains("Launcher", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(p).Contains("Error", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Length).FirstOrDefault();
                            if (executable is null) continue;
                            found[Path.GetFullPath(executable)] = new() { Product = build.Product, Build = build.Build, GameVersion = definition.Name ?? build.Product, Executable = Path.GetFullPath(executable), RootFolder = candidate };
                        }
                    }
                }
                if (before == found.Count) messages.Add("No additional installed WoW versions found in " + folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { messages.Add("Could not scan " + folder + ": " + ex.Message); }
        }
        return new(found.Values.OrderBy(i => i.GameVersion).ThenBy(i => i.Executable).ToList(), messages);
    }
    internal static List<(string Product, string Build)> ReadBuilds(IEnumerable<string> lines)
    {
        using var rows = lines.GetEnumerator(); if (!rows.MoveNext()) return new();
        var columns = rows.Current.Split('|').Select(c => c.Split('!')[0]).ToList();
        int product = columns.IndexOf("Product"), version = columns.IndexOf("Version"), active = columns.IndexOf("Active");
        var result = new List<(string, string)>(); if (product < 0 || version < 0) return result;
        while (rows.MoveNext())
        {
            var values = rows.Current.Split('|');
            if (values.Length <= Math.Max(product, Math.Max(version, active)) || (active >= 0 && values[active] != "1")) continue;
            if (values[product].StartsWith("wow", StringComparison.OrdinalIgnoreCase)) result.Add((values[product], values[version]));
        }
        return result;
    }
}
