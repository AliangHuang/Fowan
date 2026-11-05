using Fowan.Windows.AppPorts;
using Fowan.Windows.Platform.Contracts;
using Xunit;

namespace Fowan.Windows.Platform.Tests;

public sealed class TrayMessageRouterTests
{
    [Fact]
    public void CloseIsHandledOnlyWhenMinimizeWasQueued()
    {
        var rejected = CreateRouter(new StubDispatcher(false), shouldMinimize: true);
        var accepted = CreateRouter(new StubDispatcher(true), shouldMinimize: true);

        Assert.Equal(TrayMessageDisposition.Forward, rejected.Route(TrayMessageRouter.CloseMessage, 0));
        Assert.Equal(TrayMessageDisposition.Handled, accepted.Route(TrayMessageRouter.CloseMessage, 0));
    }

    [Fact]
    public void DirectExitCloseIsForwarded()
    {
        var router = CreateRouter(new StubDispatcher(true), shouldMinimize: false);

        Assert.Equal(TrayMessageDisposition.Forward, router.Route(TrayMessageRouter.CloseMessage, 0));
    }

    [Theory]
    [InlineData(TrayMessageRouter.LeftButtonUp)]
    [InlineData(TrayMessageRouter.LeftButtonDoubleClick)]
    public void LeftClickQueuesRestore(int trayEvent)
    {
        var restored = false;
        var router = CreateRouter(new StubDispatcher(true), restore: () => restored = true);

        Assert.Equal(TrayMessageDisposition.Handled, router.Route(TrayMessageRouter.TrayIconMessage, trayEvent));
        Assert.True(restored);
    }

    [Fact]
    public void RightClickQueuesContextMenu()
    {
        var opened = false;
        var router = CreateRouter(new StubDispatcher(true), showContextMenu: () => opened = true);

        Assert.Equal(
            TrayMessageDisposition.Handled,
            router.Route(TrayMessageRouter.TrayIconMessage, TrayMessageRouter.RightButtonUp));
        Assert.True(opened);
    }

    [Theory]
    [InlineData(TrayCommandRouter.RestoreCommandId, (int)TrayCommand.Restore)]
    [InlineData(TrayCommandRouter.ExitCommandId, (int)TrayCommand.Exit)]
    [InlineData(0u, (int)TrayCommand.None)]
    [InlineData(9999u, (int)TrayCommand.None)]
    public void NativeMenuCommandsHaveStrictMapping(uint command, int expected)
    {
        Assert.Equal(expected, (int)new TrayCommandRouter().Route(command));
    }

    [Fact]
    public void DispatcherRejectionForwardsTrayMessageWithoutRunningAction()
    {
        var restored = false;
        var router = CreateRouter(new StubDispatcher(false), restore: () => restored = true);

        Assert.Equal(
            TrayMessageDisposition.Forward,
            router.Route(TrayMessageRouter.TrayIconMessage, TrayMessageRouter.LeftButtonUp));
        Assert.False(restored);
    }

    [Fact]
    public void TrayInitializationFailureDoesNotHideWindow()
    {
        var hidden = false;

        var result = TrayVisibilityCoordinator.TryHide(
            () => PlatformOperationResult.Failure("tray unavailable"),
            () => hidden = true);

        Assert.False(result.Succeeded);
        Assert.False(hidden);
    }

    [Theory]
    [InlineData("message hook failed")]
    [InlineData("notification icon failed")]
    public void AnyTrayInitializationFailureKeepsWindowVisible(string error)
    {
        var hidden = false;

        var result = TrayVisibilityCoordinator.TryHide(
            () => PlatformOperationResult.Failure(error),
            () => hidden = true);

        Assert.Equal(error, result.Error);
        Assert.False(hidden);
    }

    private static TrayMessageRouter CreateRouter(
        IUiDispatcher dispatcher,
        bool shouldMinimize = true,
        Action? restore = null,
        Action? showContextMenu = null) =>
        new(dispatcher, () => shouldMinimize, () => { }, restore ?? (() => { }), showContextMenu ?? (() => { }));

    private sealed class StubDispatcher(bool acceptsWork) : IUiDispatcher
    {
        public bool TryEnqueue(Action action)
        {
            if (acceptsWork)
            {
                action();
            }

            return acceptsWork;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default) =>
            acceptsWork ? action() : Task.FromException(new UiDispatcherUnavailableException());
    }
}
