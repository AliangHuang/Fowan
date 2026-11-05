using Fowan.Windows.Services;
using System.Collections.Immutable;

namespace Fowan.Windows.Application;

internal interface IToolboxCommands
{
    void ToggleSidebar();
    bool TogglePinned(string toolId);
    void UpdateSettings(ToolboxSettingsSelection selection);
    void IgnoreUpdate(string version);
    void DisableUpdateChecks();
    void UpdateProfile(string displayName, string avatarPath);
    void AddCapture(string capture);
}

internal sealed record ToolboxSettingsSelection(
    string Theme,
    string Language,
    string CloseBehavior,
    bool UpdateCheckEnabled);

internal sealed record ToolboxSnapshot(
    string Theme,
    string Language,
    string CloseBehavior,
    string UserDisplayName,
    string AvatarPath,
    bool IsProfileInitialized,
    bool IsSidebarCollapsed,
    bool UpdateCheckEnabled,
    string IgnoredUpdateVersion,
    ImmutableArray<string> PinnedToolIds,
    ImmutableArray<string> Captures);

internal sealed class ToolboxSession : IToolboxCommands
{
    private readonly ToolboxSettingsController _settingsController;
    private ClientSettings _settings;
    private ImmutableArray<string> _captures = [];

    public ToolboxSession(IToolboxSettingsRepository repository)
    {
        _settingsController = new ToolboxSettingsController(repository);
        _settings = _settingsController.Load();
        if (_settingsController.Normalize(_settings))
        {
            _settingsController.Save(_settings);
        }
    }

    public ToolboxSnapshot State => new(
        _settings.Theme,
        _settings.Language,
        _settings.CloseBehavior,
        _settings.UserDisplayName,
        _settings.AvatarPath,
        _settings.IsProfileInitialized,
        _settings.IsSidebarCollapsed,
        _settings.UpdateCheckEnabled,
        _settings.IgnoredUpdateVersion,
        _settings.PinnedToolIds.ToImmutableArray(),
        _captures);

    public event EventHandler<ToolboxSnapshot>? StateChanged;

    public void ToggleSidebar() => Update(settings => settings.IsSidebarCollapsed = !settings.IsSidebarCollapsed);

    public bool TogglePinned(string toolId)
    {
        var pinned = !_settings.PinnedToolIds.Contains(toolId, StringComparer.Ordinal);
        Update(settings =>
        {
            settings.PinnedToolIds.RemoveAll(id => string.Equals(id, toolId, StringComparison.Ordinal));
            if (pinned) settings.PinnedToolIds.Insert(0, toolId);
        });
        return pinned;
    }

    public bool ShouldPromptForUpdate(string version) =>
        _settings.UpdateCheckEnabled &&
        !string.Equals(_settings.IgnoredUpdateVersion, version, StringComparison.OrdinalIgnoreCase);

    public void UpdateSettings(ToolboxSettingsSelection selection) => Update(settings =>
    {
        var updateWasEnabled = settings.UpdateCheckEnabled;
        settings.Theme = selection.Theme;
        settings.Language = selection.Language;
        settings.CloseBehavior = selection.CloseBehavior;
        settings.UpdateCheckEnabled = selection.UpdateCheckEnabled;
        if (!selection.UpdateCheckEnabled || (!updateWasEnabled && selection.UpdateCheckEnabled))
        {
            settings.IgnoredUpdateVersion = string.Empty;
        }
    });

    public void IgnoreUpdate(string version) => Update(settings => settings.IgnoredUpdateVersion = version);

    public void DisableUpdateChecks() => Update(settings =>
    {
        settings.UpdateCheckEnabled = false;
        settings.IgnoredUpdateVersion = string.Empty;
    });

    public void UpdateProfile(string displayName, string avatarPath) => Update(settings =>
    {
        settings.UserDisplayName = displayName.Trim();
        settings.AvatarPath = avatarPath;
        settings.IsProfileInitialized = true;
    });

    public void AddCapture(string capture)
    {
        _captures = _captures.Add(capture);
        Publish();
    }

    private void Update(Action<ClientSettings> update)
    {
        var candidate = Clone(_settings);
        update(candidate);
        _settingsController.Normalize(candidate);
        _settingsController.Save(candidate);
        _settings = candidate;
        Publish();
    }

    private void Publish() => StateChanged?.Invoke(this, State);

    private static ClientSettings Clone(ClientSettings value) => new()
    {
        Theme = value.Theme,
        Language = value.Language,
        CloseBehavior = value.CloseBehavior,
        UserDisplayName = value.UserDisplayName,
        AvatarPath = value.AvatarPath,
        IsProfileInitialized = value.IsProfileInitialized,
        IsSidebarCollapsed = value.IsSidebarCollapsed,
        UpdateCheckEnabled = value.UpdateCheckEnabled,
        IgnoredUpdateVersion = value.IgnoredUpdateVersion,
        PinnedToolIds = [.. value.PinnedToolIds]
    };
}
