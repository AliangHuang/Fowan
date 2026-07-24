namespace Fowan.Report.Windows.Platform.Windows;

/// <summary>Crash diagnostics intentionally exclude exception messages and user-authored content.</summary>
internal static class ReportSafeDiagnostics
{
    public static void Record(Exception exception) => Write("unhandled", exception);

    /// <summary>Records only a controlled operation name, exception type and HRESULT for handled UI failures.</summary>
    public static void RecordHandled(string operation, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fowan", "logs");
            Directory.CreateDirectory(root);
            File.AppendAllText(Path.Combine(root, "report-client.log"), Format($"handled:{operation}", exception));
        }
        catch
        {
            // Diagnostics must never become the reason an application terminates.
        }
    }

    private static void Write(string kind, Exception exception)
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fowan", "logs");
            Directory.CreateDirectory(root);
            File.AppendAllText(Path.Combine(root, "report-client.log"), Format(kind, exception));
        }
        catch
        {
            // Diagnostics must never become the reason an application terminates.
        }
    }

    internal static string Format(Exception exception) => Format("unhandled", exception);

    internal static string Format(string kind, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var frames = exception.StackTrace?
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(frame => frame.Contains("Fowan.", StringComparison.Ordinal))
            .Take(8)
            .Select(frame => frame.Trim()) ?? [];
        return $"[{DateTimeOffset.Now:O}] report-{kind} type={exception.GetType().FullName} hresult=0x{exception.HResult:X8}{Environment.NewLine}" +
            string.Join(Environment.NewLine, frames) + Environment.NewLine;
    }
}
