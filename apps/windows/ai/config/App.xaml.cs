using Microsoft.UI.Xaml;

namespace Fowan.Ai.Config.Windows;

public partial class App : Application
{
    private const string MutexName = @"Local\Fowan.Ai.Config.Windows.SingleInstance";
    private const string ActivationEventPrefix = @"Local\Fowan.Ai.Config.Windows.Activate";
    private static readonly string[] Pages = ["credentials", "models", "bindings"];
    private Mutex? _mutex;
    private readonly List<EventWaitHandle> _activationEvents = [];
    private CancellationTokenSource? _listenerCts;
    private Window? _window;

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
        var page = RequestedPage();
        if (!AcquireSingleInstance())
        {
            SignalExistingInstance(page);
            Exit();
            return;
        }
        WriteStartupTrace($"Creating ConfigWindow page={page}");
        ConfigWindow window;
        try
        {
            window = new ConfigWindow(page);
        }
        catch (Exception exception)
        {
            WriteStartupTrace($"ConfigWindow creation failed: {exception}");
            throw;
        }
        _window = window;
        StartListener(window);
        window.Activate();
        WriteStartupTrace("ConfigWindow activated");
    }

    private static void WriteStartupTrace(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Fowan",
                "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "ai-config-startup.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private bool AcquireSingleInstance()
    {
        var mutex = new Mutex(false, MutexName);
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
        foreach (var page in Pages)
        {
            _activationEvents.Add(new EventWaitHandle(false, EventResetMode.AutoReset, EventName(page)));
        }
        return true;
    }

    private static string RequestedPage()
    {
        var value = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))?
            .Split('=', 2)[1]
            .ToLowerInvariant();
        return value is not null && Pages.Contains(value) ? value : "credentials";
    }

    private static string EventName(string page) => $"{ActivationEventPrefix}.{page}";

    private static void SignalExistingInstance(string page)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var activation = EventWaitHandle.OpenExisting(EventName(page));
                activation.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private void StartListener(ConfigWindow window)
    {
        _listenerCts = new CancellationTokenSource();
        var token = _listenerCts.Token;
        _ = Task.Run(() =>
        {
            var handles = _activationEvents.Cast<WaitHandle>().Append(token.WaitHandle).ToArray();
            while (!token.IsCancellationRequested)
            {
                var index = WaitHandle.WaitAny(handles);
                if (index >= Pages.Length || token.IsCancellationRequested) break;
                var page = Pages[index];
                window.DispatcherQueue.TryEnqueue(() => window.NavigateTo(page));
            }
        }, token);
    }

    private void DisposeResources()
    {
        _listenerCts?.Cancel();
        _listenerCts?.Dispose();
        foreach (var activation in _activationEvents) activation.Dispose();
        _activationEvents.Clear();
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex.Dispose();
        }
        _window = null;
    }
}
