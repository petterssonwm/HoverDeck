using System.Diagnostics;
using System.Text.Json;

namespace HoverDeck;

public sealed class DisplayRecovery
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int OwnerId { get; set; }
    public long OwnerStarted { get; set; }
    public DateTimeOffset Deadline { get; set; }
    public DisplayBackup Backup { get; set; } = new();
    public bool Applying { get; set; }
}

internal sealed class DisplayChangeSession : IDisposable
{
    internal static readonly string RecoveryPath = Path.Combine(Storage.DirectoryPath, "display-recovery.json");
    private const string MutexName = "Local\\HoverDeck.DisplayChange";
    private readonly IDisplaySettingsBackend backend;
    private readonly DisplayRecovery recovery;
    private bool finished;
    public DateTimeOffset Deadline => recovery.Deadline;
    private DisplayChangeSession(IDisplaySettingsBackend backend, DisplayRecovery recovery) { this.backend = backend; this.recovery = recovery; }
    private static T Locked<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, MutexName);
        try { mutex.WaitOne(); } catch (AbandonedMutexException) { }
        try { return action(); } finally { mutex.ReleaseMutex(); }
    }
    private static DisplayRecovery? Read() => File.Exists(RecoveryPath) ? JsonSerializer.Deserialize<DisplayRecovery>(File.ReadAllText(RecoveryPath), Storage.Json) : null;
    private static bool OwnerAlive(DisplayRecovery r)
    {
        try { using var p = Process.GetProcessById(r.OwnerId); return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == r.OwnerStarted; } catch { return false; }
    }
    internal static void ApplyWithRollback(IDisplaySettingsBackend backend, IReadOnlyList<MonitorInfo> plan, DisplayBackup backup)
    {
        try { backend.Apply(plan); }
        catch (Exception applyError)
        {
            try { backend.Restore(backup); }
            catch (Exception restoreError) { throw new AggregateException("The display change failed and Windows also rejected restoration.", applyError, restoreError); }
            throw;
        }
    }
    public static async Task<DisplayChangeSession> Begin(IReadOnlyList<MonitorInfo> plan, IReadOnlyList<MonitorInfo> expected)
    {
        if (!DisplayArrangement.SamePositions(expected, Native.Monitors())) throw new InvalidOperationException("Windows' display arrangement changed. Review the refreshed map first.");
        var backend = new WindowsDisplaySettings();
        backend.Test(plan);
        using var owner = Process.GetCurrentProcess();
        var recovery = new DisplayRecovery { OwnerId = owner.Id, OwnerStarted = owner.StartTime.ToUniversalTime().Ticks, Deadline = DateTimeOffset.UtcNow.AddSeconds(45), Backup = backend.Capture() };
        Locked(() => { if (Read() is not null) throw new InvalidOperationException("A previous display change still needs recovery. Restart HoverDeck before trying again."); Storage.Write(RecoveryPath, recovery); return true; });
        var session = new DisplayChangeSession(backend, recovery);
        try
        {
            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\HoverDeck.DisplayReady." + recovery.Id);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--display-watch"); start.ArgumentList.Add(recovery.Id);
            using var watcher = Process.Start(start) ?? throw new InvalidOperationException("Could not start display recovery.");
            if (!await Task.Run(() => ready.WaitOne(5000))) throw new TimeoutException("The display recovery process did not start. No display changes were applied.");
            Locked(() =>
            {
                var current = Read();
                if (current?.Id != recovery.Id) throw new InvalidOperationException("The display change expired before it could be applied.");
                if (!DisplayArrangement.SamePositions(expected, Native.Monitors())) throw new InvalidOperationException("The connected displays changed while preparing the update.");
                recovery.Applying = true; Storage.Write(RecoveryPath, recovery);
                ApplyWithRollback(backend, plan, recovery.Backup);
                if (!DisplayArrangement.SamePositions(plan, Native.Monitors())) throw new InvalidOperationException("Windows did not accept the exact requested arrangement. The original positions will be restored.");
                recovery.Deadline = DateTimeOffset.UtcNow.AddSeconds(20);
                Storage.Write(RecoveryPath, recovery);
                return true;
            });
            return session;
        }
        catch
        {
            session.Revert();
            throw;
        }
    }
    public bool Keep()
    {
        if (finished) return false;
        bool kept = Locked(() =>
        {
            var current = Read();
            if (current?.Id != recovery.Id) return false;
            if (DateTimeOffset.UtcNow >= current.Deadline) return false;
            File.Delete(RecoveryPath); return true;
        });
        if (kept) finished = true;
        return kept;
    }
    public void Revert()
    {
        if (finished) return;
        Locked(() =>
        {
            var current = Read();
            if (current?.Id == recovery.Id)
            {
                if (current.Applying) backend.Restore(current.Backup);
                File.Delete(RecoveryPath);
            }
            return true;
        });
        finished = true;
    }
    public static void RecoverOnStartup()
    {
        Locked(() =>
        {
            var current = Read();
            if (current is null || OwnerAlive(current)) return true;
            if (current.Applying) new WindowsDisplaySettings().Restore(current.Backup);
            File.Delete(RecoveryPath); return true;
        });
    }
    public static void Watch(string id)
    {
        try
        {
            using var ready = EventWaitHandle.OpenExisting("Local\\HoverDeck.DisplayReady." + id); ready.Set();
            while (true)
            {
                bool done = Locked(() =>
                {
                    var current = Read();
                    if (current?.Id != id) return true;
                    if (OwnerAlive(current) && DateTimeOffset.UtcNow < current.Deadline) return false;
                    if (current.Applying) new WindowsDisplaySettings().Restore(current.Backup);
                    File.Delete(RecoveryPath); return true;
                });
                if (done) break;
                Thread.Sleep(250);
            }
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Storage.DirectoryPath, "display-recovery-error.log"), DateTimeOffset.Now + " " + ex + Environment.NewLine); } catch { }
        }
    }
    public void Dispose() => Revert();
}
