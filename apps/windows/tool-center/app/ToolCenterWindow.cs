using Fowan.ToolCenter.Windows.Application;
using Fowan.ToolCenter.Windows.Presentation;
using Fowan.ToolCenter.Windows.Platform.Windows;
using Microsoft.UI.Xaml;

namespace Fowan.ToolCenter.Windows;

internal sealed class ToolCenterWindow : Window
{
    public ToolCenterWindow(ToolCenterSession session)
    {
        Title = $"Fowan {session.Text("Center_Title")}";
        var picker = new WindowsToolCenterFolderPicker(this);
        var view = new ToolCenterView(session, picker.PickAsync);
        Content = view;
        Closed += (_, _) => { view.Detach(); session.Dispose(); };
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "fowan.ico"));
        ToolCenterWindowActivation.Configure(window: this);
    }
}
