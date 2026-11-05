using System.Text.RegularExpressions;

namespace Fowan.ToolCenter.Windows.Application;

internal sealed record ToolPackageRelease(
    string Version, string Tag, string AssetName, Uri DownloadUri, long Size, string Sha256, Uri ReleaseUri);

internal static class YymPackageIdentity
{
    internal const string Repository = "AliangHuang/yym";
    internal const long MaximumPackageBytes = 512L * 1024 * 1024;
    internal static Uri SourceUri { get; } = new("https://github.com/AliangHuang/yym");
    internal static ToolPackageRelease InitialRelease { get; } = new(
        "0.10.0", "v0.10.0", "yym-0.10.0-win-x64.zip",
        new("https://github.com/AliangHuang/yym/releases/download/v0.10.0/yym-0.10.0-win-x64.zip"),
        45894206, "09a9baa38dc4e8dbc7b8f3ca9bbafb76ceedec1a820f510155d83cebf129f99c",
        new("https://github.com/AliangHuang/yym/releases/tag/v0.10.0"));

    internal static void Validate(ToolPackageRelease release)
    {
        if (string.IsNullOrWhiteSpace(release.Version) || !Regex.IsMatch(release.Version, @"^\d+\.\d+\.\d+(?:[+-][A-Za-z0-9.-]+)?$") ||
            release.Version.Length > 80 || (release.Tag != release.Version && release.Tag != "v" + release.Version) ||
            release.AssetName != $"yym-{release.Version}-win-x64.zip" ||
            release.Size is <= 0 or > MaximumPackageBytes ||
            string.IsNullOrWhiteSpace(release.Sha256) || !Regex.IsMatch(release.Sha256, "^[a-fA-F0-9]{64}$") ||
            !release.DownloadUri.IsAbsoluteUri || !release.ReleaseUri.IsAbsoluteUri ||
            release.DownloadUri.AbsoluteUri != $"https://github.com/{Repository}/releases/download/{release.Tag}/{release.AssetName}" ||
            release.ReleaseUri.AbsoluteUri != $"https://github.com/{Repository}/releases/tag/{release.Tag}")
            throw new ToolManagementException("Center_InvalidRelease");
    }

    internal static bool IsNewerVersion(string? installed, string available) =>
        Version.TryParse(installed?.Split(['-', '+'])[0], out var current) &&
        Version.TryParse(available.Split(['-', '+'])[0], out var candidate) && current > candidate;
}

internal sealed record ToolTransferProgress(string StageKey, long CompletedBytes = 0, long TotalBytes = 0);

internal sealed record ToolInstallationSnapshot(
    string InstallDirectory, string UserDataDirectory, bool IsInstalled = false, bool IsHealthy = false,
    bool IsRunning = false, string? Version = null, string? ExecutablePath = null, long CacheBytes = 0,
    string? IssueKey = null, bool HasManagedFiles = false, bool HasUserData = false);

internal sealed record ToolCenterManagementSnapshot(
    ToolPackageRelease Release, ToolInstallationSnapshot Installation, bool IsInitialized = false,
    bool IsBusy = false, string? OperationKey = null, ToolTransferProgress? Progress = null,
    string? MessageKey = null, string? ErrorDetail = null, bool IsError = false);

internal sealed record ToolOperationResult(bool Succeeded, bool Canceled = false, string? ErrorKey = null);

internal sealed class ToolManagementException(string resourceKey, Exception? inner = null)
    : Exception(resourceKey, inner)
{
    internal string ResourceKey { get; } = resourceKey;
}
