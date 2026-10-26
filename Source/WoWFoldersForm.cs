namespace HoverDeck;

internal sealed class WoWFoldersForm : Form
{
    private readonly ListBox folders = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly ListBox versions = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly Label status = new() { Dock = DockStyle.Fill };
    private readonly Button save = new() { Text = "Save folders", Width = 130 };
    private List<WoWInstallation> discovered = new();
    private bool scanning;
    public WoWFoldersForm(WoWLibrary library)
    {
        Text = "World of Warcraft folders"; ClientSize = new(930, 585); StartPosition = FormStartPosition.CenterParent; Font = new("Segoe UI", 10);
        FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = MaximizeBox = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 7 };
        foreach (var height in new[] { 58, 125, 40, 26, 190, 65, 38 }) root.RowStyles.Add(new(SizeType.Absolute, height)); Controls.Add(root);
        root.Controls.Add(new Label { Text = "Choose the World of Warcraft folder. All installed versions inside it are detected automatically.\nYou can add more World of Warcraft folders on other drives. Selecting a version subfolder also includes its siblings.", Dock = DockStyle.Fill }, 0, 0);
        folders.Items.AddRange(library.Folders.Select(WoWInstallationScanner.NormalizeRoot).Distinct(StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray()); root.Controls.Add(folders, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill }; root.Controls.Add(actions, 0, 2);
        var add = new Button { Text = "Add folder…", Width = 125 }; var remove = new Button { Text = "Remove folder", Width = 130 }; var scan = new Button { Text = "Rescan", Width = 100 }; var detect = new Button { Text = "Add detected folders", Width = 180 }; actions.Controls.AddRange(new Control[] { add, remove, scan, detect });
        root.Controls.Add(new Label { Text = "All versions found in these folders · build · World of Warcraft folder", Dock = DockStyle.Fill }, 0, 3); root.Controls.Add(versions, 0, 4); root.Controls.Add(status, 0, 5);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill }; var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel }; bottom.Controls.AddRange(new Control[] { save, cancel }); root.Controls.Add(bottom, 0, 6); CancelButton = cancel;
        void Add(string path) { path = WoWInstallationScanner.NormalizeRoot(path); if (!folders.Items.Cast<string>().Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) folders.Items.Add(path); }
        async Task Scan()
        {
            if (scanning) return; scanning = true; save.Enabled = actions.Enabled = false; status.Text = "Reading installation metadata…";
            try
            {
                var paths = folders.Items.Cast<string>().ToArray(); var result = await Task.Run(() => WoWInstallationScanner.Scan(paths));
                if (IsDisposed) return; discovered = result.Installations; versions.Items.Clear(); versions.Items.AddRange(discovered.Cast<object>().ToArray());
                status.Text = $"{discovered.Count} installed version(s). " + string.Join("\n", result.Messages.Take(2));
            }
            catch (Exception ex) { if (!IsDisposed) status.Text = "Could not read the folders: " + ex.Message; }
            finally { scanning = false; if (!IsDisposed) save.Enabled = actions.Enabled = true; }
        }
        add.Click += async (_, _) => { using var picker = new FolderBrowserDialog { Description = "Choose the World of Warcraft folder — all versions inside will be detected", UseDescriptionForTitle = true, ShowNewFolderButton = false, SelectedPath = folders.Items.Cast<string>().FirstOrDefault() ?? "" }; if (picker.ShowDialog(this) == DialogResult.OK) { Add(picker.SelectedPath); await Scan(); } };
        remove.Click += async (_, _) => { if (folders.SelectedIndex >= 0) { folders.Items.RemoveAt(folders.SelectedIndex); await Scan(); } };
        scan.Click += async (_, _) => await Scan();
        detect.Click += async (_, _) => { foreach (string executable in BattleNetInstallation.GameExecutables()) { string dir = Path.GetDirectoryName(executable)!; Add(File.Exists(Path.Combine(dir, ".build.info")) ? dir : Directory.GetParent(dir)?.FullName ?? dir); } await Scan(); };
        save.Click += (_, _) => { library.Folders = folders.Items.Cast<string>().ToList(); library.Installations = discovered; library.RootFoldersInitialized = true; DialogResult = DialogResult.OK; Close(); };
        Shown += async (_, _) => await Scan();
        foreach (var button in actions.Controls.Cast<Button>().Concat(bottom.Controls.Cast<Button>())) button.Height = 32;
        AppTheme.Apply(this);
    }
}
