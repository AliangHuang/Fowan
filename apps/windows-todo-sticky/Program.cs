using System.Windows;

namespace Fowan.Todo.Sticky.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        app.Run(new StickyWindow(args));
    }
}
