using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fowan.ToolCenter.Windows.Application;
using Fowan.ToolCenter.Windows.Platform.Windows;
using Xunit;

namespace Fowan.Windows.Platform.Tests;

public sealed class ToolCenterReleaseTests
{
    [Fact]
    public async Task AnonymousApiRateLimitUsesOnlyThePublicReleaseAndChecksumWithoutDownloadingThePackage()
    {
        var release = YymPackageIdentity.InitialRelease;
        var binaryRequests = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            var uri = request.RequestUri!;
            if (uri.Host == "api.github.com") return new(HttpStatusCode.Forbidden);
            if (uri.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal))
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = release.ReleaseUri;
                return redirect;
            }
            if (uri == release.ReleaseUri) return new(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            if (uri.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)) return Text(release.Sha256 + "  " + release.AssetName);
            binaryRequests++;
            Assert.Equal(HttpMethod.Head, request.Method);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentLength = release.Size;
            return response;
        }));
        using var client = new GitHubToolReleaseClient(http);
        Assert.Equal(release, await client.GetLatestAsync(default));
        Assert.Equal(1, binaryRequests);
    }

    [Fact]
    public async Task ReleaseSelectionUsesOnlyThePortableAssetAndMatchingChecksum()
    {
        var release = YymPackageIdentity.InitialRelease;
        using var http = new HttpClient(new Handler(request => request.RequestUri!.Host == "api.github.com"
            ? JsonRelease(release) : Text(release.Sha256 + "  " + release.AssetName + "\n")));
        using var client = new GitHubToolReleaseClient(http);
        Assert.Equal(release, await client.GetLatestAsync(default));
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("prerelease")]
    [InlineData("wrong-url")]
    [InlineData("source-only")]
    [InlineData("duplicate")]
    [InlineData("digest-mismatch")]
    [InlineData("oversize")]
    public async Task IncompatibleOrAmbiguousReleaseMetadataCannotBecomeInstallable(string fault)
    {
        using var http = new HttpClient(new Handler(request => request.RequestUri!.Host == "api.github.com"
            ? JsonRelease(YymPackageIdentity.InitialRelease, fault) : Text(YymPackageIdentity.InitialRelease.Sha256 +
                "  " + YymPackageIdentity.InitialRelease.AssetName)));
        using var client = new GitHubToolReleaseClient(http);
        await Assert.ThrowsAsync<ToolManagementException>(() => client.GetLatestAsync(default));
    }

    [Fact]
    public async Task ChecksumManifestMustIdentifyTheExactAssetExactlyOnce()
    {
        var release = YymPackageIdentity.InitialRelease;
        var line = release.Sha256 + "  " + release.AssetName;
        using var http = new HttpClient(new Handler(request => request.RequestUri!.Host == "api.github.com"
            ? JsonRelease(release) : Text(line + "\n" + line)));
        using var client = new GitHubToolReleaseClient(http);
        await Assert.ThrowsAsync<ToolManagementException>(() => client.GetLatestAsync(default));
    }

    [Theory]
    [InlineData("http://github.com/AliangHuang/yym")]
    [InlineData("https://github.com.evil.example/file")]
    [InlineData("https://example.com/file")]
    [InlineData("https://github.com:8443/file")]
    [InlineData("https://user:password@github.com/file")]
    public void TransportRejectsUnapprovedLocations(string uri) => Assert.Throws<ToolManagementException>(() =>
        GitHubToolReleaseClient.ValidateTransportUri(new(uri)));

    [Fact]
    public async Task RedirectCannotSendASecondRequestToAnUnapprovedHost()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new("https://unapproved.example/package");
            return response;
        }));
        using var client = new GitHubToolReleaseClient(http);
        await Assert.ThrowsAsync<ToolManagementException>(() => client.GetLatestAsync(default));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamingDownloadRejectsChangedOrTruncatedContent(bool truncate)
    {
        byte[] expected = [1, 2, 3, 4];
        var release = YymPackageIdentity.InitialRelease with { Size = expected.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant() };
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
            { Content = new ByteArrayContent(truncate ? [1, 2] : [9, 9, 9, 9]) }));
        using var client = new GitHubToolReleaseClient(http);
        var root = Path.Combine(Path.GetTempPath(), "FowanDownloadTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await Assert.ThrowsAsync<ToolManagementException>(() => client.DownloadAsync(release,
                Path.Combine(root, "download.partial"), new Progress<ToolTransferProgress>(), default));
        }
        finally
        {
            var expectedPrefix = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FowanDownloadTests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
            ManagedToolPaths.DeleteTree(root);
        }
    }

    internal static HttpResponseMessage JsonRelease(ToolPackageRelease release, string? fault = null)
    {
        object Asset(string name, string url, long size, string digest) => new
            { name, browser_download_url = url, size, digest, state = "uploaded" };
        var portable = Asset(fault == "source-only" ? "yym-source.zip" : release.AssetName,
            fault == "wrong-url" ? "https://unapproved.example/file.zip" : release.DownloadUri.AbsoluteUri,
            fault == "oversize" ? YymPackageIdentity.MaximumPackageBytes + 1 : release.Size,
            "sha256:" + (fault == "digest-mismatch" ? new string('0', 64) : release.Sha256));
        var checksum = Asset($"yym-{release.Version}-checksums.sha256",
            $"https://github.com/AliangHuang/yym/releases/download/{release.Tag}/yym-{release.Version}-checksums.sha256", 180, "");
        object[] assets = fault == "duplicate" ? [portable, portable, checksum] :
            [Asset("yym-source.zip", "https://example.com/unused-source.zip", 100, ""), portable, checksum];
        return Text(JsonSerializer.Serialize(new
        {
            draft = fault == "draft", prerelease = fault == "prerelease", tag_name = release.Tag,
            html_url = release.ReleaseUri.AbsoluteUri, assets
        }));
    }

    internal static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK)
        { Content = new StringContent(text, Encoding.UTF8) };

    internal sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
