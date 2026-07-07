using Microsoft.UI.Xaml;
using Fowan.Todo.Core.Services;

namespace Fowan.Todo.Windows;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var store = new TodoStore();
        var settings = store.LoadSettings();
        if (settings.IsStickyModeEnabled)
        {
            if (StickyLauncher.TryLaunch(out _))
            {
                Exit();
                return;
            }

            settings.IsStickyModeEnabled = false;
            store.SaveSettings(settings);
        }

        var window = new TodoWindow();
        _window = window;
        window.ActivateInitialMode();
    }
}
