using Fowan.Todo.Sticky.Windows.AppPorts;
using Fowan.Windows.Platform.Contracts;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Fowan.Todo.Sticky.Windows.Platform.Windows;

internal sealed class WindowsStickyMainProcessCoordinator(IProcessLauncher launcher) : IStickyMainProcessCoordinator
{
    private const string ActivationEventName = @"Local\Fowan.Todo.Windows.Activate";
    private const string ShutdownEventName = @"Local\Fowan.Todo.Windows.Shutdown";
    private const uint AllowAnyProcessToSetForeground = 0xFFFFFFFF;

    public bool TryActivate(string executablePath)
    {
        // The sticky window is the foreground process when the user clicks “回到大界面”.
        // Grant the receiving main process a short-lived foreground handoff before it handles
        // the activation event; otherwise Windows can reject its SetForegroundWindow call.
        AllowSetForegroundWindow(AllowAnyProcessToSetForeground);
        return TrySignalEvent(ActivationEventName) ||
               (File.Exists(executablePath) &&
                launcher.Launch(new ProcessLaunchRequest(executablePath, UseShellExecute: false)).Succeeded);
    }

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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
