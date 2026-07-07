using Fowan.Windows.Services;
using Microsoft.UI.Xaml;

namespace Fowan.Windows;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Fowan.Windows.SingleInstance";
    private const string ActivationEventName = @"Local\Fowan.Windows.Activate";

    private Window? _window;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationListenerCts;

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
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeSingleInstanceResources();

        StartupTrace.Mark("App InitializeComponent begin");
        InitializeComponent();
        StartupTrace.Mark("App InitializeComponent end");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            StartupTrace.Mark("OnLaunched begin");
            if (!TryAcquireSingleInstance())
            {
                SignalExistingInstance();
                StartupTrace.Mark("Secondary instance signaled primary");
                Exit();
                return;
            }

            var mainWindow = new MainWindow();
            _window = mainWindow;
            StartupTrace.Mark("MainWindow constructed");
            mainWindow.Activate();
            StartupTrace.Mark("Window activated");
            StartActivationListener(mainWindow);
            mainWindow.QueueStartupUpdateCheck();
            _ = StartupTrace.FlushAsync("window activated");
        }
        catch (Exception exception)
        {
            LogStartupFailure(exception);
            throw;
        }
    }

    private bool TryAcquireSingleInstance()
    {
        var mutex = new Mutex(initiallyOwned: false, SingleInstanceMutexName);
        var acquired = false;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            mutex.Dispose();
            return false;
        }

        _singleInstanceMutex = mutex;
        EnsureActivationEvent();
        return true;
    }

    private void EnsureActivationEvent()
    {
        _activationEvent ??= new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ActivationEventName);
    }

    private void SignalExistingInstance()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
                activationEvent.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private void StartActivationListener(MainWindow mainWindow)
    {
        EnsureActivationEvent();
        _activationListenerCts = new CancellationTokenSource();
        var token = _activationListenerCts.Token;
        var activationEvent = _activationEvent;
        if (activationEvent is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            var handles = new WaitHandle[] { activationEvent, token.WaitHandle };
            while (!token.IsCancellationRequested)
            {
                var signaled = WaitHandle.WaitAny(handles);
                if (signaled != 0 || token.IsCancellationRequested)
                {
                    break;
                }

                mainWindow.DispatcherQueue.TryEnqueue(mainWindow.RestoreFromExternalActivation);
            }
        }, token);
    }

    private void DisposeSingleInstanceResources()
    {
        _activationListenerCts?.Cancel();
        _activationListenerCts?.Dispose();
        _activationListenerCts = null;

        _activationEvent?.Dispose();
        _activationEvent = null;

        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex is process-owned; release can fail if ownership has already been lost during shutdown.
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
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
