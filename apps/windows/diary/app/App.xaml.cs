using Microsoft.UI.Xaml;

namespace Fowan.Diary.Windows;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Fowan.Diary.Windows.SingleInstance";
    private const string ActivationEventName = @"Local\Fowan.Diary.Windows.Activate";
    private const string ShutdownEventName = @"Local\Fowan.Diary.Windows.Shutdown";

    private Window? _window;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private EventWaitHandle? _shutdownEvent;
    private CancellationTokenSource? _activationListenerCts;

    public App()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeSingleInstanceResources();
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!TryAcquireSingleInstance())
        {
            SignalExistingInstance();
            Exit();
            return;
        }

        var window = new DiaryWindow();
        _window = window;
        StartActivationListener(window);
        window.ActivateInitialMode();
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
        EnsureSingleInstanceEvents();
        return true;
    }

    private void EnsureSingleInstanceEvents()
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

    private void StartActivationListener(DiaryWindow window)
    {
        EnsureSingleInstanceEvents();
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

                if (signaled == 0)
                {
                    window.DispatcherQueue.TryEnqueue(window.RestoreFromExternalActivation);
                }
                else if (signaled == 1)
                {
                    window.DispatcherQueue.TryEnqueue(window.Close);
                    break;
                }
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
}
