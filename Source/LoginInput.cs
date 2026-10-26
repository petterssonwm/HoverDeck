using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HoverDeck;

internal static class LoginInput
{
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputData { [FieldOffset(0)] public Mouse Mouse; [FieldOffset(0)] public Keyboard Keyboard; }
    [StructLayout(LayoutKind.Sequential)] internal struct Mouse { public int X, Y; public uint MouseData, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint handle, out Native.RECT rectangle);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint handle, ref Native.POINT point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] input, int size);
    public static void CheckIdentity(GameWindow window)
    {
        Native.GetWindowThreadProcessId(window.Handle, out uint pid);
        if (!Native.IsWindow(window.Handle) || pid != window.ProcessId || !Matching.SamePath(Native.ProcessPath(pid), window.Executable)) throw new InvalidOperationException("The intended client window is no longer available.");
        using var process = Process.GetProcessById(window.ProcessId);
        if (process.StartTime.ToUniversalTime().Ticks != window.Started) throw new InvalidOperationException("The intended client process changed.");
    }
    public static Rectangle ClientBounds(GameWindow window)
    {
        CheckIdentity(window); var point = new Native.POINT();
        if (!GetClientRect(window.Handle, out var rect) || !ClientToScreen(window.Handle, ref point)) throw new InvalidOperationException("Could not read the client area.");
        return new(point.X, point.Y, rect.Right, rect.Bottom);
    }
    public static async Task Activate(GameWindow window, CancellationToken token)
    {
        CheckIdentity(window);
        if (Native.IsIconic(window.Handle)) Native.ShowWindowAsync(window.Handle, 4);
        SetForegroundWindow(window.Handle);
        await Task.Delay(350, token); Guard(window, token);
    }
    public static void Guard(GameWindow window, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); CheckIdentity(window);
        if (Native.GetForegroundWindow() != window.Handle || Native.IsIconic(window.Handle)) throw new OperationCanceledException("Login stopped because another window took focus.");
        foreach (int key in new[] { 0x1B, 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if ((Native.GetAsyncKeyState(key) & 0x8000) != 0) throw new OperationCanceledException("Login stopped because Escape or a modifier key was pressed.");
    }
    public static Bitmap Capture(GameWindow window, CancellationToken token)
    {
        Guard(window, token); var bounds = ClientBounds(window);
        if (bounds.Width < 100 || bounds.Height < 100 || bounds.Width > 16000 || bounds.Height > 16000) throw new InvalidOperationException("The client area cannot be captured at this size.");
        var image = new Bitmap(bounds.Width, bounds.Height);
        try { using var graphics = Graphics.FromImage(image); graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size); return image; }
        catch { image.Dispose(); throw; }
    }
    private static Point ScreenPoint(GameWindow window, Point point)
    {
        var area = ClientBounds(window); var target = new Point(area.X + point.X, area.Y + point.Y);
        if (!area.Contains(target) || Native.GetAncestor(Native.WindowFromPoint(new(target.X, target.Y)), 2) != window.Handle)
            throw new OperationCanceledException("The login field is covered by another window or outside the visible client.");
        return target;
    }
    private static Input Key(ushort key, bool up = false) => new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = up ? 2u : 0 } } };
    private static IEnumerable<Input> Click(Point point)
    {
        var desktop = SystemInformation.VirtualScreen;
        yield return new() { Data = new() { Mouse = new() { X = (int)Math.Round((point.X - desktop.X) * 65535d / Math.Max(1, desktop.Width - 1)), Y = (int)Math.Round((point.Y - desktop.Y) * 65535d / Math.Max(1, desktop.Height - 1)), Flags = 0x8000 | 0x4000 | 1 } } };
        yield return new() { Data = new() { Mouse = new() { Flags = 2 } } };
        yield return new() { Data = new() { Mouse = new() { Flags = 4 } } };
    }
    internal static Input[] TextEvents(string text)
    {
        var result = new List<Input> { Key(0x11), Key(0x41), Key(0x41, true), Key(0x11, true), Key(0x08), Key(0x08, true) };
        foreach (char c in text)
        {
            result.Add(new() { Type = 1, Data = new() { Keyboard = new() { Scan = c, Flags = 4 } } });
            result.Add(new() { Type = 1, Data = new() { Keyboard = new() { Scan = c, Flags = 6 } } });
        }
        return result.ToArray();
    }
    private static void Send(GameWindow window, Input[] events, CancellationToken token)
    {
        try
        {
            Guard(window, token);
            if (SendInput((uint)events.Length, events, Marshal.SizeOf<Input>()) != events.Length) throw new InvalidOperationException("Windows blocked login input. Run HoverDeck and the client at the same permission level.");
        }
        finally { Array.Clear(events); }
    }
    public static async Task Fill(GameWindow window, Point field, string value, CancellationToken token)
    {
        Guard(window, token); Send(window, Click(ScreenPoint(window, field)).ToArray(), token);
        await Task.Delay(100, token); Send(window, TextEvents(value), token); await Task.Delay(100, token);
    }
    public static void Submit(GameWindow window, Point button, CancellationToken token) => Send(window, Click(ScreenPoint(window, button)).ToArray(), token);
    public static async Task TabToPassword(GameWindow window, CancellationToken token)
    {
        Send(window, new[] { Key(0x09), Key(0x09, true) }, token);
        await Task.Delay(150, token);
    }
    public static async Task FillFocused(GameWindow window, string value, CancellationToken token)
    {
        Send(window, TextEvents(value), token); await Task.Delay(100, token);
    }
    public static void SubmitWithEnter(GameWindow window, CancellationToken token) => Send(window, new[] { Key(0x0D), Key(0x0D, true) }, token);
}

internal static class ClientLoginService
{
    public static bool ShouldLogin(ClientLogin? login, bool newlyLaunched) => newlyLaunched && login?.OnLaunch == true;
    public static async Task Run(Slot slot, GameWindow window, bool afterLaunch, Action<string> log, CancellationToken token)
    {
        if (slot.LaunchMethod != ClientLaunchMethod.Direct) throw new InvalidOperationException("Battle.net clients use the launcher's signed-in session, not private-server password entry.");
        var login = slot.Login ?? throw new InvalidOperationException("Add login details to this client first.");
        if (!Matching.SamePath(login.Executable, window.Executable) || !Matching.SamePath(login.Executable, slot.PathFor(new Layout { Executable = window.Executable })))
            throw new InvalidOperationException("This client uses a different executable. Set up its login again.");
        if (login.Screen is not { IsValid: true } setup) throw new InvalidOperationException("Set up this client's login screen before using its saved login.");
        if (afterLaunch) await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(login.DelaySeconds, 0, 120)), token);
        await LoginInput.Activate(window, token);
        bool matches = false; var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(afterLaunch ? 30 : 5))
        {
            using var image = LoginInput.Capture(window, token);
            if (image.Width != setup.Width || image.Height != setup.Height) throw new InvalidOperationException("The client size changed. Set up its login screen again at the saved window size.");
            if (LoginScreenMatch.Matches(image, setup)) { matches = true; break; }
            await Task.Delay(300, token);
        }
        if (!matches) throw new InvalidOperationException("The saved login-screen marker was not found. Login was skipped; open the login screen or set it up again.");
        var credentials = CredentialProtection.Unprotect(login.ProtectedCredentials);
        try
        {
            await LoginInput.Fill(window, setup.Account, credentials.Account, token);
            if (setup.KeyboardNavigation) await LoginInput.TabToPassword(window, token);
            using (var verify = LoginInput.Capture(window, token))
                if (!LoginScreenMatch.Matches(verify, setup)) throw new InvalidOperationException("The login screen changed. Password entry was stopped.");
            if (setup.KeyboardNavigation)
            {
                await LoginInput.FillFocused(window, credentials.Password, token);
                LoginInput.SubmitWithEnter(window, token);
            }
            else
            {
                await LoginInput.Fill(window, setup.Password, credentials.Password, token);
                LoginInput.Submit(window, setup.Submit, token);
            }
            log($"{slot.Name}: login submitted. Complete any server prompts or character selection in the client.");
        }
        finally { credentials = new("", ""); }
    }
}
