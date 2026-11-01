using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EMailMPC.Core.Updates;

public sealed record ReleaseUpdateManifest(
    string Version,
    string Url,
    string StableUrl,
    string Sha256,
    long SizeBytes,
    string InstallerUrl,
    string InstallerSha256,
    long InstallerSizeBytes,
    string Channel,
    DateTimeOffset UpdatedAt);

public sealed class VerifiedUpdateService(HttpClient httpClient)
{
    public const string ManifestUrl =
        "https://raw.githubusercontent.com/Hermeneuticus-of-things/e-Mail-MPC/main/release-manifest.json";
    public const string PortableAssetUrl =
        "https://github.com/Hermeneuticus-of-things/e-Mail-MPC/releases/latest/download/program.zip";
    public const string InstallerAssetUrl =
        "https://github.com/Hermeneuticus-of-things/e-Mail-MPC/releases/latest/download/eMail-MPC-windows-x64-setup.exe";

    private const long MaximumInstallerSizeBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumManifestSizeBytes = 64 * 1024;
    private static readonly Uri ManifestUri = new(ManifestUrl);
    private static readonly Regex VersionPattern = new(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ReleaseUpdateManifest> GetManifestAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        using var response = await httpClient.GetAsync(
            ManifestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long contentLength
            && contentLength > MaximumManifestSizeBytes)
        {
            throw new InvalidDataException("The update manifest exceeds the supported size limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var content = new MemoryStream();
        var buffer = new byte[8 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (content.Length + read > MaximumManifestSizeBytes)
            {
                throw new InvalidDataException("The update manifest exceeds the supported size limit.");
            }

            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        content.Position = 0;
        var manifest = await JsonSerializer.DeserializeAsync<ReleaseUpdateManifest>(
            content,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        if (manifest is null || !IsValidManifest(manifest))
        {
            throw new InvalidDataException("The update manifest is incomplete or contains unsupported release metadata.");
        }

        return manifest;
    }

    public async Task<string> DownloadAndVerifyInstallerAsync(
        ReleaseUpdateManifest manifest,
        string stagingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        EnsureValidManifest(manifest);
        return await DownloadAndVerifyAssetAsync(
            manifest.InstallerUrl,
            manifest.InstallerSha256,
            manifest.InstallerSizeBytes,
            Path.Combine(Path.GetFullPath(stagingDirectory), $"eMail-MPC-{manifest.Version}-setup.exe"),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> DownloadAndVerifyPortableArchiveAsync(
        ReleaseUpdateManifest manifest,
        string stagingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        EnsureValidManifest(manifest);
        return await DownloadAndVerifyAssetAsync(
            manifest.Url,
            manifest.Sha256,
            manifest.SizeBytes,
            Path.Combine(Path.GetFullPath(stagingDirectory), $"eMail-MPC-{manifest.Version}-portable.zip"),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> DownloadAndVerifyAssetAsync(
        string assetUrl,
        string expectedHash,
        long expectedSize,
        string stagedPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedPath);
        var stagingDirectory = Path.GetDirectoryName(stagedPath)
            ?? throw new ArgumentException("The staged asset path must have a parent directory.", nameof(stagedPath));
        Directory.CreateDirectory(stagingDirectory);
        var temporaryPath = $"{stagedPath}.{Guid.NewGuid():N}.tmp";

        if (File.Exists(stagedPath)
            && await FileMatchesManifestAsync(stagedPath, expectedSize, expectedHash, cancellationToken).ConfigureAwait(false))
        {
            return stagedPath;
        }

        try
        {
            using var response = await httpClient.GetAsync(
                new Uri(assetUrl),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength
                && contentLength != expectedSize)
            {
                throw new InvalidDataException("The downloaded release asset size does not match the release manifest.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            long totalBytes = 0;
            string actualHash;
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    totalBytes += read;
                    if (totalBytes > expectedSize || totalBytes > MaximumInstallerSizeBytes)
                    {
                        throw new InvalidDataException("The downloaded release asset exceeded its declared size.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                destination.Flush(flushToDisk: true);
                actualHash = Convert.ToHexString(hash.GetHashAndReset());
            }

            if (totalBytes != expectedSize
                || !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash),
                    Convert.FromHexString(expectedHash)))
            {
                throw new InvalidDataException("The downloaded release asset failed its SHA-256 or size check.");
            }

            File.Move(temporaryPath, stagedPath, overwrite: true);
            return stagedPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static bool IsUpdateAvailable(string currentVersion, string availableVersion)
    {
        return Version.TryParse(currentVersion, out var current)
            && Version.TryParse(availableVersion, out var available)
            && available > current;
    }

    private static bool IsValidManifest(ReleaseUpdateManifest manifest)
    {
        return manifest.Version is not null
            && VersionPattern.IsMatch(manifest.Version)
            && Version.TryParse(manifest.Version, out _)
            && manifest.Channel == "stable"
            && string.Equals(manifest.Url, PortableAssetUrl, StringComparison.Ordinal)
            && string.Equals(manifest.StableUrl, PortableAssetUrl, StringComparison.Ordinal)
            && IsValidAssetMetadata(manifest.Sha256, manifest.SizeBytes)
            && string.Equals(manifest.InstallerUrl, InstallerAssetUrl, StringComparison.Ordinal)
            && IsValidAssetMetadata(manifest.InstallerSha256, manifest.InstallerSizeBytes)
            && manifest.UpdatedAt != default;
    }

    private static bool IsValidAssetMetadata(string hash, long size)
    {
        return size is > 0 and <= MaximumInstallerSizeBytes
            && hash is { Length: 64 }
            && hash.All(Uri.IsHexDigit);
    }

    private static void EnsureValidManifest(ReleaseUpdateManifest manifest)
    {
        if (!IsValidManifest(manifest))
        {
            throw new InvalidDataException("The update manifest is incomplete or contains unsupported release metadata.");
        }
    }

    private static async Task<bool> FileMatchesManifestAsync(
        string filePath,
        long expectedSize,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(filePath);
        if (file.Length != expectedSize)
        {
            return false;
        }

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(
            hash,
            Convert.FromHexString(expectedHash));
    }
}
