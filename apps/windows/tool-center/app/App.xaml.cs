using Fowan.ToolCenter.Windows.Platform.Windows;
using Fowan.ToolCenter.Windows.Application;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Fowan.ToolCenter.Windows;

public partial class App : global::Microsoft.UI.Xaml.Application
{
    private readonly ToolCenterSession _session;
    private ToolCenterWindow? _window;
    private DispatcherQueue? _dispatcher;
    private AppInstance? _instance;

    public App()
    {
        _session = ToolCenterCompositionRoot.CreateSession();
        if (_session.Preferences.Theme == "light") RequestedTheme = ApplicationTheme.Light;
        if (_session.Preferences.Theme == "dark") RequestedTheme = ApplicationTheme.Dark;
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
#if FOWAN_DEVELOPMENT_RUNTIME
        const string instanceKey = "fowan-tool-center-dev";
#else
        const string instanceKey = "fowan-tool-center";
#endif
        _instance = AppInstance.FindOrRegisterForKey(instanceKey);
        if (!_instance.IsCurrent)
        {
            await _instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
            _session.Dispose();
            Exit();
            return;
        }
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _instance.Activated += OnActivated;
        _window = new ToolCenterWindow(_session);
        _window.Closed += (_, _) => _instance.Activated -= OnActivated;
        _window.Activate();
    }

    private void OnActivated(object? sender, AppActivationArguments args) =>
        _dispatcher?.TryEnqueue(() =>
        {
            if (_window is not null) ToolCenterWindowActivation.Restore(_window);
        });
}
