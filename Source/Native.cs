using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HoverDeck;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public Rectangle Rectangle => Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    [StructLayout(LayoutKind.Sequential)] public struct WINDOWPLACEMENT { public int Length, Flags, ShowCmd; public POINT Min, Max; public RECT Normal; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEHOOK { public POINT Point; public uint MouseData, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int Size;
        public RECT Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    public delegate bool EnumProc(nint hwnd, nint param);
    private delegate bool MonitorEnumProc(nint monitor, nint dc, nint rect, nint param);
    public delegate nint HookProc(int code, nint wp, nint lp);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnumProc callback, nint param);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, nint param);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint h, out RECT rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint h, uint attribute, out RECT rect, int size);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(nint h, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(nint h, StringBuilder text, int length);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint h, uint cmd);
    [DllImport("user32.dll")] public static extern nint GetTopWindow(nint h);
    public delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] public static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint h, uint flags);
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint h, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint h, StringBuilder text, int length);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(nint h, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(nint h, int command);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint h, int message, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint h, int id);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)] public static extern bool GetSetting(uint action, uint param, out int value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)] public static extern bool SetSetting(uint action, uint param, nint value, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint threadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook, int code, nint wp, nint lp);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(nint h, uint flags, StringBuilder text, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint h);
    public static string ProcessPath(uint pid)
    {
        var handle = OpenProcess(0x1000, false, pid);
        if (handle == 0) return "";
        try { var b = new StringBuilder(32768); int size = b.Capacity; return QueryFullProcessImageName(handle, 0, b, ref size) ? b.ToString() : ""; }
        finally { CloseHandle(handle); }
    }
    public static List<MonitorInfo> Monitors()
    {
        // Read a fresh desktop snapshot, including each monitor's independent
        // origin and work area. No Screen cache or assumed taskbar edge/height.
        var result = new List<MonitorInfo>();
        int readError = 0;
        if (!EnumDisplayMonitors(0, 0, (handle, _, _, _) =>
        {
            var info = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>(), Device = "" };
            if (!GetMonitorInfo(handle, ref info)) { readError = Marshal.GetLastWin32Error(); return false; }
            result.Add(new(info.Device, (info.Flags & 1) != 0, info.Monitor.Rectangle, info.Work.Rectangle));
            return true;
        }, 0) || readError != 0)
            throw new Win32Exception(readError != 0 ? readError : Marshal.GetLastWin32Error(), "Could not read the current Windows display layout.");
        if (result.Count == 0) throw new InvalidOperationException("Windows did not return any connected displays.");
        return result.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ThenBy(m => m.Name).ToList();
    }
    public static List<GameWindow> Windows(IEnumerable<string>? paths = null, bool discover = false)
    {
        var allowed = (paths ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new List<GameWindow>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, 4) != 0) return true;
            var title = new StringBuilder(512); GetWindowText(h, title, title.Capacity);
            GetWindowThreadProcessId(h, out var pid);
            var path = ProcessPath(pid);
            bool looksLikeWow = title.ToString().Contains("World of Warcraft", StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetFileNameWithoutExtension(path).Equals("Wow", StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetFileNameWithoutExtension(path).Equals("WowB", StringComparison.OrdinalIgnoreCase);
            if (!allowed.Contains(path) && !(discover && looksLikeWow)) return true;
            if (path.Length == 0 || !GetWindowRect(h, out var r)) return true;
            var bounds = r.Rectangle;
            bool minimized = IsIconic(h);
            if (!minimized && TryVisibleBounds(h, bounds, out var visible)) bounds = visible;
            if (minimized)
            {
                var p = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
                if (GetWindowPlacement(h, ref p)) { bounds = p.Normal.Rectangle; var s = Screen.FromRectangle(bounds); bounds.Offset(s.WorkingArea.Left - s.Bounds.Left, s.WorkingArea.Top - s.Bounds.Top); }
            }
            if (bounds.Width < 100 || bounds.Height < 100) return true;
            long start = 0;
            try { using var process = Process.GetProcessById((int)pid); start = process.StartTime.ToUniversalTime().Ticks; } catch { }
            found.Add(new(h, (int)pid, start, path, title.ToString(), bounds, minimized));
            return true;
        }, 0);
        return found.GroupBy(w => w.ProcessId).Select(g => g.OrderByDescending(w => (long)w.Bounds.Width * w.Bounds.Height).First()).ToList();
    }
    public static void Place(GameWindow window, Rectangle bounds)
    {
        if (!IsWindow(window.Handle)) throw new InvalidOperationException("The client closed before it could be placed.");
        GetWindowThreadProcessId(window.Handle, out var pid);
        if (pid != window.ProcessId) throw new InvalidOperationException("The client window changed. Refresh and try again.");
        // The layout describes visible window edges. SetWindowPos includes
        // invisible resize margins, so expand its rectangle using this window's
        // measured DWM frame. Launcher remeasures on each placement pass after
        // restoring or crossing to a monitor with a different DPI.
        bool wasMinimized = IsIconic(window.Handle);
        ShowWindowAsync(window.Handle, 4);
        var outerTarget = bounds;
        if (!wasMinimized && GetWindowRect(window.Handle, out var outer) && TryVisibleBounds(window.Handle, outer.Rectangle, out var visible))
            outerTarget = WindowGeometry.OuterForVisible(bounds, outer.Rectangle, visible);
        if (!SetWindowPos(window.Handle, 0, outerTarget.X, outerTarget.Y, outerTarget.Width, outerTarget.Height, 0x0010 | 0x0004 | 0x0200 | 0x4000))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not place this client. Check whether WoW is running as administrator.");
    }
    public static bool IsSameWindow(long handle, int processId, long started)
    {
        if (started == 0 || !IsWindow((nint)handle)) return false;
        GetWindowThreadProcessId((nint)handle, out var pid);
        if (pid != processId) return false;
        try { using var process = Process.GetProcessById(processId); return process.StartTime.ToUniversalTime().Ticks == started && !process.HasExited; }
        catch { return false; }
    }
    public static bool IsTopmost(nint handle) => (GetWindowLong(handle, -20) & 0x00000008) != 0;
    public static void SetTopmost(nint handle, bool value)
    {
        // Keep geometry and keyboard focus; only change this client's stacking band.
        if (!SetWindowPos(handle, value ? -1 : -2, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200 | 0x4000))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not update this client's taskbar coverage. Check whether WoW is running as administrator.");
    }
    public static bool TaskbarAbove(nint handle)
    {
        if (!GetWindowRect(handle, out var game)) return false;
        bool above = false;
        EnumWindows((h, _) =>
        {
            if (h == handle) return false;
            if (!IsWindowVisible(h)) return true;
            var name = new StringBuilder(128); GetClassName(h, name, name.Capacity);
            if ((name.ToString() == "Shell_TrayWnd" || name.ToString() == "Shell_SecondaryTrayWnd") && GetWindowRect(h, out var bar) && bar.Rectangle.IntersectsWith(game.Rectangle))
            { above = true; return false; }
            return true;
        }, 0);
        return above;
    }
    private static bool TryVisibleBounds(nint handle, Rectangle outer, out Rectangle visible)
    {
        visible = outer;
        if (DwmGetWindowAttribute(handle, 9, out var frame, Marshal.SizeOf<RECT>()) != 0 || !WindowGeometry.IsUsableFrame(outer, frame.Rectangle)) return false;
        visible = frame.Rectangle; return true;
    }
    public static int Setting(uint action) { if (!GetSetting(action, 0, out var result, 0)) throw new Win32Exception(Marshal.GetLastWin32Error()); return result; }
    public static void Setting(uint action, int value) { if (!SetSetting(action, 0, (nint)value, 0)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
}
