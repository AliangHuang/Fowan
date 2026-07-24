using Fowan.Windows.Platform.Contracts;

namespace Fowan.Windows.AppPorts;

internal static class TrayVisibilityCoordinator
{
    public static PlatformOperationResult TryHide(Func<PlatformOperationResult> ensureTrayVisible, Action hideWindow)
    {
        var result = ensureTrayVisible();
        if (result.Succeeded)
        {
            hideWindow();
        }

        return result;
    }
}
