using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fowan.ToolCenter.Windows.Application;
using Fowan.ToolCenter.Windows.Application.Ports;

namespace Fowan.ToolCenter.Windows.Platform.Windows;

internal sealed class GitHubToolReleaseClient : IToolReleaseClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public GitHubToolReleaseClient(HttpClient? http = null)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            { Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task<ToolPackageRelease> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        using var response = await GetAsync(new($"https://api.github.com/repos/{YymPackageIdentity.Repository}/releases/latest"), token);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests ||
            (int)response.StatusCode >= 500)
            return await GetLatestFromPublicReleaseAsync(token);
        response.EnsureSuccessStatusCode();
        try
        {
            using var document = JsonDocument.Parse(await ReadBoundedAsync(response.Content, 512 * 1024, token));
            var release = document.RootElement;
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
                throw new ToolManagementException("Center_InvalidRelease");
            var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
            var version = tag.StartsWith('v') ? tag[1..] : tag;
            var name = $"yym-{version}-win-x64.zip";
            var assets = release.GetProperty("assets").EnumerateArray().ToArray();
            var packages = assets.Where(asset => asset.GetProperty("name").GetString() == name).ToArray();
            var manifests = assets.Where(asset => asset.GetProperty("name").GetString() is { } file &&
                (file == $"yym-{version}-checksums.sha256" || file == "checksums.sha256")).ToArray();
            if (packages.Length != 1 || manifests.Length != 1) throw new ToolManagementException("Center_NoCompatiblePackage");
            var package = packages[0];
            var manifest = manifests[0];
            if ((package.TryGetProperty("state", out var packageState) && packageState.GetString() != "uploaded") ||
                (manifest.TryGetProperty("state", out var manifestState) && manifestState.GetString() != "uploaded"))
                throw new ToolManagementException("Center_NoCompatiblePackage");
            var manifestName = manifest.GetProperty("name").GetString();
            var manifestUri = new Uri(manifest.GetProperty("browser_download_url").GetString()!);
            if (manifestUri.AbsoluteUri != $"https://github.com/{YymPackageIdentity.Repository}/releases/download/{tag}/{manifestName}")
                throw new ToolManagementException("Center_InvalidRelease");
            var candidate = new ToolPackageRelease(version, tag, name,
                new(package.GetProperty("browser_download_url").GetString()!), package.GetProperty("size").GetInt64(),
                new string('0', 64), new(release.GetProperty("html_url").GetString()!));
            YymPackageIdentity.Validate(candidate);
            using var checksumResponse = await GetAsync(manifestUri, token);
            checksumResponse.EnsureSuccessStatusCode();
            var checksumText = Encoding.UTF8.GetString(await ReadBoundedAsync(checksumResponse.Content, 64 * 1024, token));
            var hash = ParseChecksum(checksumText, name);
            if (package.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String &&
                !string.Equals(digest.GetString(), "sha256:" + hash, StringComparison.OrdinalIgnoreCase))
                throw new ToolManagementException("Center_ChecksumMismatch");
            return candidate with { Sha256 = hash };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            KeyNotFoundException or UriFormatException or FormatException)
        {
            throw new ToolManagementException("Center_InvalidRelease", exception);
        }
    }

    private async Task<ToolPackageRelease> GetLatestFromPublicReleaseAsync(CancellationToken token)
    {
        Uri? resolved = null;
        using var page = await GetAsync(new($"https://github.com/{YymPackageIdentity.Repository}/releases/latest"),
            token, HttpMethod.Head, uri => resolved = uri);
        page.EnsureSuccessStatusCode();
        var prefix = $"https://github.com/{YymPackageIdentity.Repository}/releases/tag/";
        if (resolved is null || !resolved.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal))
            throw new ToolManagementException("Center_NoCompatiblePackage");
        var tag = resolved.AbsoluteUri[prefix.Length..];
        var version = tag.StartsWith('v') ? tag[1..] : tag;
        var name = $"yym-{version}-win-x64.zip";
        var candidate = new ToolPackageRelease(version, tag, name,
            new($"https://github.com/{YymPackageIdentity.Repository}/releases/download/{tag}/{name}"), 1, new string('0', 64), resolved);
        YymPackageIdentity.Validate(candidate);
        using var manifest = await GetAsync(new($"https://github.com/{YymPackageIdentity.Repository}/releases/download/{tag}/yym-{version}-checksums.sha256"), token);
        manifest.EnsureSuccessStatusCode();
        var hash = ParseChecksum(Encoding.UTF8.GetString(await ReadBoundedAsync(manifest.Content, 64 * 1024, token)), name);
        using var package = await GetAsync(candidate.DownloadUri, token, HttpMethod.Head);
        package.EnsureSuccessStatusCode();
        if (package.Content.Headers.ContentLength is not { } size) throw new ToolManagementException("Center_InvalidRelease");
        var release = candidate with { Size = size, Sha256 = hash };
        YymPackageIdentity.Validate(release);
        return release;
    }

    private static string ParseChecksum(string text, string name)
    {
        var matches = text.Split('\n').Select(line => Regex.Match(line.Trim(), @"^([a-fA-F0-9]{64})\s+\*?(.+)$"))
            .Where(match => match.Success && match.Groups[2].Value == name).ToArray();
        if (matches.Length != 1) throw new ToolManagementException("Center_InvalidRelease");
        return matches[0].Groups[1].Value.ToLowerInvariant();
    }

    public async Task DownloadAsync(ToolPackageRelease release, string destination,
        IProgress<ToolTransferProgress> progress, CancellationToken cancellationToken)
    {
        YymPackageIdentity.Validate(release);
        using var response = await GetAsync(release.DownloadUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } length && length != release.Size)
            throw new ToolManagementException("Center_DownloadSizeMismatch");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long completed = 0;
        var lastReport = Environment.TickCount64;
        progress.Report(new("Center_Downloading", 0, release.Size));
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            completed += count;
            if (completed > release.Size) throw new ToolManagementException("Center_DownloadSizeMismatch");
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            if (Environment.TickCount64 - lastReport >= 100)
            {
                progress.Report(new("Center_Downloading", completed, release.Size));
                lastReport = Environment.TickCount64;
            }
        }
        if (completed != release.Size) throw new ToolManagementException("Center_DownloadSizeMismatch");
        if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(release.Sha256)))
            throw new ToolManagementException("Center_ChecksumMismatch");
        await output.FlushAsync(cancellationToken);
        progress.Report(new("Center_DownloadVerified", completed, release.Size));
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token, HttpMethod? method = null,
        Action<Uri>? resolvedUri = null)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            ValidateTransportUri(uri);
            using var request = new HttpRequestMessage(method ?? HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Fowan-ToolCenter/1.0");
            if (uri.Host == "api.github.com")
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            }
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new ToolManagementException("Center_InvalidRelease");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            resolvedUri?.Invoke(uri);
            return response;
        }
        throw new ToolManagementException("Center_InvalidRelease");
    }

    internal static void ValidateTransportUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 ||
            uri.Host is not ("github.com" or "api.github.com" or "release-assets.githubusercontent.com" or
                "objects.githubusercontent.com" or "github-releases.githubusercontent.com"))
            throw new ToolManagementException("Center_InvalidRelease");
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit) throw new ToolManagementException("Center_InvalidRelease");
        await using var stream = await content.ReadAsStreamAsync(token);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, token);
            if (count == 0) return result.ToArray();
            if (result.Length + count > limit) throw new ToolManagementException("Center_InvalidRelease");
            result.Write(buffer, 0, count);
        }
    }

    public void Dispose() { if (_ownsClient) _http.Dispose(); }
}
