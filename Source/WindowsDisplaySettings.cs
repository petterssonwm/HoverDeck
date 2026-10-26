using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HoverDeck;

public sealed record DisplayPosition(string Name, int X, int Y, bool Primary);
public sealed class DisplayBackup
{
    public List<DisplayPosition> Current { get; set; } = new();
    public List<DisplayPosition> Registered { get; set; } = new();
}
internal interface IDisplaySettingsBackend
{
    DisplayBackup Capture();
    void Test(IReadOnlyList<MonitorInfo> plan);
    void Apply(IReadOnlyList<MonitorInfo> plan);
    void Restore(DisplayBackup backup);
}

internal sealed class WindowsDisplaySettings : IDisplaySettingsBackend
{
    // DEVMODEW's display union; the remaining fields retain the documented layout.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int X, Y;
        public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel, Width, Height, DisplayFlags, Frequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumSettings(string name, int mode, ref DevMode settings, uint flags);
    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)]
    private static extern int ChangeSettings(string name, ref DevMode settings, nint hwnd, uint flags, nint param);
    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)]
    private static extern int CommitSettings(string? name, nint settings, nint hwnd, uint flags, nint param);
    private static DevMode Read(string name, int source)
    {
        var mode = new DevMode { Size = (ushort)Marshal.SizeOf<DevMode>(), DeviceName = "", FormName = "" };
        if (!EnumSettings(name, source, ref mode, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not read display settings for {name}.");
        return mode;
    }
    private static void Check(int result, string operation)
    {
        if (result == 0) return;
        string reason = result switch { 1 => "Windows requires a restart for this change", -1 => "the display driver rejected the change", -2 => "the arrangement is not supported", -3 => "Windows could not save the settings", -4 => "Windows rejected the flags", -5 => "Windows rejected the parameters", -6 => "the display driver does not support this arrangement", _ => $"Windows returned {result}" };
        throw new InvalidOperationException($"{operation}: {reason}.");
    }
    public DisplayBackup Capture()
    {
        var live = Native.Monitors(); var backup = new DisplayBackup();
        foreach (var monitor in live)
        {
            var current = Read(monitor.Name, -1); var registered = Read(monitor.Name, -2);
            backup.Current.Add(new(monitor.Name, current.X, current.Y, monitor.Primary));
            backup.Registered.Add(new(monitor.Name, registered.X, registered.Y, false));
        }
        var registryOrigin = backup.Registered.Where(p => p.X == 0 && p.Y == 0).ToList();
        string registryPrimary = registryOrigin.Count == 1 ? registryOrigin[0].Name : live.Single(m => m.Primary).Name;
        backup.Registered = backup.Registered.Select(p => p with { Primary = p.Name == registryPrimary }).ToList();
        return backup;
    }
    public void Test(IReadOnlyList<MonitorInfo> plan)
    {
        if (DisplayArrangement.Validate(plan) is { } invalid) throw new InvalidOperationException(invalid);
        foreach (var monitor in plan)
        {
            var mode = Read(monitor.Name, -1);
            if (mode.Width != monitor.Bounds.Width || mode.Height != monitor.Bounds.Height) throw new InvalidOperationException("The display resolution changed. Refresh the arrangement before applying it.");
            // CDS_TEST checks one device against the current desktop. In particular,
            // it rejects moving the current primary away from (0,0) before the new
            // primary has been staged. Validate the full geometry above, test each
            // unchanged graphics mode here, then stage all positions before commit.
            mode.Fields &= ~0x20u; mode.DriverExtra = 0;
            Check(ChangeSettings(monitor.Name, ref mode, 0, 0x2, 0), $"Testing the graphics mode for {monitor.Name}");
        }
    }
    private static void Stage(IEnumerable<DisplayPosition> positions)
    {
        foreach (var position in positions.OrderByDescending(p => p.Primary))
        {
            var mode = Read(position.Name, -1);
            mode.X = position.X; mode.Y = position.Y; mode.Fields |= 0x20; mode.DriverExtra = 0;
            Check(ChangeSettings(position.Name, ref mode, 0, 0x10000001 | (position.Primary ? 0x10u : 0), 0), "Staging the display positions");
        }
    }
    public void Apply(IReadOnlyList<MonitorInfo> plan)
    {
        Stage(plan.Select(m => new DisplayPosition(m.Name, m.Bounds.X, m.Bounds.Y, m.Primary)));
        Check(CommitSettings(null, 0, 0, 0, 0), "Applying the display arrangement");
    }
    public void Restore(DisplayBackup backup)
    {
        var connected = Native.Monitors();
        var names = connected.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = backup.Current.Where(p => names.Contains(p.Name)).ToList();
        if (current.Count == 0) return;
        if (!current.Any(p => p.Primary))
        {
            var anchor = current.FirstOrDefault(p => connected.Any(m => m.Name == p.Name && m.Primary)) ?? current[0];
            current = current.Select(p => p with { X = p.X - anchor.X, Y = p.Y - anchor.Y, Primary = p.Name == anchor.Name }).ToList();
        }
        Stage(current);
        Check(CommitSettings(null, 0, 0, 0, 0), "Restoring the original display arrangement");
        // Preserve the previously registered positions too, even if the user had
        // a temporary desktop configuration before opening this confirmation.
        var registered = backup.Registered.Where(p => names.Contains(p.Name)).ToList();
        if (registered.Count == backup.Registered.Count && registered.Any(p => p.Primary)) Stage(registered);
    }
}
