using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fowan.ToolCenter.Windows.Application;
using Fowan.ToolCenter.Windows.Application.Ports;

namespace Fowan.ToolCenter.Windows.Platform.Windows;

internal sealed class WindowsToolInstallationPlatform : IToolInstallationPlatform
{
    private const string OwnershipFile = ".fowan-tool.json";
    private const string ReceiptFile = "installed.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _dataRoot;
    private readonly string _cacheRoot;
    private readonly IToolReleaseClient _source;
    private readonly IManagedToolProcesses _processes;

    internal WindowsToolInstallationPlatform(string dataRoot, string userDataDirectory,
        IToolReleaseClient source, IManagedToolProcesses processes)
    {
        _dataRoot = ManagedToolPaths.Canonical(dataRoot);
        _cacheRoot = ManagedToolPaths.Child(_dataRoot, "cache/yym");
        UserDataDirectory = ManagedToolPaths.Canonical(userDataDirectory);
        DefaultInstallDirectory = ManagedToolPaths.Child(_dataRoot, "Tools/yym");
        _source = source;
        _processes = processes;
    }

    public string DefaultInstallDirectory { get; }
    public string UserDataDirectory { get; }

    public Task<ToolInstallationSnapshot> InspectAsync(CancellationToken cancellationToken) =>
        Task.Run(Inspect, cancellationToken);

    private ToolInstallationSnapshot Inspect()
    {
        var root = ReadInstallRoot();
        var owned = ValidateRoot(root, create: false);
        var cacheBytes = CacheFiles().Sum(path => new FileInfo(path).Length);
        var initial = new ToolInstallationSnapshot(root, UserDataDirectory, CacheBytes: cacheBytes,
            HasManagedFiles: owned, HasUserData: Directory.Exists(UserDataDirectory));
        if (!owned) return initial;
        var running = _processes.IsRunning(root);
        var receiptPath = ManagedToolPaths.Child(root, ReceiptFile);
        if (!File.Exists(receiptPath)) return initial with { IsRunning = running };
        try
        {
            var receipt = ReadReceipt(root)!;
            var program = ProgramDirectory(root, receipt.InstallId);
            ValidateProgram(program);
            return initial with { IsInstalled = true, IsHealthy = true, IsRunning = running,
                Version = receipt.Version, ExecutablePath = ManagedToolPaths.Child(program, "yym.exe") };
        }
        catch (Exception exception) when (exception is ToolManagementException or IOException or UnauthorizedAccessException)
        {
            return initial with { IsInstalled = true, IsHealthy = false, IsRunning = running,
                IssueKey = exception is ToolManagementException failure ? failure.ResourceKey : "Center_InstallationDamaged" };
        }
    }

    public Task<ToolInstallationSnapshot> InstallAsync(ToolPackageRelease release,
        IProgress<ToolTransferProgress> progress, CancellationToken cancellationToken, string? expectedInstallDirectory = null) => Task.Run(async () =>
    {
        YymPackageIdentity.Validate(release);
        using var lease = AcquireLease();
        var root = ReadInstallRoot();
        ValidateExpectedRoot(root, expectedInstallDirectory);
        ValidateRoot(root, create: true);
        EnsureStopped(root);
        InstallationReceipt? prior = null;
        try { prior = ReadReceipt(root); } catch (ToolManagementException failure) when (failure.ResourceKey == "Center_ReceiptInvalid") { }
        if (YymPackageIdentity.IsNewerVersion(prior?.Version, release.Version))
            throw new ToolManagementException("Center_DowngradeBlocked");
        var package = await GetPackageAsync(release, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var installId = Guid.NewGuid().ToString("N");
        var staging = ManagedToolPaths.Child(root, "staging/" + installId);
        var versionDirectory = ManagedToolPaths.Child(root, "versions/" + installId);
        var committed = false;
        Exception? primaryFailure = null;
        try
        {
            ManagedToolPaths.RejectLinksOnPath(staging);
            Directory.CreateDirectory(staging);
            await ExtractAsync(package, staging, progress, cancellationToken);
            ValidateProgram(ManagedToolPaths.Child(staging, "yym"));
            EnsureStopped(root);
            cancellationToken.ThrowIfCancellationRequested();
            ManagedToolPaths.RejectLinksOnPath(versionDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(versionDirectory)!);
            Directory.Move(staging, versionDirectory);
            var receipt = new InstallationReceipt(1, "yym", YymPackageIdentity.Repository, installId,
                release.Version, release.Sha256.ToLowerInvariant(), DateTimeOffset.UtcNow);
            var installed = new ToolInstallationSnapshot(root, UserDataDirectory, IsInstalled: true, IsHealthy: true,
                Version: release.Version, ExecutablePath: ManagedToolPaths.Child(ProgramDirectory(root, installId), "yym.exe"),
                CacheBytes: CacheFiles().Sum(path => new FileInfo(path).Length), HasManagedFiles: true,
                HasUserData: Directory.Exists(UserDataDirectory));
            WriteAtomic(ManagedToolPaths.Child(root, ReceiptFile), receipt);
            committed = true;
            return installed;
        }
        catch (Exception exception) { primaryFailure = exception; throw; }
        finally
        {
            try
            {
                if (Directory.Exists(staging)) ManagedToolPaths.DeleteTree(staging);
                if (!committed && Directory.Exists(versionDirectory)) ManagedToolPaths.DeleteTree(versionDirectory);
            }
            catch (Exception cleanupFailure)
            {
                if (primaryFailure is not null) throw new AggregateException(primaryFailure, cleanupFailure);
                throw;
            }
        }
    }, cancellationToken);

    private async Task<string> GetPackageAsync(ToolPackageRelease release, IProgress<ToolTransferProgress> progress,
        CancellationToken token)
    {
        ManagedToolPaths.RejectLinksOnPath(_cacheRoot);
        Directory.CreateDirectory(_cacheRoot);
        var cached = ManagedToolPaths.Child(_cacheRoot, release.Sha256.ToLowerInvariant() + ".zip");
        if (File.Exists(cached))
        {
            if (await MatchesAsync(cached, release, token)) return cached;
            File.Delete(cached);
        }
        var partial = ManagedToolPaths.Child(_cacheRoot, release.Sha256.ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N") + ".partial");
        Exception? failure = null;
        try
        {
            await _source.DownloadAsync(release, partial, progress, token);
            progress.Report(new("Center_VerifyingDownload"));
            if (!await MatchesAsync(partial, release, token)) throw new ToolManagementException("Center_ChecksumMismatch");
            File.Move(partial, cached);
            return cached;
        }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            try
            {
                ManagedToolPaths.RejectLinksOnPath(partial);
                if (File.Exists(partial)) File.Delete(partial);
            }
            catch (Exception cleanup)
            {
                if (failure is not null) throw new AggregateException(failure, cleanup);
                throw;
            }
        }
    }

    private static async Task<bool> MatchesAsync(string path, ToolPackageRelease release, CancellationToken token)
    {
        ManagedToolPaths.RejectLinksOnPath(path);
        if (new FileInfo(path).Length != release.Size) return false;
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(input, token), Convert.FromHexString(release.Sha256));
    }

    private static async Task ExtractAsync(string package, string destination, IProgress<ToolTransferProgress> progress,
        CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(package);
        if (archive.Entries.Count is 0 or > 10000) throw new ToolManagementException("Center_InvalidArchive");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (!name.StartsWith("yym/", StringComparison.Ordinal) || name.Contains('\\') || name.Contains("//") ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new ToolManagementException("Center_InvalidArchive");
            var trimmed = name.TrimEnd('/');
            foreach (var segment in trimmed.Split('/')) ManagedToolPaths.ValidateSegment(segment);
            if (!paths.Add(trimmed) || entry.Length < 0 || entry.Length > 512L * 1024 * 1024 ||
                total > 2L * 1024 * 1024 * 1024 - entry.Length)
                throw new ToolManagementException("Center_InvalidArchive");
            _ = ManagedToolPaths.Child(destination, trimmed);
            total += entry.Length;
        }
        long completed = 0;
        var lastReport = Environment.TickCount64;
        var buffer = new byte[81920];
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var path = ManagedToolPaths.Child(destination, entry.FullName.TrimEnd('/'));
            ManagedToolPaths.RejectLinksOnPath(path);
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var input = entry.Open();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            long fileBytes = 0;
            while (true)
            {
                var count = await input.ReadAsync(buffer, token);
                if (count == 0) break;
                fileBytes += count;
                if (fileBytes > entry.Length) throw new ToolManagementException("Center_InvalidArchive");
                await output.WriteAsync(buffer.AsMemory(0, count), token);
                completed += count;
                if (Environment.TickCount64 - lastReport >= 100)
                {
                    progress.Report(new("Center_Extracting", completed, total));
                    lastReport = Environment.TickCount64;
                }
            }
            if (fileBytes != entry.Length) throw new ToolManagementException("Center_InvalidArchive");
        }
        progress.Report(new("Center_Extracting", completed, total));
    }

    private static void ValidateProgram(string program)
    {
        foreach (var relative in new[] { "yym.exe", "LICENSE", "THIRD_PARTY.md", "README.md",
                     "_internal/python312.dll", "_internal/PySide6/Qt6Core.dll" })
        {
            var path = ManagedToolPaths.Child(program, relative);
            ManagedToolPaths.RejectLinksOnPath(path);
            if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new ToolManagementException("Center_InstallationDamaged");
        }
        using var executable = File.OpenRead(ManagedToolPaths.Child(program, "yym.exe"));
        if (executable.ReadByte() != 'M' || executable.ReadByte() != 'Z') throw new ToolManagementException("Center_InstallationDamaged");
    }

    public Task UninstallAsync(bool removeUserData, CancellationToken cancellationToken, string? expectedInstallDirectory = null) => Task.Run(() =>
    {
        using var lease = AcquireLease();
        var root = ReadInstallRoot();
        ValidateExpectedRoot(root, expectedInstallDirectory);
        var owned = ValidateRoot(root, create: false);
        EnsureStopped(root);
        if (removeUserData)
        {
            if (_processes.AnyYymRunning()) throw new ToolManagementException("Center_UserDataInUse");
            ManagedToolPaths.RejectLinksInTree(UserDataDirectory);
        }
        if (owned) ManagedToolPaths.RejectLinksInTree(root);
        cancellationToken.ThrowIfCancellationRequested();
        // Keep the receipt and root ownership marker until every other managed file is gone.
        if (owned)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(root))
            {
                if (Path.GetFileName(path) is OwnershipFile or ReceiptFile) continue;
                ManagedToolPaths.RejectLinksOnPath(path);
                if (Directory.Exists(path)) ManagedToolPaths.DeleteTree(path);
                else File.Delete(path);
            }
            File.Delete(ManagedToolPaths.Child(root, ReceiptFile));
            File.Delete(ManagedToolPaths.Child(root, OwnershipFile));
            Directory.Delete(root);
        }
        ClearCache();
        if (removeUserData) ManagedToolPaths.DeleteTree(UserDataDirectory);
    }, cancellationToken);

    public Task ChangeInstallParentAsync(string parentDirectory, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var lease = AcquireLease();
        var current = ReadInstallRoot();
        var currentOwned = false;
        try { currentOwned = ValidateRoot(current, create: false); }
        catch (ToolManagementException failure) when (failure.ResourceKey == "Center_UnownedDirectory")
        {
            // A user can leave a foreign directory by selecting a new managed location; never adopt or remove it.
        }
        if (currentOwned && Directory.EnumerateFileSystemEntries(current)
                .Any(path => Path.GetFileName(path) != OwnershipFile))
            throw new ToolManagementException("Center_UninstallBeforeLocationChange");
        var parent = ManagedToolPaths.Canonical(parentDirectory);
        ManagedToolPaths.RejectLinksOnPath(parent);
        if (!Directory.Exists(parent)) throw new ToolManagementException("Center_DirectoryMissing");
        var desired = ManagedToolPaths.Child(parent, "FowanTools/yym");
        ValidateInstallRoot(desired);
        ValidateRoot(desired, create: false);
        if (File.Exists(ManagedToolPaths.Child(desired, ReceiptFile))) throw new ToolManagementException("Center_PathAlreadyInstalled");
        cancellationToken.ThrowIfCancellationRequested();
        WriteAtomic(ManagedToolPaths.Child(_dataRoot, "locations.json"), new LocationSettings(1, desired));
    }, cancellationToken);

    public Task CleanAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var lease = AcquireLease();
        var root = ReadInstallRoot();
        var owned = ValidateRoot(root, create: false);
        EnsureStopped(root);
        cancellationToken.ThrowIfCancellationRequested();
        ClearCache();
        if (!owned) return;
        var receipt = ReadReceipt(root);
        foreach (var section in new[] { "staging", "versions" })
        {
            var parent = ManagedToolPaths.Child(root, section);
            ManagedToolPaths.RejectLinksInTree(parent);
            if (!Directory.Exists(parent)) continue;
            foreach (var directory in Directory.EnumerateDirectories(parent))
                if (Guid.TryParseExact(Path.GetFileName(directory), "N", out _) &&
                    (section == "staging" || !string.Equals(Path.GetFileName(directory), receipt?.InstallId, StringComparison.OrdinalIgnoreCase)))
                    ManagedToolPaths.DeleteTree(directory);
        }
    }, cancellationToken);

    public Task LaunchAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var lease = AcquireLease();
        var installation = Inspect();
        if (!installation.IsInstalled || !installation.IsHealthy || installation.ExecutablePath is null)
            throw new ToolManagementException("Center_InstallationDamaged");
        if (installation.IsRunning) throw new ToolManagementException("Center_AlreadyRunning");
        cancellationToken.ThrowIfCancellationRequested();
        _processes.Launch(installation.ExecutablePath);
    }, cancellationToken);

    public Task OpenDirectoryAsync(bool userData, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var root = ReadInstallRoot();
        if (!userData && !ValidateRoot(root, create: false)) throw new ToolManagementException("Center_DirectoryMissing");
        _processes.OpenDirectory(userData ? UserDataDirectory : root);
    }, cancellationToken);

    public Task OpenPageAsync(Uri uri, CancellationToken cancellationToken) =>
        Task.Run(() => _processes.OpenPage(uri), cancellationToken);

    private string ReadInstallRoot()
    {
        var path = ManagedToolPaths.Child(_dataRoot, "locations.json");
        ManagedToolPaths.RejectLinksOnPath(path);
        if (!File.Exists(path)) return DefaultInstallDirectory;
        var settings = ReadJson<LocationSettings>(path, "Center_LocationSettingsInvalid");
        if (settings.SchemaVersion != 1) throw new ToolManagementException("Center_LocationSettingsInvalid");
        var root = ManagedToolPaths.Canonical(settings.InstallDirectory);
        ValidateInstallRoot(root);
        return root;
    }

    private void ValidateInstallRoot(string root)
    {
        ManagedToolPaths.RejectLinksOnPath(root);
        if (new DriveInfo(Path.GetPathRoot(root)!).DriveType == DriveType.Network)
            throw new ToolManagementException("Center_UnsafePath");
        if (!string.Equals(root, DefaultInstallDirectory, StringComparison.OrdinalIgnoreCase) &&
            (Path.GetFileName(root) != "yym" || Path.GetFileName(Path.GetDirectoryName(root)) != "FowanTools"))
            throw new ToolManagementException("Center_UnsafePath");
        if (Contains(root, _dataRoot) || Contains(root, UserDataDirectory) || Contains(UserDataDirectory, root))
            throw new ToolManagementException("Center_UnsafePath");
    }

    private static bool Contains(string parent, string child) => string.Equals(parent, child, StringComparison.OrdinalIgnoreCase) ||
        child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void ValidateExpectedRoot(string root, string? expected)
    {
        if (expected is not null && !string.Equals(root, ManagedToolPaths.Canonical(expected), StringComparison.OrdinalIgnoreCase))
            throw new ToolManagementException("Center_InstallationChanged");
    }

    private bool ValidateRoot(string root, bool create)
    {
        ValidateInstallRoot(root);
        if (File.Exists(root)) throw new ToolManagementException("Center_UnownedDirectory");
        var marker = ManagedToolPaths.Child(root, OwnershipFile);
        ManagedToolPaths.RejectLinksOnPath(marker);
        if (File.Exists(marker))
        {
            var ownership = ReadJson<Ownership>(marker, "Center_UnownedDirectory");
            if (ownership.SchemaVersion != 1 || ownership.ToolId != "yym" || ownership.Repository != YymPackageIdentity.Repository ||
                !string.Equals(ownership.Root, root, StringComparison.OrdinalIgnoreCase))
                throw new ToolManagementException("Center_UnownedDirectory");
            return true;
        }
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new ToolManagementException("Center_UnownedDirectory");
        if (!create) return false;
        Directory.CreateDirectory(root);
        WriteAtomic(marker, new Ownership(1, "yym", YymPackageIdentity.Repository, root));
        return true;
    }

    private InstallationReceipt? ReadReceipt(string root)
    {
        var path = ManagedToolPaths.Child(root, ReceiptFile);
        ManagedToolPaths.RejectLinksOnPath(path);
        if (!File.Exists(path)) return null;
        var receipt = ReadJson<InstallationReceipt>(path, "Center_ReceiptInvalid");
        if (receipt.SchemaVersion != 1 || receipt.ToolId != "yym" || receipt.Repository != YymPackageIdentity.Repository ||
            !Guid.TryParseExact(receipt.InstallId, "N", out _) || string.IsNullOrWhiteSpace(receipt.Version) ||
            !Regex.IsMatch(receipt.Version, @"^\d+\.\d+\.\d+(?:[+-][A-Za-z0-9.-]+)?$") ||
            receipt.Sha256 is null || !Regex.IsMatch(receipt.Sha256, "^[a-fA-F0-9]{64}$"))
            throw new ToolManagementException("Center_ReceiptInvalid");
        return receipt;
    }

    private static string ProgramDirectory(string root, string installId) => ManagedToolPaths.Child(root, $"versions/{installId}/yym");

    private FileStream AcquireLease()
    {
        ManagedToolPaths.RejectLinksOnPath(_dataRoot);
        Directory.CreateDirectory(_dataRoot);
        var path = ManagedToolPaths.Child(_dataRoot, "operation.lock");
        ManagedToolPaths.RejectLinksOnPath(path);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception) { throw new ToolManagementException("Center_OtherManagerBusy", exception); }
    }

    private void EnsureStopped(string root)
    {
        if (_processes.IsRunning(root)) throw new ToolManagementException("Center_ToolRunning");
    }

    private string[] CacheFiles()
    {
        ManagedToolPaths.RejectLinksOnPath(_cacheRoot);
        if (!Directory.Exists(_cacheRoot)) return [];
        var files = Directory.EnumerateFiles(_cacheRoot).Where(path =>
            Regex.IsMatch(Path.GetFileName(path), @"^[a-f0-9]{64}(?:\.zip|-[a-f0-9]{32}\.partial)$")).ToArray();
        foreach (var path in files) ManagedToolPaths.RejectLinksOnPath(path);
        return files;
    }

    private void ClearCache() { foreach (var path in CacheFiles()) File.Delete(path); }

    private static T ReadJson<T>(string path, string errorKey)
    {
        ManagedToolPaths.RejectLinksOnPath(path);
        if (new FileInfo(path).Length > 64 * 1024) throw new ToolManagementException(errorKey);
        try { return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), JsonOptions) ?? throw new ToolManagementException(errorKey); }
        catch (JsonException exception) { throw new ToolManagementException(errorKey, exception); }
    }

    private static void WriteAtomic<T>(string path, T value)
    {
        ManagedToolPaths.RejectLinksOnPath(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                JsonSerializer.Serialize(output, value, JsonOptions);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (created && File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record Ownership(int SchemaVersion, string ToolId, string Repository, string Root);
    private sealed record LocationSettings(int SchemaVersion, string InstallDirectory);
    private sealed record InstallationReceipt(int SchemaVersion, string ToolId, string Repository, string InstallId,
        string Version, string Sha256, DateTimeOffset InstalledAtUtc);
}
