namespace HoverDeck;

internal enum ClientFit { Screen, LeftHalf, RightHalf }

internal static class ClientPlacement
{
    public static Rectangle Area(MonitorInfo monitor, ClientWindowMode mode) =>
        mode == ClientWindowMode.FullscreenWindowed ? monitor.Bounds : monitor.WorkArea;

    public static Rectangle Fit(MonitorInfo monitor, ClientWindowMode mode, ClientFit fit)
    {
        var area = Area(monitor, mode);
        if (fit == ClientFit.LeftHalf) area.Width /= 2;
        if (fit == ClientFit.RightHalf) { int half = area.Width / 2; area.X += half; area.Width -= half; }
        return area;
    }

    public static void ChangeMode(Slot slot, MonitorInfo monitor, ClientWindowMode mode)
    {
        // Preserve a half-screen preset when switching its available area.
        // Other placements fit the chosen screen, making the mode change visible immediately.
        var fit = slot.Bounds == Fit(monitor, slot.WindowMode, ClientFit.LeftHalf) ? ClientFit.LeftHalf
            : slot.Bounds == Fit(monitor, slot.WindowMode, ClientFit.RightHalf) ? ClientFit.RightHalf : ClientFit.Screen;
        slot.WindowMode = mode; slot.SetBounds(Fit(monitor, mode, fit));
    }
}
