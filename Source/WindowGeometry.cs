namespace HoverDeck;

internal static class WindowGeometry
{
    // Use the measured invisible margins from this HWND, not assumed Windows
    // border widths: different styles and DPI settings have different margins.
    public static Rectangle OuterForVisible(Rectangle target, Rectangle outer, Rectangle visible) => Rectangle.FromLTRB(
        target.Left - (visible.Left - outer.Left),
        target.Top - (visible.Top - outer.Top),
        target.Right + (outer.Right - visible.Right),
        target.Bottom + (outer.Bottom - visible.Bottom));

    public static bool IsUsableFrame(Rectangle outer, Rectangle visible) =>
        visible.Width > 0 && visible.Height > 0 && outer.Contains(visible);
}
