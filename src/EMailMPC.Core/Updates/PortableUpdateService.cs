using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace EMailMPC.Core.Updates;

public sealed class PortableUpdateService
{
    private const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumArchiveEntries = 1000;
    private const string ApplicationExecutable = "EMailMPC.Desktop.exe";
    private static readonly Regex BackupDirectoryNamePattern = new(
        @"^\.emailmpc-update-backup-[a-f0-9]{32}$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public async Task<string> ApplyAsync(
        string archivePath,
        string installationDirectory,
        string expectedSha256,
        long expectedSizeBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        if (expectedSizeBytes <= 0 || expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("The update size or SHA-256 metadata is invalid.");
        }

        var archiveFullPath = Path.GetFullPath(archivePath);
        var installationFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationDirectory));
        if (!File.Exists(Path.Combine(installationFullPath, "portable.mode")))
        {
            throw new InvalidOperationException("Automatic updates are available only for portable installations.");
        }

        await VerifyArchiveDigestAsync(
            archiveFullPath,
            expectedSha256,
            expectedSizeBytes,
            cancellationToken).ConfigureAwait(false);

        var parentDirectory = Path.GetDirectoryName(installationFullPath)
            ?? throw new ArgumentException("The installation directory must have a parent directory.", nameof(installationDirectory));
        var operationId = Guid.NewGuid().ToString("N");
        var stagingDirectory = Path.Combine(parentDirectory, $".emailmpc-update-stage-{operationId}");
        var backupDirectory = Path.Combine(parentDirectory, $".emailmpc-update-backup-{operationId}");
        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(backupDirectory);

        var movedToBackup = new List<string>();
        var installedPaths = new List<string>();
        var committed = false;
        var preserveBackup = false;

        try
        {
            await ExtractAndValidateAsync(archiveFullPath, stagingDirectory, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                foreach (var existingPath in Directory.EnumerateFileSystemEntries(installationFullPath).ToArray())
                {
                    if (string.Equals(Path.GetFileName(existingPath), "data", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var backupPath = Path.Combine(backupDirectory, Path.GetFileName(existingPath));
                    MoveEntry(existingPath, backupPath);
                    movedToBackup.Add(Path.GetFileName(existingPath));
                }

                foreach (var stagedPath in Directory.EnumerateFileSystemEntries(stagingDirectory).ToArray())
                {
                    var installedPath = Path.Combine(installationFullPath, Path.GetFileName(stagedPath));
                    MoveEntry(stagedPath, installedPath);
                    installedPaths.Add(Path.GetFileName(stagedPath));
                }
            }
            catch (Exception updateException)
            {
                try
                {
                    RollbackFiles(installationFullPath, backupDirectory, movedToBackup, installedPaths);
                }
                catch (Exception rollbackException)
                {
                    preserveBackup = true;
                    throw new AggregateException(
                        $"Applying the portable update failed and automatic rollback was incomplete. The previous application files were retained at '{backupDirectory}'.",
                        updateException,
                        rollbackException);
                }

                throw;
            }

            committed = true;
            return backupDirectory;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }

            if (!committed && !preserveBackup && Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
    }

    public static void Rollback(string installationDirectory, string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        var installationFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationDirectory));
        var backupFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(backupDirectory));
        if (!string.Equals(
                Path.GetDirectoryName(installationFullPath),
                Path.GetDirectoryName(backupFullPath),
                StringComparison.OrdinalIgnoreCase)
            || !BackupDirectoryNamePattern.IsMatch(Path.GetFileName(backupFullPath))
            || !Directory.Exists(backupFullPath))
        {
            throw new InvalidDataException("The update rollback directory is invalid or does not belong beside this installation.");
        }

        var backupEntries = Directory.EnumerateFileSystemEntries(backupFullPath)
            .Select(path => Path.GetFileName(path)
                ?? throw new InvalidDataException("The update backup contains an invalid entry path."))
            .ToArray();
        var installedEntries = Directory.EnumerateFileSystemEntries(installationFullPath)
            .Where(path => !string.Equals(Path.GetFileName(path), "data", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileName(path)
                ?? throw new InvalidDataException("The portable installation contains an invalid entry path."))
            .ToArray();

        foreach (var entry in installedEntries)
        {
            DeleteEntry(Path.Combine(installationFullPath, entry));
        }

        foreach (var entry in backupEntries)
        {
            MoveEntry(Path.Combine(backupFullPath, entry), Path.Combine(installationFullPath, entry));
        }

        Directory.Delete(backupFullPath, recursive: true);
    }

    public static string? Complete(string installationDirectory, string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        var installationFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationDirectory));
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(backupDirectory));
        if (!string.Equals(
                Path.GetDirectoryName(installationFullPath),
                Path.GetDirectoryName(fullPath),
                StringComparison.OrdinalIgnoreCase)
            || !BackupDirectoryNamePattern.IsMatch(Path.GetFileName(fullPath)))
        {
            throw new InvalidDataException("The update backup directory is invalid.");
        }

        if (Directory.Exists(fullPath))
        {
            try
            {
                Directory.Delete(fullPath, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return $"The updated application started successfully, but the prior program-file backup could not be removed from '{fullPath}': {exception.Message}";
            }
        }

        return null;
    }

    private static async Task VerifyArchiveDigestAsync(
        string archivePath,
        string expectedSha256,
        long expectedSizeBytes,
        CancellationToken cancellationToken)
    {
        var archive = new FileInfo(archivePath);
        if (!archive.Exists || archive.Length != expectedSizeBytes || archive.Length > MaximumExpandedBytes)
        {
            throw new InvalidDataException("The portable update archive is missing or does not match its declared size.");
        }

        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actualHash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, Convert.FromHexString(expectedSha256)))
        {
            throw new InvalidDataException("The portable update archive failed its SHA-256 check.");
        }
    }

    private static async Task ExtractAndValidateAsync(
        string archivePath,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count is < 2 or > MaximumArchiveEntries)
        {
            throw new InvalidDataException("The portable update contains an invalid number of entries.");
        }

        var root = Path.GetFullPath(stagingDirectory) + Path.DirectorySeparatorChar;
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var foundApplication = false;
        var foundPortableMarker = false;
        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedName = entry.FullName.Replace('\\', '/');
            if (normalizedName.StartsWith("/", StringComparison.Ordinal)
                || Path.IsPathRooted(entry.FullName)
                || normalizedName.Split('/').Any(part =>
                    part is "." or ".."
                    || part.Contains(':'))
                || string.Equals(normalizedName.Split('/')[0], "data", StringComparison.OrdinalIgnoreCase)
                || !seenPaths.Add(normalizedName))
            {
                throw new InvalidDataException("The portable update contains an unsafe or duplicate path.");
            }

            var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixFileType == 0xA000)
            {
                throw new InvalidDataException("The portable update contains a symbolic link.");
            }

            if ((expandedBytes += entry.Length) > MaximumExpandedBytes)
            {
                throw new InvalidDataException("The portable update exceeds the supported expanded size.");
            }

            var destinationPath = Path.GetFullPath(Path.Combine(stagingDirectory, normalizedName.Replace('/', Path.DirectorySeparatorChar)));
            if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The portable update contains a path outside the installation directory.");
            }

            if (normalizedName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var source = entry.Open();
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
            foundApplication |= string.Equals(normalizedName, ApplicationExecutable, StringComparison.OrdinalIgnoreCase);
            foundPortableMarker |= string.Equals(normalizedName, "portable.mode", StringComparison.OrdinalIgnoreCase);
        }

        if (!foundApplication || !foundPortableMarker)
        {
            throw new InvalidDataException("The portable update is missing the application executable or portable marker.");
        }
    }

    private static void RollbackFiles(
        string installationDirectory,
        string backupDirectory,
        IReadOnlyCollection<string> movedToBackup,
        IReadOnlyCollection<string> installedPaths)
    {
        foreach (var entry in installedPaths)
        {
            DeleteEntry(Path.Combine(installationDirectory, entry));
        }

        foreach (var entry in movedToBackup.Reverse())
        {
            var backupPath = Path.Combine(backupDirectory, entry);
            if (File.Exists(backupPath) || Directory.Exists(backupPath))
            {
                MoveEntry(backupPath, Path.Combine(installationDirectory, entry));
            }
        }
    }

    private static void MoveEntry(string sourcePath, string destinationPath)
    {
        if (Directory.Exists(sourcePath))
        {
            Directory.Move(sourcePath, destinationPath);
        }
        else
        {
            File.Move(sourcePath, destinationPath);
        }
    }

    private static void DeleteEntry(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
