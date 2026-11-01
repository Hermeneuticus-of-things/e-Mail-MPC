using System.Net;
using System.Security.Cryptography;
using System.Text;
using EMailMPC.Core.Updates;

namespace EMailMPC.Core.Tests.Updates;

public sealed class VerifiedUpdateServiceTests
{
    [Fact]
    public async Task DownloadsAndStagesInstallerOnlyWhenSizeAndHashMatchManifest()
    {
        using var directory = new TemporaryDirectory();
        var installer = Encoding.UTF8.GetBytes("verified installer payload");
        using var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(installer)
        }));
        var service = new VerifiedUpdateService(client);
        var manifest = CreateManifest(installer);

        var installerPath = await service.DownloadAndVerifyInstallerAsync(manifest, directory.Path);

        Assert.Equal(installer, await File.ReadAllBytesAsync(installerPath));
        Assert.Equal("eMail-MPC-1.2.3-setup.exe", Path.GetFileName(installerPath));
    }

    [Fact]
    public async Task RejectsInstallerWithWrongHashWithoutLeavingStagedFile()
    {
        using var directory = new TemporaryDirectory();
        var installer = Encoding.UTF8.GetBytes("tampered installer");
        using var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(installer)
        }));
        var service = new VerifiedUpdateService(client);
        var manifest = CreateManifest(Encoding.UTF8.GetBytes("expected installer"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.DownloadAndVerifyInstallerAsync(manifest, directory.Path));

        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task RejectsManifestWithNonOfficialInstallerUrl()
    {
        var json = $$"""
            {
              "version": "1.2.3",
              "url": "{{VerifiedUpdateService.PortableAssetUrl}}",
              "stableUrl": "{{VerifiedUpdateService.PortableAssetUrl}}",
              "sha256": "{{new string('a', 64)}}",
              "sizeBytes": 12,
              "installerUrl": "https://attacker.example/setup.exe",
              "installerSha256": "{{new string('a', 64)}}",
              "installerSizeBytes": 12,
              "channel": "stable",
              "updatedAt": "2026-10-28T00:00:00Z"
            }
            """;
        using var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        var service = new VerifiedUpdateService(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetManifestAsync());
    }

    [Fact]
    public async Task ReadsManifestFromPinnedOfficialEndpoint()
    {
        var installer = Encoding.UTF8.GetBytes("expected installer");
        var manifest = CreateManifest(installer);
        var json = $$"""
            {
              "version": "{{manifest.Version}}",
              "url": "{{manifest.Url}}",
              "stableUrl": "{{manifest.StableUrl}}",
              "sha256": "{{manifest.Sha256}}",
              "sizeBytes": {{manifest.SizeBytes}},
              "installerUrl": "{{manifest.InstallerUrl}}",
              "installerSha256": "{{manifest.InstallerSha256}}",
              "installerSizeBytes": {{manifest.InstallerSizeBytes}},
              "channel": "{{manifest.Channel}}",
              "updatedAt": "{{manifest.UpdatedAt:O}}"
            }
            """;
        Uri? requestedUri = null;
        using var client = CreateClient((request, _) =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });
        var service = new VerifiedUpdateService(client);

        var result = await service.GetManifestAsync();

        Assert.Equal(VerifiedUpdateService.ManifestUrl, requestedUri?.ToString());
        Assert.Equal(manifest, result);
    }

    [Fact]
    public async Task DownloadsAndStagesPortableArchiveUsingManifestIntegrityMetadata()
    {
        using var directory = new TemporaryDirectory();
        var archive = Encoding.UTF8.GetBytes("verified portable archive");
        using var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(archive)
        }));
        var service = new VerifiedUpdateService(client);
        var manifest = CreateManifest(archive);

        var archivePath = await service.DownloadAndVerifyPortableArchiveAsync(manifest, directory.Path);

        Assert.Equal(archive, await File.ReadAllBytesAsync(archivePath));
        Assert.Equal("eMail-MPC-1.2.3-portable.zip", Path.GetFileName(archivePath));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.4", true)]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.3.0", "1.2.9", false)]
    [InlineData("development", "1.2.4", false)]
    public void ComparesVersionsWithoutTreatingOlderReleaseAsAnUpdate(
        string currentVersion,
        string availableVersion,
        bool expected)
    {
        Assert.Equal(expected, VerifiedUpdateService.IsUpdateAvailable(currentVersion, availableVersion));
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        return new HttpClient(new StubHttpMessageHandler(send));
    }

    private static ReleaseUpdateManifest CreateManifest(byte[] installer)
    {
        return new ReleaseUpdateManifest(
            "1.2.3",
            VerifiedUpdateService.PortableAssetUrl,
            VerifiedUpdateService.PortableAssetUrl,
            Convert.ToHexString(SHA256.HashData(installer)),
            installer.Length,
            VerifiedUpdateService.InstallerAssetUrl,
            Convert.ToHexString(SHA256.HashData(installer)),
            installer.Length,
            "stable",
            DateTimeOffset.UtcNow);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return send(request, cancellationToken);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
