using Fowan.Windows.Models;
using Fowan.Windows.Platform.Contracts;
using Fowan.Windows.Platform.Windows;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Windows.Coordination;

internal sealed class ToolLaunchCoordinator(
    IProcessLauncher processLauncher,
    Func<string, string> localize,
    Action<string, InfoBarSeverity> showInfo,
    Action<ToolCard> selectTool,
    Func<Task> showQuickCapture,
    Func<Task> showSettings,
    Action showToolboxHome,
    Action minimizeToTray)
{
    private const int DoubleClickMilliseconds = 500;
    private const int LaunchDebounceMilliseconds = 700;
    private string _lastClickId = string.Empty;
    private DateTimeOffset _lastClickAt = DateTimeOffset.MinValue;
    private string _lastLaunchId = string.Empty;
    private DateTimeOffset _lastLaunchAt = DateTimeOffset.MinValue;

    public async Task HandleClickAsync(ToolCard tool)
    {
        var now = DateTimeOffset.UtcNow;
        var doubleClick = string.Equals(_lastClickId, tool.Id, StringComparison.OrdinalIgnoreCase) &&
            now - _lastClickAt <= TimeSpan.FromMilliseconds(DoubleClickMilliseconds);
        _lastClickId = tool.Id;
        _lastClickAt = now;
        selectTool(tool);
        if (doubleClick) await ExecuteAsync(tool);
    }

    public async Task ExecuteAsync(ToolCard tool)
    {
        if (tool.Status != ToolStatus.Available) return;
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(_lastLaunchId, tool.Id, StringComparison.OrdinalIgnoreCase) &&
            now - _lastLaunchAt <= TimeSpan.FromMilliseconds(LaunchDebounceMilliseconds)) return;
        _lastLaunchId = tool.Id;
        _lastLaunchAt = now;
        var opened = tool.Id switch
        {
            "todo" => Launch(ToolExecutableResolver.ResolveTodo(), "Tool_Todo"),
            "diary" => Launch(ToolExecutableResolver.ResolveDiary(), "Tool_Diary"),
            "report" => Launch(ToolExecutableResolver.ResolveReport(), "Tool_Report"),
            "ai-chat" => Launch(ToolExecutableResolver.ResolveAi(ToolExecutableResolver.AiChatExecutableName, "Chat"), "Tool_AIChat"),
            "ai-config" => Launch(ToolExecutableResolver.ResolveAi(ToolExecutableResolver.AiConfigExecutableName, "Config"), "Tool_AIConfig"),
            _ => false
        };
        switch (tool.Id)
        {
            case "quick-capture": await showQuickCapture(); break;
            case "settings": await showSettings(); break;
            case "diagnostics": selectTool(tool); break;
            case "toolbox-home": showToolboxHome(); break;
        }
        if (opened) minimizeToTray();
    }

    private bool Launch(string? executablePath, string toolNameKey)
    {
        if (executablePath is null)
        {
            showInfo(string.Format(localize("Tool_LaunchMissing"), localize(toolNameKey)), InfoBarSeverity.Error);
            return false;
        }
        var result = processLauncher.Launch(new ProcessLaunchRequest(executablePath));
        if (result.Succeeded) return true;
        showInfo(string.Format(localize("Tool_LaunchFailed"), localize(toolNameKey), result.Error), InfoBarSeverity.Error);
        return false;
    }
}
