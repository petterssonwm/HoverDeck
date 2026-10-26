using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace HoverDeck;

public sealed class ClientLogin
{
    public string ProtectedCredentials { get; set; } = "";
    public string Executable { get; set; } = "";
    public bool OnLaunch { get; set; } = true;
    public int DelaySeconds { get; set; } = 8;
    public LoginScreen? Screen { get; set; }
}
public sealed class LoginScreen
{
    public int Width { get; set; }
    public int Height { get; set; }
    public Point Account { get; set; }
    public Point Password { get; set; }
    public Point Submit { get; set; }
    public bool KeyboardNavigation { get; set; }
    public Rectangle Landmark { get; set; }
    public string LandmarkPng { get; set; } = "";
    public bool IsValid => Width > 0 && Height > 0 && new Rectangle(0, 0, Width, Height).Contains(Account)
        && (KeyboardNavigation || (new Rectangle(0, 0, Width, Height).Contains(Password)
            && new Rectangle(0, 0, Width, Height).Contains(Submit) && Account != Password))
        && Landmark.Width >= 16 && Landmark.Height >= 10
        && new Rectangle(0, 0, Width, Height).Contains(Landmark) && LandmarkPng.Length > 0;
}
internal sealed record LoginCredentials(string Account, string Password);

internal static class CredentialProtection
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public nint Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(ref Blob input, string description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    public static string Protect(LoginCredentials credentials)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(credentials);
        try { return Convert.ToBase64String(Transform(bytes, true)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static LoginCredentials Unprotect(string encrypted)
    {
        byte[] plain = Array.Empty<byte>();
        try { plain = Transform(Convert.FromBase64String(encrypted), false); return JsonSerializer.Deserialize<LoginCredentials>(plain) ?? throw new CryptographicException(); }
        catch { throw new InvalidOperationException("The saved login cannot be unlocked by this Windows account. Enter the account and password again."); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok = encrypt ? CryptProtectData(ref input, "HoverDeck client login", 0, 0, 0, 1, out output) : CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!ok) throw new CryptographicException("Windows could not protect or unlock this login.");
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            for (int i = 0; i < input.Size; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != 0) { for (int i = 0; i < output.Size; i++) Marshal.WriteByte(output.Data, i, 0); LocalFree(output.Data); }
        }
    }
}

internal static class LoginScreenMatch
{
    public static bool Matches(Bitmap screen, LoginScreen setup)
    {
        if (!setup.IsValid || screen.Width != setup.Width || screen.Height != setup.Height) return false;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(setup.LandmarkPng)); using var reference = new Bitmap(stream);
            if (reference.Size != setup.Landmark.Size || !HasDetail(reference)) return false;
            double error = 0; int samples = 0, largeDifferences = 0;
            for (int y = 0; y < reference.Height; y += 2)
                for (int x = 0; x < reference.Width; x += 2)
                {
                    var a = reference.GetPixel(x, y); var b = screen.GetPixel(setup.Landmark.X + x, setup.Landmark.Y + y);
                    int delta = Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                    error += delta / 3.0; if (delta > 90) largeDifferences++; samples++;
                }
            return error / samples <= 12 && largeDifferences <= samples * .12;
        }
        catch { return false; }
    }
    public static bool HasDetail(Bitmap image)
    {
        double sum = 0, squares = 0; int count = 0;
        for (int y = 0; y < image.Height; y += 2)
            for (int x = 0; x < image.Width; x += 2) { var c = image.GetPixel(x, y); double v = (c.R + c.G + c.B) / 3.0; sum += v; squares += v * v; count++; }
        return count > 0 && squares / count - Math.Pow(sum / count, 2) > 180;
    }
}
