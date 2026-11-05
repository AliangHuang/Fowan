using System.Diagnostics;

namespace Fowan.Windows.Services;

public static class StartupTrace
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static readonly List<string> Events = [];

    public static void Mark(string label)
    {
        lock (Gate)
        {
            Events.Add($"{Clock.ElapsedMilliseconds,6} ms  {label}");
        }
    }

    public static Task FlushAsync(string label)
    {
        string[] snapshot;
        var total = Clock.ElapsedMilliseconds;
        lock (Gate)
        {
            snapshot = Events.ToArray();
        }

        return Task.Run(() =>
        {
            try
            {
                var logRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Fowan",
                    "logs");
                Directory.CreateDirectory(logRoot);

                var lines = new List<string>
                {
                    $"[{DateTimeOffset.Now:O}] startup trace: {label}, total {total} ms"
                };
                lines.AddRange(snapshot);
                lines.Add(string.Empty);

                File.AppendAllLines(Path.Combine(logRoot, "startup.log"), lines);
            }
            catch
            {
                // Startup tracing is diagnostic only and must never affect launching.
            }
        });
    }
}
