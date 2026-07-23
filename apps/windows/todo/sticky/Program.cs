using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Fowan.Todo.Sticky.Windows.Platform.Windows;

namespace Fowan.Todo.Sticky.Windows;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\Fowan.Todo.Sticky.Windows.SingleInstance";
    private const string ActivationEventName = @"Local\Fowan.Todo.Sticky.Windows.Activate";
    private const string ShutdownEventName = @"Local\Fowan.Todo.Sticky.Windows.Shutdown";
    private const string StartHiddenArgument = "--start-hidden";

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _activationEvent;
    private static EventWaitHandle? _shutdownEvent;
    private static CancellationTokenSource? _activationListenerCts;

    [STAThread]
    private static void Main(string[] args)
    {
        EnsureWindowsDirectoryEnvironment();

        if (!TryAcquireSingleInstance())
        {
            SignalExistingInstance();
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeSingleInstanceResources();

        try
        {
            var startHidden = HasArgument(args, StartHiddenArgument);
            StickyDpiAwarenessBootstrapper.EnsurePerMonitorAwareness();
            var app = new global::System.Windows.Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };
            app.Exit += (_, _) => DisposeSingleInstanceResources();

            var window = new StickyWindow(args);
            StartActivationListener(window);
            if (startHidden)
            {
                app.MainWindow = window;
                new WindowInteropHelper(window).EnsureHandle();
                app.Run();
            }
            else
            {
                app.Run(window);
            }
        }
        finally
        {
            DisposeSingleInstanceResources();
        }
    }

    private static void EnsureWindowsDirectoryEnvironment()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINDIR")))
        {
            return;
        }

        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsDirectory))
        {
            Environment.SetEnvironmentVariable("WINDIR", windowsDirectory);
        }
    }

    private static bool TryAcquireSingleInstance()
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

    private static void EnsureActivationEvent()
    {
        _activationEvent ??= new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ActivationEventName);
        _shutdownEvent ??= new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ShutdownEventName);
    }

    private static void SignalExistingInstance()
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

    private static void StartActivationListener(StickyWindow window)
    {
        EnsureActivationEvent();
        _activationListenerCts = new CancellationTokenSource();
        var token = _activationListenerCts.Token;
        var activationEvent = _activationEvent;
        var shutdownEvent = _shutdownEvent;
        if (activationEvent is null || shutdownEvent is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            var handles = new WaitHandle[] { activationEvent, shutdownEvent, token.WaitHandle };
            while (!token.IsCancellationRequested)
            {
                var signaled = WaitHandle.WaitAny(handles);
                if (signaled == 2 || token.IsCancellationRequested)
                {
                    break;
                }

                if (window.Dispatcher.HasShutdownStarted || window.Dispatcher.HasShutdownFinished)
                {
                    break;
                }

                if (signaled == 0)
                {
                    window.Dispatcher.BeginInvoke((Action)window.RestoreFromExternalActivation);
                }
                else if (signaled == 1)
                {
                    window.Dispatcher.BeginInvoke((Action)window.CloseFromCoordinatorShutdown);
                    break;
                }
            }
        }, token);
    }

    private static void DisposeSingleInstanceResources()
    {
        _activationListenerCts?.Cancel();
        _activationListenerCts?.Dispose();
        _activationListenerCts = null;

        _activationEvent?.Dispose();
        _activationEvent = null;

        _shutdownEvent?.Dispose();
        _shutdownEvent = null;

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

    private static bool HasArgument(string[] args, string argument)
    {
        return args.Any(arg => string.Equals(arg, argument, StringComparison.OrdinalIgnoreCase));
    }
}
