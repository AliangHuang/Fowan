using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Fowan.Todo.Sticky.Windows.Platform.Windows;
using Xunit;

namespace Fowan.Windows.Platform.Tests;

public sealed class StickyFloatingModeTests
{
    [Fact]
    public void ExitingFloatingModeBeforeFirstShowKeepsPrewarmedWindowHidden()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var repository = new MemoryTodoRepository();
                var workspace = new TodoWorkspace(repository);
                window = new Window { Width = 52, Height = 52, ShowInTaskbar = false };
                new WindowInteropHelper(window).EnsureHandle();
                var refreshCount = 0;
                var controller = new StickyFloatingModeController(
                    window, workspace, () => (0, 55), () => (1, 1), () => (1920, 1080),
                    () => 55, () => { }, () => { }, () => { }, () => { },
                    () => refreshCount++, _ => { }, (_, _) => { }, (_, _) => { }, _ => { });

                controller.Exit();

                Assert.False(window.IsVisible);
                Assert.False(workspace.State.Settings.IsStickyFloatingModeEnabled);
                Assert.Null(workspace.State.Settings.StickyFloatingEdge);
                Assert.Null(workspace.State.Settings.StickyFloatingTop);
                Assert.False(repository.Settings.IsStickyFloatingModeEnabled);
                Assert.Equal(1, refreshCount);
                Assert.False(controller.IsApplying);

                window.Show();
                window.Activate();
                Assert.True(window.IsVisible);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Sticky mode transition did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class MemoryTodoRepository : ITodoRepository
    {
        private readonly TodoData _data = new();
        public TodoSettings Settings { get; private set; } = new()
        {
            IsStickyFloatingModeEnabled = true,
            StickyFloatingEdge = TodoStickyFloatingEdges.Right,
            StickyFloatingTop = 100
        };

        public TodoData LoadData() => _data;
        public TodoSettings LoadSettings() => Settings;
        public void SaveData(TodoData data) { }
        public void SaveSettings(TodoSettings settings) => Settings = settings;
        public bool UpdateData(Func<TodoData, TodoSettings, bool> update) => update(_data, Settings);
    }
}
