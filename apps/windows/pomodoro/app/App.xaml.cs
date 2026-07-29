using Fowan.Pomodoro.Shared.Application;
using Fowan.Pomodoro.Shared.Services;
using Fowan.Pomodoro.Windows.Platform.Windows;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Fowan.Pomodoro.Windows;

public partial class App : global::Microsoft.UI.Xaml.Application
{
    private Window? _window;
    private AppInstance? _mainInstance;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // For unpackaged desktop apps, LaunchActivatedEventArgs.Arguments does not
        // consistently preserve a Process.Start command line.  Handle only the two
        // fixed background verbs before AppInstance routing so Toolbox maintenance
        // and reminder delivery can never activate the Pomodoro window.
        var backgroundCommand = GetBackgroundCommand(args);
        if (backgroundCommand == "--dispatch-due")
        {
            DispatchDue();
            Exit();
            return;
        }
        if (backgroundCommand == "--reconcile-wake")
        {
            PomodoroWorkspace.CreateDefault().ReconcileWake(DateTimeOffset.Now);
            Exit();
            return;
        }

        var currentInstance = AppInstance.GetCurrent();
        _mainInstance ??= AppInstance.FindOrRegisterForKey("fowan-pomodoro");
        if (!_mainInstance.IsCurrent)
        {
            await _mainInstance.RedirectActivationToAsync(currentInstance.GetActivatedEventArgs());
            Exit();
            return;
        }
        _window = new PomodoroWindow();
        _window.Activate();
    }

    private static string? GetBackgroundCommand(LaunchActivatedEventArgs args)
    {
        var commandLineCommand = Environment.GetCommandLineArgs()
            .Skip(1)
            .Select(argument => argument.Trim())
            .FirstOrDefault(IsBackgroundCommand);
        if (commandLineCommand is not null) return commandLineCommand;

        var activationCommand = args.Arguments?.Trim();
        return IsBackgroundCommand(activationCommand) ? activationCommand : null;
    }

    private static bool IsBackgroundCommand(string? command) =>
        string.Equals(command, "--dispatch-due", StringComparison.Ordinal) ||
        string.Equals(command, "--reconcile-wake", StringComparison.Ordinal);

    private static void DispatchDue()
    {
        var reader = new WindowsPomodoroTodoReader();
        var workspace = PomodoroWorkspace.CreateDefault();
        var notification = new WindowsPomodoroNotificationService();
        foreach (var item in workspace.DispatchDue(DateTimeOffset.Now, reader.IsActive)) notification.TryShowReminder(item);
        var settings = workspace.ReminderSettings;
        if (!DailyDigestBuilder.IsDueAt(DateTimeOffset.Now, settings)) return;
        var digest = DailyDigestBuilder.Build(DateOnly.FromDateTime(DateTime.Now), settings, reader.ActiveTasks());
        if (digest.HasTasks)
        {
            var start = digest.StartTasks.Select(task => task.Title);
            var due = digest.DueTasks.Select(task => task.Title);
            notification.TryShowDigest(string.Join("\n", start.Concat(due).Take(8)));
        }
        workspace.UpdateReminderSettings(current =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (current.IsStartDigestEnabled && TimeOnly.FromDateTime(DateTime.Now).ToTimeSpan() >= current.StartDigestTime) current.LastStartDigestDate = today;
            if (current.IsDueDigestEnabled && TimeOnly.FromDateTime(DateTime.Now).ToTimeSpan() >= current.DueDigestTime) current.LastDueDigestDate = today;
        });
    }
}
