using Fowan.Todo.Shared.Application;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoOnboardingCoordinator(Action enterModal, Action exitModal)
{
    private readonly TodoOnboardingService _service = new(enterModal, exitModal);
    private bool _pending;

    public bool IsShowing => _service.IsShowing;

    public void Dismiss() => _service.Dismiss();

    public void Queue(
        Func<TodoSettingsSnapshot> settings,
        Func<Grid> root,
        Func<Button> stickyButton,
        Func<Button> helpButton,
        Func<TodoOnboardingPalette> palette,
        TodoOnboardingControls controls,
        Func<Task> waitForLayout,
        Action markCompleted)
    {
        if (!ShouldShow(settings()) || _pending) return;
        _pending = true;
        _ = ShowWhenReadyAsync(
            settings, root, stickyButton, helpButton, palette, controls, waitForLayout, markCompleted);
    }

    private async Task ShowWhenReadyAsync(
        Func<TodoSettingsSnapshot> settings,
        Func<Grid> root,
        Func<Button> stickyButton,
        Func<Button> helpButton,
        Func<TodoOnboardingPalette> palette,
        TodoOnboardingControls controls,
        Func<Task> waitForLayout,
        Action markCompleted)
    {
        try
        {
            while (true)
            {
                var currentRoot = root();
                await WaitForLoadedAsync(currentRoot);
                await waitForLayout();
                if (!ReferenceEquals(currentRoot, root())) continue;
                if (!ShouldShow(settings())) return;
                _service.Show(
                    currentRoot,
                    stickyButton(),
                    helpButton(),
                    palette(),
                    controls,
                    markCompleted);
                return;
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to prepare Todo onboarding: {exception}");
        }
        finally
        {
            _pending = false;
        }
    }

    private bool ShouldShow(TodoSettingsSnapshot settings) =>
        !settings.HasCompletedMainOnboarding && !settings.IsStickyModeEnabled && !_service.IsShowing;

    private static Task WaitForLoadedAsync(FrameworkElement element)
    {
        if (element.IsLoaded) return Task.CompletedTask;
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler? loaded = null;
        loaded = (_, _) =>
        {
            element.Loaded -= loaded;
            completion.TrySetResult(null);
        };
        element.Loaded += loaded;
        return completion.Task;
    }
}
