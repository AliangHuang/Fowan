using Fowan.Windows.Services;
using Microsoft.UI.Xaml;

namespace Fowan.Windows;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        StartupTrace.Mark("App ctor begin");
        UnhandledException += (_, args) => LogStartupFailure(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogStartupFailure(exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) => LogStartupFailure(args.Exception);

        StartupTrace.Mark("App InitializeComponent begin");
        InitializeComponent();
        StartupTrace.Mark("App InitializeComponent end");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            StartupTrace.Mark("OnLaunched begin");
            _window = new MainWindow();
            StartupTrace.Mark("MainWindow constructed");
            _window.Activate();
            StartupTrace.Mark("Window activated");
            _ = StartupTrace.FlushAsync("window activated");
        }
        catch (Exception exception)
        {
            LogStartupFailure(exception);
            throw;
        }
    }

    private static void LogStartupFailure(Exception exception)
    {
        WriteLog($"[{DateTimeOffset.Now:O}] {exception}\n");
    }

    private static void WriteLog(string message)
    {
        try
        {
            var logRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Fowan",
                "logs");
            Directory.CreateDirectory(logRoot);
            File.AppendAllText(
                Path.Combine(logRoot, "client.log"),
                message);
        }
        catch
        {
            // Logging must never become the startup failure.
        }
    }
}
