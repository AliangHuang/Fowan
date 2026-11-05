using System.Collections.Immutable;
using Fowan.ToolCenter.Windows.Application.Ports;

namespace Fowan.ToolCenter.Windows.Application;

internal sealed record ToolCenterPreferences(string Theme = "system", string Language = "system");

internal sealed record ToolCenterApplicationSnapshot(
    string Id, string Name, string Description, string IconGlyph, string CategoryId, string CategoryName,
    bool IsAvailable);

// The session owns catalog and management state; platform ports own no cached business state.
internal sealed class ToolCenterSession : IToolCenterCommands, IDisposable
{
    private readonly IReadOnlyDictionary<string, string> _strings;
    private readonly IToolReleaseClient? _releaseClient;
    private readonly IToolInstallationPlatform? _platform;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _stateGate = new();
    private CancellationTokenSource? _activeOperation;
    private ToolCenterManagementSnapshot _management;
    private long _operationSequence;
    private bool _disposed;

    public ToolCenterSession(ToolCenterPreferences preferences, IReadOnlyDictionary<string, string> strings,
        IEnumerable<ToolCenterApplicationSnapshot>? applications = null,
        IToolReleaseClient? releaseClient = null, IToolInstallationPlatform? platform = null)
    {
        Preferences = preferences;
        _strings = strings.ToImmutableDictionary(StringComparer.Ordinal);
        Applications = (applications ?? [new("yym", Text("Tool_Yym"), Text("Tool_Yym_Description"),
            "\uE8D4", "games", Text("Category_games"), true)]).Where(app => app.IsAvailable).ToImmutableArray();
        _releaseClient = releaseClient;
        _platform = platform;
        _management = new(YymPackageIdentity.InitialRelease,
            new(platform?.DefaultInstallDirectory ?? string.Empty, platform?.UserDataDirectory ?? string.Empty));
    }

    public ToolCenterPreferences Preferences { get; }
    public ImmutableArray<ToolCenterApplicationSnapshot> Applications { get; }
    public string Text(string key) => _strings.TryGetValue(key, out var text) ? text : key;
    public ToolCenterManagementSnapshot Management { get { lock (_stateGate) return _management; } }
    public event EventHandler<ToolCenterManagementSnapshot>? StateChanged;

    public ImmutableArray<ToolCenterApplicationSnapshot> Search(string? searchText, string categoryId = "all")
    {
        var words = (searchText ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return Applications.Where(app =>
            (categoryId == "all" || app.CategoryId == categoryId) &&
            words.All(word => $"{app.Name} {app.Description} {app.Id} {app.CategoryName}"
                .Contains(word, StringComparison.OrdinalIgnoreCase))).ToImmutableArray();
    }

    public Task<ToolOperationResult> InitializeAsync() => RunAsync("Center_ReadingInstallation", async token =>
    {
        await ReadInstallationAsync(token);
        return null;
    });

    public Task<ToolOperationResult> CheckUpdatesAsync() => RunAsync("Center_CheckingUpdates", async token =>
    {
        await ReadInstallationAsync(token);
        var release = await (_releaseClient ?? throw new ToolManagementException("Center_ManagerUnavailable")).GetLatestAsync(token);
        YymPackageIdentity.Validate(release);
        Publish(state => state with { Release = release });
        return "Center_VersionChecked";
    });

    public Task<ToolOperationResult> InstallAsync() => RunAsync("Center_Installing", async token =>
    {
        var release = Management.Release;
        YymPackageIdentity.Validate(release);
        var sequence = Volatile.Read(ref _operationSequence);
        var progress = new Progress<ToolTransferProgress>(value => Publish(state => state.IsBusy && sequence == Volatile.Read(ref _operationSequence)
            ? state with { Progress = value } : state));
        var installed = await Platform.InstallAsync(release, progress, token, Management.Installation.InstallDirectory);
        Publish(state => state with { Installation = installed, IsInitialized = true });
        return "Center_InstallComplete";
    });

    public Task<ToolOperationResult> UninstallAsync(bool removeUserData) => RunAsync(
        Management.Installation.IsInstalled ? "Center_Uninstalling" : "Center_CleaningResiduals", async token =>
    {
        var wasInstalled = Management.Installation.IsInstalled;
        await Platform.UninstallAsync(removeUserData, token, Management.Installation.InstallDirectory);
        await ReadInstallationAsync(CancellationToken.None);
        return wasInstalled ? removeUserData ? "Center_FullUninstallComplete" : "Center_UninstallComplete"
            : removeUserData ? "Center_UserDataCleanupComplete" : "Center_ResidualCleanupComplete";
    });

    public Task<ToolOperationResult> ChangeInstallParentAsync(string parentDirectory) => RunAsync("Center_ChangingLocation", async token =>
    {
        await Platform.ChangeInstallParentAsync(parentDirectory, token);
        await ReadInstallationAsync(token);
        return "Center_LocationChanged";
    });

    public Task<ToolOperationResult> CleanAsync() => RunAsync("Center_Cleaning", async token =>
    {
        await Platform.CleanAsync(token);
        await ReadInstallationAsync(token);
        return "Center_CleanupComplete";
    });

    public Task<ToolOperationResult> LaunchAsync() => RunAsync("Center_Launching", async token =>
    {
        await Platform.LaunchAsync(token);
        await ReadInstallationAsync(token);
        return "Center_LaunchComplete";
    });

    public Task<ToolOperationResult> OpenDirectoryAsync(bool userData) => RunAsync("Center_OpeningDirectory", async token =>
    {
        await Platform.OpenDirectoryAsync(userData, token);
        return null;
    });

    public Task<ToolOperationResult> OpenPageAsync(bool source) => RunAsync("Center_OpeningPage", async token =>
    {
        await Platform.OpenPageAsync(source ? YymPackageIdentity.SourceUri : Management.Release.ReleaseUri, token);
        return null;
    });

    private IToolInstallationPlatform Platform => _platform ?? throw new ToolManagementException("Center_ManagerUnavailable");

    private async Task ReadInstallationAsync(CancellationToken token)
    {
        var installation = await Platform.InspectAsync(token);
        Publish(state => state with { Installation = installation, IsInitialized = true });
    }

    private async Task<ToolOperationResult> RunAsync(string operation, Func<CancellationToken, Task<string?>> action)
    {
        if (!await _operation.WaitAsync(0)) return new(false, ErrorKey: "Center_Busy");
        CancellationTokenSource cancellation;
        lock (_stateGate)
        {
            if (_disposed) { _operation.Release(); return new(false, Canceled: true); }
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _activeOperation = cancellation;
            Interlocked.Increment(ref _operationSequence);
        }
        using var operationCancellation = cancellation;
        Publish(state => state with { IsBusy = true, OperationKey = operation, Progress = null,
            MessageKey = null, ErrorDetail = null, IsError = false });
        try
        {
            var message = await action(cancellation.Token);
            Publish(state => state with { MessageKey = message });
            return new(true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Publish(state => state with { MessageKey = "Center_Canceled" });
            return new(false, Canceled: true);
        }
        catch (Exception exception)
        {
            // Reflect a partially removed or externally changed installation without claiming success.
            try { await ReadInstallationAsync(CancellationToken.None); } catch (Exception) { }
            var key = exception is ToolManagementException failure ? failure.ResourceKey : "Center_OperationFailed";
            Publish(state => state with { MessageKey = key, ErrorDetail = exception is ToolManagementException
                ? exception.InnerException?.Message : exception.Message, IsError = true });
            return new(false, ErrorKey: key);
        }
        finally
        {
            lock (_stateGate) _activeOperation = null;
            Publish(state => state with { IsBusy = false, OperationKey = null, Progress = null });
            _operation.Release();
        }
    }

    private void Publish(Func<ToolCenterManagementSnapshot, ToolCenterManagementSnapshot> update)
    {
        ToolCenterManagementSnapshot snapshot;
        lock (_stateGate) snapshot = _management = update(_management);
        StateChanged?.Invoke(this, snapshot);
    }

    public void CancelCurrentOperation()
    {
        lock (_stateGate) _activeOperation?.Cancel();
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
            (_releaseClient as IDisposable)?.Dispose();
        }
    }
}
