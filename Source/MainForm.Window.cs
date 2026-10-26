using System.Runtime.InteropServices;

namespace HoverDeck;

// Borderless main window. The top bar acts as the title bar, the edges still
// resize, and the window keeps WS_THICKFRAME so Windows snap and maximize work.
public sealed partial class MainForm
{
    private const int WM_GETMINMAXINFO = 0x0024, WM_NCCALCSIZE = 0x0083, WM_NCHITTEST = 0x0084, WM_NCPAINT = 0x0085, WM_NCACTIVATE = 0x0086, HTTRANSPARENT = -1, HTCLIENT = 1, HTCAPTION = 2;
    private const int ResizeBorder = 6;
    private readonly List<HitTestPassThrough> hitTestHooks = new();

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.Style |= 0x00040000 | 0x00020000 | 0x00010000 | 0x00080000; // WS_THICKFRAME, WS_MINIMIZEBOX, WS_MAXIMIZEBOX, WS_SYSMENU
            p.Style &= ~0x00C00000; // no WS_CAPTION: Windows would otherwise paint a classic caption over the top bar
            return p;
        }
    }
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint window);
    [StructLayout(LayoutKind.Sequential)] private struct MINMAXINFO { public Native.POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    private bool HandleFrameMessage(ref Message m)
    {
        if (m.Msg == WM_GETMINMAXINFO)
        {
            // Maximize into the work area so the taskbar stays visible and usable.
            base.WndProc(ref m);
            var info = Marshal.PtrToStructure<MINMAXINFO>(m.LParam); var screen = Screen.FromHandle(Handle);
            info.MaxPosition = new(screen.WorkingArea.X - screen.Bounds.X, screen.WorkingArea.Y - screen.Bounds.Y);
            info.MaxSize = new(screen.WorkingArea.Width, screen.WorkingArea.Height);
            Marshal.StructureToPtr(info, m.LParam, false); return true;
        }
        // There is no non-client area left to draw. Without this, Windows repaints its
        // classic caption and frame over the window whenever it loses focus.
        if (m.Msg == WM_NCPAINT) { m.Result = IntPtr.Zero; return true; }
        if (m.Msg == WM_NCACTIVATE) { m.LParam = new IntPtr(-1); base.WndProc(ref m); m.Result = new IntPtr(1); return true; }
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            // The whole window is client area. A maximized window is also clamped to the
            // monitor's work area in case Windows still adds its invisible frame overhang.
            if (IsZoomed(Handle))
            {
                var proposed = Marshal.PtrToStructure<Native.RECT>(m.LParam).Rectangle;
                var area = Screen.FromRectangle(proposed).WorkingArea;
                Marshal.StructureToPtr(new Native.RECT { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Bottom }, m.LParam, false);
            }
            m.Result = IntPtr.Zero; return true;
        }
        if (m.Msg == WM_NCHITTEST)
        {
            var point = PointToClient(ScreenPoint(m.LParam));
            int edge = ResizeEdge(point);
            m.Result = edge != HTCLIENT ? edge : titleBar.Bounds.Contains(point) ? HTCAPTION : HTCLIENT;
            return true;
        }
        return false;
    }
    private static Point ScreenPoint(IntPtr lParam) { long value = lParam.ToInt64(); return new((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF)); }
    private int ResizeEdge(Point p)
    {
        if (WindowState != FormWindowState.Normal) return HTCLIENT;
        bool left = p.X < ResizeBorder, right = p.X >= ClientSize.Width - ResizeBorder, top = p.Y < ResizeBorder, bottom = p.Y >= ClientSize.Height - ResizeBorder;
        if (top) return left ? 13 : right ? 14 : 12;
        if (bottom) return left ? 16 : right ? 17 : 15;
        return left ? 10 : right ? 11 : HTCLIENT;
    }
    // Child controls hand edge and title-bar hits back to the form.
    private bool PassesHitTest(Control control, IntPtr lParam)
    {
        var point = PointToClient(ScreenPoint(lParam));
        if (ResizeEdge(point) != HTCLIENT) return true;
        if (control == titleBar || control == logo || control == titleRight || control == openCount) return true;
        return control == mainMenu && mainMenu.GetItemAt(mainMenu.PointToClient(ScreenPoint(lParam))) is null;
    }
    private void HookHitTesting(Control parent)
    {
        foreach (Control child in parent.Controls) { hitTestHooks.Add(new HitTestPassThrough(this, child)); HookHitTesting(child); }
    }
    private sealed class HitTestPassThrough : NativeWindow
    {
        private readonly MainForm form; private readonly Control control;
        public HitTestPassThrough(MainForm form, Control control)
        {
            this.form = form; this.control = control;
            control.HandleCreated += (_, _) => AssignHandle(control.Handle);
            control.HandleDestroyed += (_, _) => ReleaseHandle();
            if (control.IsHandleCreated) AssignHandle(control.Handle);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && !form.IsDisposed && form.PassesHitTest(control, m.LParam)) { m.Result = HTTRANSPARENT; return; }
            base.WndProc(ref m);
        }
    }
}
