using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Fowan.ToolCenter.Windows.Application;
using Fowan.ToolCenter.Windows.Application.Ports;
using Fowan.ToolCenter.Windows.Platform.Windows;
using Xunit;

namespace Fowan.Windows.Platform.Tests;

public sealed class ToolCenterManagementTests
{
    [Fact]
    public async Task InspectingAnUninstalledToolDoesNotCreateProgramOrUserDataDirectories()
    {
        using var scenario = new Scenario();
        var state = await scenario.Platform.InspectAsync(default);
        Assert.False(state.IsInstalled);
        Assert.False(state.HasManagedFiles);
        Assert.False(state.HasUserData);
        Assert.False(Directory.Exists(scenario.Platform.DefaultInstallDirectory));
        Assert.False(Directory.Exists(scenario.UserData));
        Assert.False(Directory.Exists(scenario.ManagerRoot));
    }

    [Fact]
    public async Task ExistingUserDataWithoutAnInstallationIsReportedAsCleanupRatherThanUninstall()
    {
        using var scenario = new Scenario();
        scenario.CreateUserData();
        using var session = new ToolCenterSession(new(), new Dictionary<string, string>(),
            releaseClient: scenario.Source, platform: scenario.Platform);
        await session.InitializeAsync();
        Assert.False(session.Management.Installation.IsInstalled);
        Assert.True(session.Management.Installation.HasUserData);
        Assert.True((await session.UninstallAsync(false)).Succeeded);
        Assert.Equal("Center_ResidualCleanupComplete", session.Management.MessageKey);
        Assert.True(Directory.Exists(scenario.UserData));
        Assert.True((await session.UninstallAsync(true)).Succeeded);
        Assert.Equal("Center_UserDataCleanupComplete", session.Management.MessageKey);
        Assert.False(Directory.Exists(scenario.UserData));
        Assert.False(Directory.Exists(scenario.Platform.DefaultInstallDirectory));
    }

    [Fact]
    public async Task SuccessfulUpdatePublishesNewVersionAndCleanupRemovesOnlyTheOldProgram()
    {
        using var scenario = new Scenario();
        var previous = await scenario.InstallAsync();
        scenario.Source.SetPackage(Package(extraName: "yym/new-version.txt"), "0.11.0");
        var current = await scenario.InstallAsync();
        Assert.Equal("0.11.0", current.Version);
        Assert.NotEqual(previous.ExecutablePath, current.ExecutablePath);
        Assert.True(File.Exists(previous.ExecutablePath));
        Assert.True(File.Exists(current.ExecutablePath));
        await scenario.Platform.CleanAsync(default);
        Assert.False(File.Exists(previous.ExecutablePath));
        Assert.True(File.Exists(current.ExecutablePath));
        var inspected = await scenario.Platform.InspectAsync(default);
        Assert.True(inspected.IsHealthy);
        Assert.Equal("0.11.0", inspected.Version);
        Assert.Equal(0, inspected.CacheBytes);
    }

    [Fact]
    public async Task CleanupKeepsTheActiveVersionWhenReceiptGuidCaseDiffersOnWindows()
    {
        using var scenario = new Scenario();
        var installed = await scenario.InstallAsync();
        var receiptPath = Path.Combine(installed.InstallDirectory, "installed.json");
        var receipt = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(receiptPath))!;
        receipt["installId"] = receipt["installId"]!.GetValue<string>().ToUpperInvariant();
        File.WriteAllText(receiptPath, receipt.ToJsonString());
        await scenario.Platform.CleanAsync(default);
        Assert.True(File.Exists(installed.ExecutablePath));
        Assert.True((await scenario.Platform.InspectAsync(default)).IsHealthy);
    }

    [Fact]
    public async Task LocationChangedByAnotherManagerCannotExpandTheConfirmedInstallOrRemovalScope()
    {
        using var scenario = new Scenario();
        var prior = scenario.Platform.DefaultInstallDirectory;
        var parent = Path.Combine(scenario.Root, "new-parent");
        Directory.CreateDirectory(parent);
        await scenario.Platform.ChangeInstallParentAsync(parent, default);
        var error = await Assert.ThrowsAsync<ToolManagementException>(() => scenario.Platform.InstallAsync(
            scenario.Source.Latest, new Progress<ToolTransferProgress>(), default, prior));
        Assert.Equal("Center_InstallationChanged", error.ResourceKey);
        var installed = await scenario.InstallAsync();
        await Assert.ThrowsAsync<ToolManagementException>(() => scenario.Platform.UninstallAsync(false, default, prior));
        Assert.True(File.Exists(installed.ExecutablePath));
    }

    [Fact]
    public async Task GitHubPackageCanBeDownloadedInstalledAndRemovedWithoutLaunching()
    {
        using var scenario = new Scenario();
        var live = Environment.GetEnvironmentVariable("FOWAN_TOOL_CENTER_LIVE_RELEASE") == "1";
        using var http = live ? null : new HttpClient(new ToolCenterReleaseTests.Handler(request =>
            request.RequestUri!.Host == "api.github.com" ? ToolCenterReleaseTests.JsonRelease(scenario.Source.Latest) :
            request.RequestUri.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? ToolCenterReleaseTests.Text(scenario.Source.Latest.Sha256 + "  " + scenario.Source.Latest.AssetName)
                : new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(scenario.Source.Bytes) }));
        using var client = new GitHubToolReleaseClient(http);
        var release = await client.GetLatestAsync(default);
        if (live) Assert.Equal(YymPackageIdentity.InitialRelease.Sha256, release.Sha256);
        var installer = new WindowsToolInstallationPlatform(scenario.ManagerRoot, scenario.UserData, client, scenario.Processes);
        var installation = await installer.InstallAsync(release, new Progress<ToolTransferProgress>(), default);
        Assert.True(installation.IsHealthy);
        Assert.Equal(release.Version, installation.Version);
        Assert.Null(scenario.Processes.LaunchedPath);
        scenario.CreateUserData();
        await installer.UninstallAsync(false, default);
        Assert.False(Directory.Exists(installation.InstallDirectory));
        Assert.True(Directory.Exists(scenario.UserData));
    }

    [Fact]
    public async Task NewerInstalledVersionCannotBeReplacedByOlderPinnedMetadata()
    {
        using var scenario = new Scenario();
        var oldBytes = scenario.Source.Bytes;
        scenario.Source.SetPackage(Package(extraName: "yym/new.txt"), "0.11.0");
        await scenario.InstallAsync();
        scenario.Source.SetPackage(oldBytes);
        var error = await Assert.ThrowsAsync<ToolManagementException>(() => scenario.InstallAsync());
        Assert.Equal("Center_DowngradeBlocked", error.ResourceKey);
        Assert.Equal("0.11.0", (await scenario.Platform.InspectAsync(default)).Version);
    }

    [Fact]
    public async Task PortablePackageLifecyclePreservesRuntimeLicensesAndDataUnlessExplicitlyRemoved()
    {
        using var scenario = new Scenario();
        var fixture = Environment.GetEnvironmentVariable("FOWAN_TOOL_CENTER_PACKAGE_FIXTURE");
        if (!string.IsNullOrEmpty(fixture)) scenario.Source.SetPackage(File.ReadAllBytes(fixture));
        var customParent = Path.Combine(scenario.Root, "custom install location");
        Directory.CreateDirectory(customParent);
        await scenario.Platform.ChangeInstallParentAsync(customParent, default);
        var installed = await scenario.InstallAsync();
        Assert.True(installed.IsHealthy);
        Assert.StartsWith(customParent, installed.InstallDirectory);
        var program = Path.GetDirectoryName(installed.ExecutablePath!)!;
        Assert.True(File.Exists(Path.Combine(program, "_internal", "python312.dll")));
        Assert.True(File.Exists(Path.Combine(program, "_internal", "PySide6", "Qt6Core.dll")));
        Assert.True(File.Exists(Path.Combine(program, "LICENSE")));
        Assert.True(File.Exists(Path.Combine(program, "THIRD_PARTY.md")));
        Assert.True((await scenario.Platform.InspectAsync(default)).CacheBytes > 0);
        await scenario.Platform.LaunchAsync(default);
        Assert.Equal(installed.ExecutablePath, scenario.Processes.LaunchedPath);
        scenario.CreateUserData();
        var external = Path.Combine(scenario.Root, "mumu-data.vdi");
        File.WriteAllText(external, "external input must be preserved");
        await scenario.Platform.CleanAsync(default);
        Assert.True(File.Exists(installed.ExecutablePath));
        Assert.Equal(0, (await scenario.Platform.InspectAsync(default)).CacheBytes);
        await scenario.Platform.UninstallAsync(removeUserData: false, default);
        Assert.False(Directory.Exists(installed.InstallDirectory));
        Assert.True(File.Exists(Path.Combine(scenario.UserData, "account-notes.json")));
        Assert.False((await scenario.Platform.InspectAsync(default)).IsInstalled);
        await scenario.InstallAsync();
        await scenario.Platform.UninstallAsync(removeUserData: true, default);
        Assert.False(Directory.Exists(scenario.UserData));
        Assert.True(File.Exists(external));
    }

    [Fact]
    public async Task FailedReplacementLeavesPreviousReceiptAndExecutableIntact()
    {
        using var scenario = new Scenario();
        var installed = await scenario.InstallAsync();
        var receiptPath = Path.Combine(installed.InstallDirectory, "installed.json");
        var receipt = File.ReadAllText(receiptPath);
        scenario.Source.SetPackage(Package(extraName: "yym/new-version.txt"), "0.11.0");
        scenario.Source.CorruptDownload = true;
        var failure = await Assert.ThrowsAsync<ToolManagementException>(() => scenario.InstallAsync());
        Assert.Equal("Center_ChecksumMismatch", failure.ResourceKey);
        Assert.Equal(receipt, File.ReadAllText(receiptPath));
        Assert.True(File.Exists(installed.ExecutablePath));
        Assert.Equal("0.10.0", (await scenario.Platform.InspectAsync(default)).Version);
        Assert.Empty(Directory.GetFiles(Path.Combine(scenario.ManagerRoot, "cache", "yym"), "*.partial"));
    }

    [Theory]
    [InlineData("../escape.exe")]
    [InlineData("yym/../../escape.exe")]
    [InlineData("C:/escape.exe")]
    [InlineData("yym/yym.exe:payload")]
    [InlineData("yym/CON.txt")]
    [InlineData("yym/aliases./file")]
    [InlineData("yym/YyM.ExE")]
    [InlineData("yym\\escape.exe")]
    public async Task MalformedArchivesNeverPublishAnInstallation(string entry)
    {
        using var scenario = new Scenario();
        scenario.Source.SetPackage(Package(extraName: entry));
        await Assert.ThrowsAnyAsync<Exception>(() => scenario.InstallAsync());
        Assert.False((await scenario.Platform.InspectAsync(default)).IsInstalled);
        Assert.False(File.Exists(Path.Combine(scenario.Root, "escape.exe")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(scenario.Platform.DefaultInstallDirectory, "staging")));
    }

    [Fact]
    public async Task ArchiveSymlinksAndMissingRuntimeAreRejectedBeforePublication()
    {
        using var scenario = new Scenario();
        scenario.Source.SetPackage(Package(extraName: "yym/link", symlink: true));
        await Assert.ThrowsAsync<ToolManagementException>(() => scenario.InstallAsync());
        scenario.Source.SetPackage(Package(omitRuntime: true));
        await Assert.ThrowsAsync<ToolManagementException>(() => scenario.InstallAsync());
        Assert.False((await scenario.Platform.InspectAsync(default)).IsInstalled);
    }

    [Fact]
    public async Task ForeignDirectoryAndTamperedLocationAreNeverAdoptedOrDeleted()
    {
        using var scenario = new Scenario();
        Directory.CreateDirectory(scenario.Platform.DefaultInstallDirectory);
        var foreign = Path.Combine(scenario.Platform.DefaultInstallDirectory, "keep.txt");
        File.WriteAllText(foreign, "foreign data");
        await Assert.ThrowsAsync<ToolManagementException>(() => scenario.InstallAsync());
        await Assert.ThrowsAsync<ToolManagementException>(() => scenario.Platform.UninstallAsync(true, default));
        Assert.Equal("foreign data", File.ReadAllText(foreign));
        var recoveryParent = Path.Combine(scenario.Root, "safe-new-location");
        Directory.CreateDirectory(recoveryParent);
        await scenario.Platform.ChangeInstallParentAsync(recoveryParent, default);
        Assert.Equal(Path.Combine(recoveryParent, "FowanTools", "yym"), (await scenario.Platform.InspectAsync(default)).InstallDirectory);
        Assert.True(File.Exists(foreign));
        Directory.CreateDirectory(scenario.ManagerRoot);
        File.WriteAllText(Path.Combine(scenario.ManagerRoot, "locations.json"),
            "{\"schemaVersion\":1,\"installDirectory\":\"C:\\\\Users\"}");
        await Assert.ThrowsAsync<ToolManagementException>(() => scenario.Platform.InspectAsync(default));
        Assert.True(File.Exists(foreign));
    }

    [Fact]
    public async Task CorruptReceiptIsReportedAndCanBeRepairedOrUninstalledWithinOwnedRoot()
    {
        using var scenario = new Scenario();
        var installed = await scenario.InstallAsync();
        File.WriteAllText(Path.Combine(installed.InstallDirectory, "installed.json"), "{");
        var damaged = await scenario.Platform.InspectAsync(default);
        Assert.True(damaged.IsInstalled);
        Assert.False(damaged.IsHealthy);
        Assert.Equal("Center_ReceiptInvalid", damaged.IssueKey);
        await Assert.ThrowsAsync<ToolManagementException>(() => scenario.Platform.LaunchAsync(default));
        Assert.True((await scenario.InstallAsync()).IsHealthy);
        await scenario.Platform.UninstallAsync(false, default);
        Assert.False(Directory.Exists(installed.InstallDirectory));
    }

    [Fact]
    public async Task RunningManagedToolBlocksMutationAndForeignInstanceBlocksOnlySharedDataRemoval()
    {
        using var scenario = new Scenario();
        var installed = await scenario.InstallAsync();
        scenario.Processes.ManagedRunning = true;
        await Assert.ThrowsAsync<ToolManagementException>(() => scenario.InstallAsync());
        await Assert.ThrowsAsync<ToolManagementException>(() => scenario.Platform.UninstallAsync(false, default));
        Assert.True(File.Exists(installed.ExecutablePath));
        scenario.Processes.ManagedRunning = false;
        scenario.Processes.ForeignRunning = true;
        scenario.CreateUserData();
        await Assert.ThrowsAsync<ToolManagementException>(() => scenario.Platform.UninstallAsync(true, default));
        await scenario.Platform.UninstallAsync(false, default);
        Assert.True(Directory.Exists(scenario.UserData));
    }

    [Fact]
    public async Task ExclusiveLeaseStopsTwoManagersFromChangingOneInstallation()
    {
        using var scenario = new Scenario();
        Directory.CreateDirectory(scenario.ManagerRoot);
        using var held = new FileStream(Path.Combine(scenario.ManagerRoot, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var exception = await Assert.ThrowsAsync<ToolManagementException>(() => scenario.InstallAsync());
        Assert.Equal("Center_OtherManagerBusy", exception.ResourceKey);
    }

    [Fact]
    public async Task CancellationAndConcurrentCommandPreserveStateAndClearPartialDownloads()
    {
        using var scenario = new Scenario();
        var installed = await scenario.InstallAsync();
        scenario.Source.SetPackage(Package(extraName: "yym/update.txt"), "0.11.0");
        scenario.Source.BlockDownload = true;
        using var session = new ToolCenterSession(new(), new Dictionary<string, string>(),
            releaseClient: scenario.Source, platform: scenario.Platform);
        Assert.True((await session.InitializeAsync()).Succeeded);
        Assert.True((await session.CheckUpdatesAsync()).Succeeded);
        var pending = session.InstallAsync();
        await scenario.Source.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Center_Busy", (await session.CleanAsync()).ErrorKey);
        session.CancelCurrentOperation();
        var result = await pending;
        Assert.True(result.Canceled);
        Assert.False(session.Management.IsBusy);
        Assert.False(session.Management.IsError);
        Assert.Equal("0.10.0", session.Management.Installation.Version);
        Assert.True(File.Exists(installed.ExecutablePath));
        Assert.Empty(Directory.GetFiles(Path.Combine(scenario.ManagerRoot, "cache", "yym"), "*.partial"));
    }

    private static byte[] Package(string? extraName = null, bool symlink = false, bool omitRuntime = false)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "yym/yym.exe", "yym/LICENSE", "yym/README.md", "yym/THIRD_PARTY.md",
                         "yym/_internal/python312.dll", "yym/_internal/PySide6/Qt6Core.dll" })
            {
                if (omitRuntime && name.Contains("_internal")) continue;
                using var output = zip.CreateEntry(name).Open();
                output.Write(Encoding.UTF8.GetBytes(name.EndsWith(".exe") ? "MZsynthetic-executable-not-runnable" : "synthetic fixture"));
            }
            if (extraName is not null)
            {
                var entry = zip.CreateEntry(extraName);
                if (symlink) entry.ExternalAttributes = unchecked((int)0xA1FF0000);
                using var output = entry.Open();
                output.Write(Encoding.UTF8.GetBytes("synthetic extra entry"));
            }
        }
        return bytes.ToArray();
    }

    private sealed class Scenario : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "FowanToolCenterTests", Guid.NewGuid().ToString("N"));
        internal string ManagerRoot => Path.Combine(Root, "manager");
        internal string UserData => Path.Combine(Root, "user-data");
        internal FakeSource Source { get; } = new();
        internal FakeProcesses Processes { get; } = new();
        internal WindowsToolInstallationPlatform Platform { get; }
        internal Scenario()
        {
            Directory.CreateDirectory(Root);
            Source.SetPackage(Package());
            Platform = new(ManagerRoot, UserData, Source, Processes);
        }
        internal Task<ToolInstallationSnapshot> InstallAsync() => Platform.InstallAsync(Source.Latest,
            new Progress<ToolTransferProgress>(), default);
        internal void CreateUserData()
        {
            Directory.CreateDirectory(UserData);
            File.WriteAllText(Path.Combine(UserData, "account-notes.json"), "synthetic notes");
        }
        public void Dispose()
        {
            var prefix = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FowanToolCenterTests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(Root).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
            ManagedToolPaths.DeleteTree(Root);
        }
    }

    private sealed class FakeSource : IToolReleaseClient
    {
        internal byte[] Bytes { get; private set; } = [];
        internal ToolPackageRelease Latest { get; private set; } = YymPackageIdentity.InitialRelease;
        internal bool CorruptDownload { get; set; }
        internal bool BlockDownload { get; set; }
        internal TaskCompletionSource DownloadStarted { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void SetPackage(byte[] bytes, string version = "0.10.0")
        {
            Bytes = bytes;
            DownloadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Latest = new(version, "v" + version, $"yym-{version}-win-x64.zip",
                new($"https://github.com/AliangHuang/yym/releases/download/v{version}/yym-{version}-win-x64.zip"),
                bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                new($"https://github.com/AliangHuang/yym/releases/tag/v{version}"));
        }
        public Task<ToolPackageRelease> GetLatestAsync(CancellationToken cancellationToken) => Task.FromResult(Latest);
        public async Task DownloadAsync(ToolPackageRelease release, string destination, IProgress<ToolTransferProgress> progress,
            CancellationToken cancellationToken)
        {
            await File.WriteAllBytesAsync(destination, CorruptDownload ? [1, 2, 3] : Bytes, cancellationToken);
            DownloadStarted.TrySetResult();
            if (BlockDownload) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class FakeProcesses : IManagedToolProcesses
    {
        internal bool ManagedRunning { get; set; }
        internal bool ForeignRunning { get; set; }
        internal string? LaunchedPath { get; private set; }
        public bool IsRunning(string ownedDirectory) => ManagedRunning;
        public bool AnyYymRunning() => ManagedRunning || ForeignRunning;
        public void Launch(string executable) => LaunchedPath = executable;
        public void OpenDirectory(string directory) { }
        public void OpenPage(Uri uri) { }
    }
}
