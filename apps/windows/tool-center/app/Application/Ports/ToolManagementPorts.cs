namespace Fowan.ToolCenter.Windows.Application.Ports;

internal interface IToolReleaseClient
{
    Task<ToolPackageRelease> GetLatestAsync(CancellationToken cancellationToken);
    Task DownloadAsync(ToolPackageRelease release, string destination,
        IProgress<ToolTransferProgress> progress, CancellationToken cancellationToken);
}

internal interface IToolInstallationPlatform
{
    string DefaultInstallDirectory { get; }
    string UserDataDirectory { get; }
    Task<ToolInstallationSnapshot> InspectAsync(CancellationToken cancellationToken);
    Task<ToolInstallationSnapshot> InstallAsync(ToolPackageRelease release,
        IProgress<ToolTransferProgress> progress, CancellationToken cancellationToken, string? expectedInstallDirectory = null);
    Task UninstallAsync(bool removeUserData, CancellationToken cancellationToken, string? expectedInstallDirectory = null);
    Task ChangeInstallParentAsync(string parentDirectory, CancellationToken cancellationToken);
    Task CleanAsync(CancellationToken cancellationToken);
    Task LaunchAsync(CancellationToken cancellationToken);
    Task OpenDirectoryAsync(bool userData, CancellationToken cancellationToken);
    Task OpenPageAsync(Uri uri, CancellationToken cancellationToken);
}

internal interface IToolCenterCommands
{
    Task<ToolOperationResult> InitializeAsync();
    Task<ToolOperationResult> CheckUpdatesAsync();
    Task<ToolOperationResult> InstallAsync();
    Task<ToolOperationResult> UninstallAsync(bool removeUserData);
    Task<ToolOperationResult> ChangeInstallParentAsync(string parentDirectory);
    Task<ToolOperationResult> CleanAsync();
    Task<ToolOperationResult> LaunchAsync();
    Task<ToolOperationResult> OpenDirectoryAsync(bool userData);
    Task<ToolOperationResult> OpenPageAsync(bool source);
    void CancelCurrentOperation();
}
