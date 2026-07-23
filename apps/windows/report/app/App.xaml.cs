using Fowan.Report.Windows.Platform.Windows;
using Microsoft.UI.Xaml;

namespace Fowan.Report.Windows;

public partial class App : global::Microsoft.UI.Xaml.Application
{
    private Window? _window;
    private static bool IsVisualFixtureRequested(IEnumerable<string> arguments)
    {
#if DEBUG
        return arguments.Any(argument => string.Equals(argument, "--visual-fixture", StringComparison.Ordinal));
#else
        _ = arguments;
        return false;
#endif
    }

    public App()
    {
        UnhandledException += (_, args) => ReportSafeDiagnostics.Record(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception) ReportSafeDiagnostics.Record(exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => ReportSafeDiagnostics.Record(args.Exception);
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new ReportWindow(IsVisualFixtureRequested(Environment.GetCommandLineArgs()));
            _window.Activate();
            ((ReportWindow)_window).InitializeEditorSurfaces();
        }
        catch (Exception exception)
        {
            ReportSafeDiagnostics.Record(exception);
            throw;
        }
    }
}
