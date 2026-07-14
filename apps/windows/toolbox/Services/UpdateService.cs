using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Fowan.Windows.Services;

public sealed class UpdateService
{
    public const string DefaultManifestUrl = "https://github.com/AliangHuang/Fowan/releases/latest/download/fowan-update.json";
    private const string StableChannel = "stable";
    private const string GitHubHost = "github.com";
    private const string ReleaseDownloadPathPrefix = "/AliangHuang/Fowan/releases/download/";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(20)
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var token = timeout.Token;

            using var request = new HttpRequestMessage(HttpMethod.Get, DefaultManifestUrl);
            request.Headers.UserAgent.ParseAdd("Fowan/1.0");
            using var response = await Http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
            {
                StartupTrace.Mark($"Update check skipped: manifest HTTP {(int)response.StatusCode}");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonOptions, token);
            return TryCreateUpdateInfo(manifest);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            StartupTrace.Mark($"Update check skipped: {exception.GetType().Name}");
            return null;
        }
    }

    public async Task<string> DownloadInstallerAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        var updateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Fowan",
            "Updates",
            update.Version);
        Directory.CreateDirectory(updateRoot);

        var installerUri = new Uri(update.InstallerUrl);
        var fileName = Path.GetFileName(installerUri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            fileName = $"FowanSetup-{update.Version}-win-x64.exe";
        }

        var installerPath = Path.Combine(updateRoot, fileName);
        if (File.Exists(installerPath) &&
            await FileMatchesHashAsync(installerPath, update.InstallerSha256, cancellationToken))
        {
            return installerPath;
        }

        var tempPath = installerPath + ".download";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, update.InstallerUrl);
        request.Headers.UserAgent.ParseAdd("Fowan/1.0");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        if (!await FileMatchesHashAsync(tempPath, update.InstallerSha256, cancellationToken))
        {
            File.Delete(tempPath);
            throw new InvalidOperationException("Downloaded installer checksum did not match the update manifest.");
        }

        if (File.Exists(installerPath))
        {
            File.Delete(installerPath);
        }

        File.Move(tempPath, installerPath);
        return installerPath;
    }

    public static string CurrentVersion()
    {
        var assembly = typeof(UpdateService).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return NormalizeVersionText(informationalVersion);
        }

        var version = assembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : version.Revision > 0 ? version.ToString(4) : version.ToString(3);
    }

    private static UpdateInfo? TryCreateUpdateInfo(UpdateManifest? manifest)
    {
        if (manifest is null ||
            !string.Equals(manifest.Channel, StableChannel, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.InstallerUrl) ||
            string.IsNullOrWhiteSpace(manifest.InstallerSha256))
        {
            StartupTrace.Mark("Update check skipped: invalid manifest");
            return null;
        }

        var remoteVersion = ParseVersion(manifest.Version);
        var currentVersion = ParseVersion(CurrentVersion());
        if (remoteVersion is null || currentVersion is null || remoteVersion <= currentVersion)
        {
            return null;
        }

        if (!IsAllowedGitHubInstallerUrl(manifest.InstallerUrl) ||
            !IsSha256(manifest.InstallerSha256))
        {
            StartupTrace.Mark("Update check skipped: untrusted manifest");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(manifest.ReleaseNotesUrl) &&
            !IsAllowedGitHubUrl(manifest.ReleaseNotesUrl))
        {
            StartupTrace.Mark("Update check skipped: untrusted release notes URL");
            return null;
        }

        return new UpdateInfo(
            NormalizeVersionText(manifest.Version),
            manifest.InstallerUrl.Trim(),
            manifest.InstallerSha256.Trim().ToLowerInvariant(),
            manifest.ReleaseNotesUrl?.Trim() ?? string.Empty,
            manifest.Notes?.Trim() ?? string.Empty);
    }

    private static bool IsAllowedGitHubInstallerUrl(string url)
    {
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(uri.Host, GitHubHost, StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith(ReleaseDownloadPathPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedGitHubUrl(string url)
    {
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(uri.Host, GitHubHost, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSha256(string value)
    {
        var hash = value.Trim();
        return hash.Length == 64 && hash.All(Uri.IsHexDigit);
    }

    private static async Task<bool> FileMatchesHashAsync(string path, string expectedHash, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = NormalizeVersionText(value);
        return Version.TryParse(normalized, out var version)
            ? new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision))
            : null;
    }

    private static string NormalizeVersionText(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var suffixIndex = normalized.IndexOfAny(['+', '-']);
        return suffixIndex > 0 ? normalized[..suffixIndex] : normalized;
    }

    private sealed class UpdateManifest
    {
        public string? Version { get; set; }
        public string? Channel { get; set; }
        public string? InstallerUrl { get; set; }
        public string? InstallerSha256 { get; set; }
        public string? ReleaseNotesUrl { get; set; }
        public string? Notes { get; set; }
    }
}

public sealed record UpdateInfo(
    string Version,
    string InstallerUrl,
    string InstallerSha256,
    string ReleaseNotesUrl,
    string Notes);
