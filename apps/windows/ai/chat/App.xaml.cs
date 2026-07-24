using Microsoft.UI.Xaml;

namespace Fowan.Ai.Chat.Windows;

public partial class App : Application
{
    private const string MainMutexName = @"Local\Fowan.Ai.Chat.Windows.SingleInstance";
    private const string MainActivationEventName = @"Local\Fowan.Ai.Chat.Windows.Activate";
    private const string VisualFixtureMutexName = @"Local\Fowan.Ai.Chat.Windows.VisualFixture";
    private const string VisualFixtureActivationEventName = @"Local\Fowan.Ai.Chat.Windows.VisualFixture.Activate";
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _listenerCts;
    private Window? _window;
    private readonly bool _visualFixtureRequested = IsVisualFixtureRequested(Environment.GetCommandLineArgs());
    private string InstanceMutexName => _visualFixtureRequested ? VisualFixtureMutexName : MainMutexName;
    private string InstanceActivationEventName => _visualFixtureRequested ? VisualFixtureActivationEventName : MainActivationEventName;

    public App()
    {
        WriteStartupTrace("App constructor begin");
        try
        {
            InitializeComponent();
            WriteStartupTrace("App InitializeComponent completed");
        }
        catch (Exception exception)
        {
            WriteStartupTrace($"App InitializeComponent failed: {exception.GetType().FullName}: {exception.Message}");
            throw;
        }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeResources();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!AcquireSingleInstance())
        {
            SignalExistingInstance(InstanceActivationEventName);
            Exit();
            return;
        }
        WriteStartupTrace("Creating ChatWindow");
        ChatWindow window;
        try
        {
            window = new ChatWindow(_visualFixtureRequested);
        }
        catch (Exception exception)
        {
            WriteStartupTrace($"ChatWindow creation failed: {exception}");
            throw;
        }
        _window = window;
        StartListener(window);
        window.Activate();
        WriteStartupTrace("ChatWindow activated");
    }

    private static bool IsVisualFixtureRequested(IEnumerable<string> arguments)
    {
#if DEBUG
        return arguments
            .Any(argument => string.Equals(argument, "--visual-fixture", StringComparison.Ordinal));
#else
        _ = arguments;
        return false;
#endif
    }

    private static void WriteStartupTrace(string message)
    {
        if (IsVisualFixtureRequested(Environment.GetCommandLineArgs())) return;
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Fowan",
                "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "ai-chat-startup.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private bool AcquireSingleInstance()
    {
        var mutex = new Mutex(false, InstanceMutexName);
        try
        {
            if (!mutex.WaitOne(0))
            {
                mutex.Dispose();
                return false;
            }
        }
        catch (AbandonedMutexException)
        {
        }
        _mutex = mutex;
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceActivationEventName);
        return true;
    }

    private static void SignalExistingInstance(string activationEventName)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var activation = EventWaitHandle.OpenExisting(activationEventName);
                activation.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private void StartListener(ChatWindow window)
    {
        _listenerCts = new CancellationTokenSource();
        var token = _listenerCts.Token;
        var activation = _activationEvent;
        if (activation is null) return;
        _ = Task.Run(() =>
        {
            var handles = new WaitHandle[] { activation, token.WaitHandle };
            while (!token.IsCancellationRequested && WaitHandle.WaitAny(handles) == 0)
            {
                window.DispatcherQueue.TryEnqueue(window.RestoreFromExternalActivation);
            }
        }, token);
    }

    private void DisposeResources()
    {
        _listenerCts?.Cancel();
        _listenerCts?.Dispose();
        _activationEvent?.Dispose();
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex.Dispose();
        }
        _window = null;
    }
}
