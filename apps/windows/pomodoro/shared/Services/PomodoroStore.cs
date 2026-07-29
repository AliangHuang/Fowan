using Fowan.Pomodoro.Models;
using System.Text;
using System.Text.Json;

namespace Fowan.Pomodoro.Services;

public interface IPomodoroRepository
{
    PomodoroData Load();
    void Save(PomodoroData data);
}

public sealed class PomodoroStore : IPomodoroRepository
{
    private const string MutexName = @"Local\Fowan.Pomodoro.Storage";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PomodoroStore(string? rootPath = null)
    {
        RootPath = Path.GetFullPath(rootPath ?? ResolveRoot());
        DataPath = Path.Combine(RootPath, "pomodoro-data.json");
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }
    public string DataPath { get; }

    public PomodoroData Load()
    {
        var data = File.Exists(DataPath)
            ? JsonSerializer.Deserialize<PomodoroData>(File.ReadAllText(DataPath), Options) ?? new PomodoroData()
            : new PomodoroData();
        Normalize(data);
        return data;
    }

    public void Save(PomodoroData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var mutex = new Mutex(false, MutexName);
        var taken = false;
        try
        {
            try { taken = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { taken = true; }
            if (!taken) throw new TimeoutException("等待番茄钟数据锁超时。");
            Normalize(data);
            var temporary = $"{DataPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(data, Options), new UTF8Encoding(false));
                File.Move(temporary, DataPath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { if (taken) mutex.ReleaseMutex(); }
    }

    private static void Normalize(PomodoroData data)
    {
        data.SchemaVersion = 1;
        data.Settings ??= new PomodoroSettings();
        data.Settings.FocusMinutes = Math.Clamp(data.Settings.FocusMinutes, 1, 180);
        data.Settings.ShortBreakMinutes = Math.Clamp(data.Settings.ShortBreakMinutes, 1, 60);
        data.Settings.LongBreakMinutes = Math.Clamp(data.Settings.LongBreakMinutes, 1, 120);
        data.Settings.FocusesPerCycle = Math.Clamp(data.Settings.FocusesPerCycle, 2, 12);
        data.Sessions ??= [];
        data.Active ??= new ActivePomodoro();
        data.Active.CompletedFocusesInCycle = Math.Max(0, data.Active.CompletedFocusesInCycle);
    }

    private static string ResolveRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("FOWAN_POMODORO_DATA_ROOT");
        return !string.IsNullOrWhiteSpace(overridePath)
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fowan", "Pomodoro");
    }
}
