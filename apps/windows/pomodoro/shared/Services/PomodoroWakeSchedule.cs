using System.Text.Json;

namespace Fowan.Pomodoro.Shared.Services;

/// <summary>A deliberately narrow cross-process handoff: Toolbox reads only the next due time.</summary>
public static class PomodoroWakeSchedule
{
    private const string WakeScheduleMutexName = @"Local\Fowan.Pomodoro.WakeSchedule";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static DateTimeOffset? Read(string? rootPath = null)
    {
        var path = Path.Combine(ResolveRoot(rootPath), "next-wake.json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<WakeRecord>(File.ReadAllText(path), JsonOptions)?.NextWakeAt; }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    public static void Write(DateTimeOffset? nextWakeAt, string? rootPath = null)
    {
        var root = ResolveRoot(rootPath);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "next-wake.json");
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        using var mutex = new Mutex(false, WakeScheduleMutexName);
        var lockTaken = false;
        try
        {
            try { lockTaken = mutex.WaitOne(LockTimeout); }
            catch (AbandonedMutexException) { lockTaken = true; }
            if (!lockTaken) throw new TimeoutException("等待番茄钟唤醒计划锁超时。");
            File.WriteAllText(temporary, JsonSerializer.Serialize(new WakeRecord(nextWakeAt), JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (lockTaken) mutex.ReleaseMutex();
        }
    }

    private static string ResolveRoot(string? rootPath) => Path.GetFullPath(rootPath ??
        Environment.GetEnvironmentVariable("FOWAN_POMODORO_DATA_ROOT") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fowan", "Pomodoro"));

    private sealed record WakeRecord(DateTimeOffset? NextWakeAt);
}
