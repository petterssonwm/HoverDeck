using System.Text.Json;

namespace HoverDeck;

internal static class ClientPlacementTests
{
    public static void Run(Action<string, bool> check)
    {
        // Regression: a 697 px tall client dropped at y=8 must snap its top to 0. The old code then also snapped its bottom to the display middle (720), leaving it at y=23.
        int[] displayX = { 0, 2560, 1280 }, displayY = { 0, 1440, 720 };
        check("Dragging snaps to the closest guide only, not a farther one checked later", LayoutCanvas.Snap(new(4, 8, 1081, 697), false, displayX, displayY, 55) == new Rectangle(0, 0, 1081, 697));
        check("Clients snap to another client's edges", LayoutCanvas.Snap(new(1500, 300, 400, 300), false, new[] { 0, 2560, 1280, 1520 }, new[] { 0, 1440, 720, 310 }, 55) == new Rectangle(1520, 310, 400, 300));
        check("Resizing snaps only the moving right and bottom edges", LayoutCanvas.Snap(new(0, 0, 1270, 700), true, displayX, displayY, 55) == new Rectangle(0, 0, 1280, 720));
        check("Nothing beyond the snap distance moves the client", LayoutCanvas.Snap(new(300, 300, 400, 300), false, displayX, displayY, 55) == new Rectangle(300, 300, 400, 300));
        var display = new MonitorInfo("OFFSET", false, new(-1440, -743, 1440, 2560), new(-1440, -743, 1440, 2512));
        var slot = new Slot { Name = "Fullscreen fixture" }; slot.SetBounds(display.WorkArea);
        ClientPlacement.ChangeMode(slot, display, ClientWindowMode.FullscreenWindowed);
        check("Fullscreen windowed fills the offset display including its actual taskbar area", slot.Bounds == new Rectangle(-1440, -743, 1440, 2560));
        ClientPlacement.ChangeMode(slot, display, ClientWindowMode.Windowed);
        check("Switching back to Windowed restores the real work area", slot.Bounds == display.WorkArea);
        foreach (var monitor in new[]
        {
            new MonitorInfo("LEFT", true, new(-1921, 80, 1921, 1080), new(-1857, 80, 1857, 1080)),
            new MonitorInfo("TOP", true, new(600, -1080, 1921, 1080), new(600, -1040, 1921, 1040)),
            new MonitorInfo("RIGHT", true, new(100, 200, 1921, 1080), new(100, 200, 1857, 1080)),
            display
        })
        {
            slot.WindowMode = ClientWindowMode.Windowed; slot.SetBounds(ClientPlacement.Fit(monitor, slot.WindowMode, ClientFit.RightHalf));
            ClientPlacement.ChangeMode(slot, monitor, ClientWindowMode.FullscreenWindowed);
            var left = ClientPlacement.Fit(monitor, slot.WindowMode, ClientFit.LeftHalf);
            check($"{monitor.Name} taskbar: half-screen modes meet exactly and include all display pixels", left.Right == slot.Bounds.Left && Rectangle.Union(left, slot.Bounds) == monitor.Bounds && !left.IntersectsWith(slot.Bounds));
            ClientPlacement.ChangeMode(slot, monitor, ClientWindowMode.Windowed);
            check($"{monitor.Name} taskbar: Windowed half-screen modes stay inside the work area", slot.Bounds.Right == monitor.WorkArea.Right && slot.Bounds.Bottom == monitor.WorkArea.Bottom && monitor.WorkArea.Contains(slot.Bounds));
        }
        var moved = DisplayArrangement.Move(display, new(-1440, -900));
        check("Placement follows moved displays without hardcoded taskbar offsets", ClientPlacement.Fit(moved, ClientWindowMode.FullscreenWindowed, ClientFit.Screen) == moved.Bounds && ClientPlacement.Fit(moved, ClientWindowMode.Windowed, ClientFit.Screen) == moved.WorkArea);
        var autoHidden = display with { WorkArea = display.Bounds };
        check("Both modes fit identically when Windows reserves no taskbar space", ClientPlacement.Area(autoHidden, ClientWindowMode.Windowed) == ClientPlacement.Area(autoHidden, ClientWindowMode.FullscreenWindowed));
        var bar = DesktopGeometry.ReservedAreas(display).Single().Bounds;
        var rightHalf = ClientPlacement.Fit(display, ClientWindowMode.FullscreenWindowed, ClientFit.RightHalf);
        var visible = DesktopGeometry.Subtract(bar, new[] { rightHalf });
        check("A fullscreen half hides only its covered half of the taskbar in the preview", visible.Count == 1 && visible[0] == new Rectangle(bar.X, bar.Y, bar.Width / 2, bar.Height));
        check("Full coverage hides the taskbar strip without modifying monitor geometry", DesktopGeometry.Subtract(bar, new[] { display.Bounds }).Count == 0 && DesktopGeometry.ReservedAreas(display).Single().Bounds == bar);
        var saved = new Layout { Slots = new() { new() { WindowMode = ClientWindowMode.FullscreenWindowed }, new() { WindowMode = ClientWindowMode.Windowed } } };
        var restored = JsonSerializer.Deserialize<Layout>(JsonSerializer.Serialize(saved, Storage.Json), Storage.Json)!;
        check("Window modes persist independently for each client", restored.Slots[0].WindowMode == ClientWindowMode.FullscreenWindowed && restored.Slots[1].WindowMode == ClientWindowMode.Windowed);
        var legacy = JsonSerializer.Deserialize<Slot>("{\"X\":-1440,\"Y\":286,\"Width\":1440,\"Height\":1483}", Storage.Json)!;
        check("Older layouts retain their exact geometry and default to Windowed", legacy.WindowMode == ClientWindowMode.Windowed && legacy.Bounds == new Rectangle(-1440, 286, 1440, 1483));
    }
}
