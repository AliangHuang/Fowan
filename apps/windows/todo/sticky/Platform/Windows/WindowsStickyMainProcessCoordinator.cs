using Fowan.Todo.Sticky.Windows.AppPorts;
using Fowan.Windows.Platform.Contracts;
using System.IO;
using System.Threading;

namespace Fowan.Todo.Sticky.Windows.Platform.Windows;

internal sealed class WindowsStickyMainProcessCoordinator(IProcessLauncher launcher) : IStickyMainProcessCoordinator
{
    private const string ActivationEventName = @"Local\Fowan.Todo.Windows.Activate";
    private const string ShutdownEventName = @"Local\Fowan.Todo.Windows.Shutdown";

    public bool TryActivate(string executablePath) =>
        TrySignalEvent(ActivationEventName) ||
        (File.Exists(executablePath) &&
         launcher.Launch(new ProcessLaunchRequest(executablePath, UseShellExecute: false)).Succeeded);

    public bool TryShutdown() => TrySignalEvent(ShutdownEventName, attempts: 4);

    private static bool TrySignalEvent(string eventName, int attempts = 20)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(eventName);
                activationEvent.Set();
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }
}
