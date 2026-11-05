using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EMailMPC.Core.Storage;

[SupportedOSPlatform("windows")]
public sealed class LocalMailBackupService
{
    private const int FormatVersion = 1;
    private const string DatabaseEntry = "mail.db";
    private const string KeyEntry = "database.dpapi";
    private const string ManifestEntry = "manifest.json";
    private const long MaximumArchiveBytes = 20L * 1024 * 1024 * 1024;
    private const int MaximumAccountSecretFiles = 1000;
    private static readonly Regex MigrationSnapshotNamePattern = new(
        @"^mail\.db\.pre-migration-v[0-9]+-[0-9]{17}-[0-9a-f]{32}\.db$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public async Task CreateAsync(
        EncryptedMailDatabase database,
        string keyPath,
        string accountSecretsDirectory,
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountSecretsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);

        var targetPath = Path.GetFullPath(backupPath);
        var targetDirectory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDirectory);
        var workingDirectory = Path.Combine(targetDirectory, $".emailmpc-backup-{Guid.NewGuid():N}");
        var temporaryArchive = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var snapshotPath = Path.Combine(workingDirectory, DatabaseEntry);
            database.CreateEncryptedSnapshot(snapshotPath);
            cancellationToken.ThrowIfCancellationRequested();

            var keySourcePath = Path.GetFullPath(keyPath);
            if (!File.Exists(keySourcePath))
            {
                throw new FileNotFoundException("The protected database key was not found.", keySourcePath);
            }

            var secretsSourceDirectory = Path.GetFullPath(accountSecretsDirectory);
            var secretFiles = Directory.Exists(secretsSourceDirectory)
                ? Directory.GetFiles(secretsSourceDirectory, "*.dpapi", SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                    .ToArray()
                : [];
            if (secretFiles.Length > MaximumAccountSecretFiles
                || secretFiles.Any(path => !IsProtectedSecretFile(Path.GetFileName(path))))
            {
                throw new InvalidDataException("The account-secret directory contains an unsupported file layout.");
            }

            var keyCopyPath = Path.Combine(workingDirectory, KeyEntry);
            File.Copy(keySourcePath, keyCopyPath);
            var archiveFiles = new List<(string Path, string Entry)>
            {
                (snapshotPath, DatabaseEntry),
                (keyCopyPath, KeyEntry)
            };

            foreach (var secretPath in secretFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(secretPath);
                var copyPath = Path.Combine(workingDirectory, name);
                File.Copy(secretPath, copyPath);
                archiveFiles.Add((copyPath, $"accounts/{name}"));
            }

            var manifestFiles = new Dictionary<string, BackupFile>(StringComparer.Ordinal);
            foreach (var item in archiveFiles)
            {
                await using var file = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
                manifestFiles.Add(item.Entry, new BackupFile(file.Length, Convert.ToHexString(hash)));
            }

            var manifest = new BackupManifest(FormatVersion, DateTimeOffset.UtcNow, manifestFiles);
            await using (var stream = new FileStream(
                             temporaryArchive,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var item in archiveFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entry = archive.CreateEntry(item.Entry, CompressionLevel.NoCompression);
                        await using var entryStream = entry.Open();
                        await using var input = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await input.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
                    }

                    var manifestEntry = archive.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
                    await using var manifestStream = manifestEntry.Open();
                    await JsonSerializer.SerializeAsync(manifestStream, manifest, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryArchive, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryArchive))
            {
                File.Delete(temporaryArchive);
            }

            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
    }

    public async Task<string?> RestoreAsync(
        string backupPath,
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        EnsureWindows();

        var targetDirectory = Path.GetFullPath(dataDirectory);
        var parentDirectory = Path.GetDirectoryName(targetDirectory)
            ?? throw new ArgumentException("The data directory must have a parent directory.", nameof(dataDirectory));
        Directory.CreateDirectory(parentDirectory);
        var stagingDirectory = Path.Combine(parentDirectory, $".emailmpc-restore-{Guid.NewGuid():N}");
        var previousDirectory = Path.Combine(parentDirectory, $".emailmpc-previous-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        var switched = false;
        string? cleanupWarning = null;

        try
        {
            await ExtractAndValidateAsync(Path.GetFullPath(backupPath), stagingDirectory, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRestoredDatabase(stagingDirectory);

            if (Directory.Exists(targetDirectory))
            {
                Directory.Move(targetDirectory, previousDirectory);
                switched = true;
            }

            try
            {
                Directory.Move(stagingDirectory, targetDirectory);
                switched = false;
            }
            catch
            {
                if (switched && !Directory.Exists(targetDirectory))
                {
                    Directory.Move(previousDirectory, targetDirectory);
                    switched = false;
                }

                throw;
            }

            if (Directory.Exists(previousDirectory))
            {
                try
                {
                    Directory.Delete(previousDirectory, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    cleanupWarning = $"The restored data is active, but the prior data copy could not be removed from '{previousDirectory}': {exception.Message}";
                }
            }

            return cleanupWarning;
        }
        finally
        {
            if (switched && !Directory.Exists(targetDirectory) && Directory.Exists(previousDirectory))
            {
                Directory.Move(previousDirectory, targetDirectory);
            }

            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    public async Task<string?> RestoreMigrationSnapshotAsync(
        string snapshotPath,
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        EnsureWindows();

        var targetDirectory = Path.GetFullPath(dataDirectory);
        var fullSnapshotPath = Path.GetFullPath(snapshotPath);
        if (!string.Equals(
                Path.GetDirectoryName(fullSnapshotPath),
                targetDirectory,
                StringComparison.OrdinalIgnoreCase)
            || !MigrationSnapshotNamePattern.IsMatch(Path.GetFileName(fullSnapshotPath)))
        {
            throw new InvalidDataException("Choose a pre-migration snapshot from this eMail MPC data directory.");
        }

        if (!File.Exists(fullSnapshotPath))
        {
            throw new FileNotFoundException("The selected pre-migration snapshot was not found.", fullSnapshotPath);
        }

        var databasePath = Path.Combine(targetDirectory, DatabaseEntry);
        var keyPath = Path.Combine(targetDirectory, "keys", KeyEntry);
        if (!File.Exists(databasePath) || !File.Exists(keyPath))
        {
            throw new InvalidDataException("The current eMail MPC database or its protected key is missing.");
        }

        var operationDirectory = Path.Combine(
            Path.GetDirectoryName(targetDirectory)!,
            $".emailmpc-migration-restore-{Guid.NewGuid():N}");
        var stagedDatabasePath = Path.Combine(operationDirectory, DatabaseEntry);
        var previousDirectory = Path.Combine(operationDirectory, "previous");
        Directory.CreateDirectory(previousDirectory);
        var previousFiles = new List<(string OriginalPath, string SavedPath)>();
        var installedFiles = new List<string>();
        var committed = false;
        var preserveOperationDirectory = false;

        try
        {
            File.Copy(fullSnapshotPath, stagedDatabasePath);
            var key = new WindowsUserKeyStore().Load(keyPath);
            try
            {
                using var stagedDatabase = new EncryptedMailDatabase(stagedDatabasePath, key);
                stagedDatabase.Initialize();
                _ = await stagedDatabase.ListAccountsAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(targetDirectory);
            try
            {
                foreach (var fileName in new[] { DatabaseEntry, $"{DatabaseEntry}-wal", $"{DatabaseEntry}-shm" })
                {
                    var existingPath = Path.Combine(targetDirectory, fileName);
                    if (!File.Exists(existingPath))
                    {
                        continue;
                    }

                    var savedPath = Path.Combine(previousDirectory, fileName);
                    File.Move(existingPath, savedPath);
                    previousFiles.Add((existingPath, savedPath));
                }

                File.Move(stagedDatabasePath, databasePath);
                installedFiles.Add(databasePath);
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    var stagedSidecar = stagedDatabasePath + suffix;
                    if (File.Exists(stagedSidecar))
                    {
                        var targetSidecar = databasePath + suffix;
                        File.Move(stagedSidecar, targetSidecar);
                        installedFiles.Add(targetSidecar);
                    }
                }
            }
            catch (Exception restoreException)
            {
                try
                {
                    foreach (var installedFile in installedFiles)
                    {
                        File.Delete(installedFile);
                    }

                    foreach (var (originalPath, savedPath) in previousFiles.AsEnumerable().Reverse())
                    {
                        File.Move(savedPath, originalPath);
                    }
                }
                catch (Exception rollbackException)
                {
                    preserveOperationDirectory = true;
                    throw new AggregateException(
                        $"Restoring the pre-migration snapshot failed, and automatic rollback was incomplete. The previous database files were retained at '{previousDirectory}'.",
                        restoreException,
                        rollbackException);
                }

                throw;
            }

            committed = true;
            try
            {
                Directory.Delete(operationDirectory, recursive: true);
                return null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return $"The snapshot is active, but the prior database copy could not be removed from '{previousDirectory}': {exception.Message}";
            }
        }
        finally
        {
            if (!committed && !preserveOperationDirectory && Directory.Exists(operationDirectory))
            {
                Directory.Delete(operationDirectory, recursive: true);
            }
        }
    }

    private static async Task ExtractAndValidateAsync(
        string backupPath,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        var archiveInfo = new FileInfo(backupPath);
        if (!archiveInfo.Exists || archiveInfo.Length > MaximumArchiveBytes)
        {
            throw new InvalidDataException("The backup file is missing or exceeds the supported size limit.");
        }

        await using var stream = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count is < 3 or > MaximumAccountSecretFiles + 3)
        {
            throw new InvalidDataException("The backup contains an invalid number of entries.");
        }

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!IsAllowedEntry(entry.FullName) || !entries.TryAdd(entry.FullName, entry))
            {
                throw new InvalidDataException("The backup contains duplicate or unsupported paths.");
            }
        }

        if (!entries.TryGetValue(ManifestEntry, out var manifestEntry))
        {
            throw new InvalidDataException("The backup manifest is missing.");
        }

        BackupManifest? manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (manifest is null || manifest.FormatVersion != FormatVersion || manifest.Files is null
            || !manifest.Files.ContainsKey(DatabaseEntry) || !manifest.Files.ContainsKey(KeyEntry)
            || manifest.Files.Count != archive.Entries.Count - 1
            || manifest.Files.Keys.Any(name => !IsAllowedPayloadEntry(name)))
        {
            throw new InvalidDataException("The backup manifest is invalid or uses an unsupported format.");
        }

        long expandedBytes = 0;
        foreach (var (name, entry) in entries)
        {
            if (name == ManifestEntry)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!manifest.Files.TryGetValue(name, out var expected)
                || expected.Size < 0
                || expected.Size != entry.Length
                || (expandedBytes += entry.Length) > MaximumArchiveBytes)
            {
                throw new InvalidDataException($"The backup entry '{name}' does not match its manifest.");
            }

            var destinationPath = name == DatabaseEntry
                ? Path.Combine(stagingDirectory, DatabaseEntry)
                : name == KeyEntry
                    ? Path.Combine(stagingDirectory, "keys", KeyEntry)
                    : Path.Combine(stagingDirectory, "keys", "accounts", Path.GetFileName(name));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using (var source = entry.Open())
            await using (var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[64 * 1024];
                long copiedBytes = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    copiedBytes += read;
                    if (copiedBytes > expected.Size)
                    {
                        throw new InvalidDataException($"The backup entry '{name}' expanded beyond its declared size.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                destination.Flush(flushToDisk: true);
                if (copiedBytes != expected.Size
                    || !CryptographicOperations.FixedTimeEquals(
                        hash.GetHashAndReset(),
                        Convert.FromHexString(expected.Sha256)))
                {
                    throw new InvalidDataException($"The backup entry '{name}' failed its integrity check.");
                }
            }
        }

        if (entries.Keys.Any(name => name != ManifestEntry && !manifest.Files.ContainsKey(name)))
        {
            throw new InvalidDataException("The backup contains unmanifested data.");
        }
    }

    private static void ValidateRestoredDatabase(string stagingDirectory)
    {
        var keyPath = Path.Combine(stagingDirectory, "keys", KeyEntry);
        var key = new WindowsUserKeyStore().Load(keyPath);
        try
        {
            using var database = new EncryptedMailDatabase(Path.Combine(stagingDirectory, DatabaseEntry), key);
            database.Initialize();
            _ = database.ListAccountsAsync().GetAwaiter().GetResult();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static bool IsAllowedEntry(string entry)
    {
        return entry == ManifestEntry || IsAllowedPayloadEntry(entry);
    }

    private static bool IsAllowedPayloadEntry(string entry)
    {
        if (entry is DatabaseEntry or KeyEntry)
        {
            return true;
        }

        return entry.StartsWith("accounts/", StringComparison.Ordinal)
            && IsProtectedSecretFile(entry["accounts/".Length..]);
    }

    private static bool IsProtectedSecretFile(string? fileName)
    {
        return fileName is { Length: 70 }
            && fileName.EndsWith(".dpapi", StringComparison.Ordinal)
            && fileName.AsSpan(0, 64).ToString().All(Uri.IsHexDigit);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Local mail backup restore requires Windows DPAPI.");
        }
    }

    private sealed record BackupManifest(int FormatVersion, DateTimeOffset CreatedAtUtc, Dictionary<string, BackupFile> Files);

    private sealed record BackupFile(long Size, string Sha256);
}
