using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using EMailMPC.Core.Updates;

namespace EMailMPC.Core.Tests.Updates;

public sealed class PortableUpdateServiceTests
{
    [Fact]
    public async Task AppliesUpdatePreservingDataAndCanRestorePreviousProgramFiles()
    {
        using var directory = new TemporaryDirectory();
        var installation = Path.Combine(directory.Path, "portable");
        var dataDirectory = Path.Combine(installation, "data");
        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(Path.Combine(installation, "portable.mode"), "portable");
        await File.WriteAllTextAsync(Path.Combine(installation, "EMailMPC.Desktop.exe"), "old application");
        await File.WriteAllTextAsync(Path.Combine(installation, "obsolete.file"), "old file");
        await File.WriteAllTextAsync(Path.Combine(dataDirectory, "mail.db"), "keep local data");
        var archivePath = await CreateArchiveAsync(directory.Path, new Dictionary<string, string>
        {
            ["portable.mode"] = "portable",
            ["EMailMPC.Desktop.exe"] = "new application",
            ["new.file"] = "new file"
        });
        var archiveBytes = await File.ReadAllBytesAsync(archivePath);

        var backupDirectory = await new PortableUpdateService().ApplyAsync(
            archivePath,
            installation,
            Convert.ToHexString(SHA256.HashData(archiveBytes)),
            archiveBytes.Length);

        Assert.Equal("new application", await File.ReadAllTextAsync(Path.Combine(installation, "EMailMPC.Desktop.exe")));
        Assert.True(File.Exists(Path.Combine(installation, "new.file")));
        Assert.False(File.Exists(Path.Combine(installation, "obsolete.file")));
        Assert.Equal("keep local data", await File.ReadAllTextAsync(Path.Combine(dataDirectory, "mail.db")));

        PortableUpdateService.Rollback(installation, backupDirectory);

        Assert.Equal("old application", await File.ReadAllTextAsync(Path.Combine(installation, "EMailMPC.Desktop.exe")));
        Assert.Equal("old file", await File.ReadAllTextAsync(Path.Combine(installation, "obsolete.file")));
        Assert.False(File.Exists(Path.Combine(installation, "new.file")));
        Assert.Equal("keep local data", await File.ReadAllTextAsync(Path.Combine(dataDirectory, "mail.db")));
    }

    [Fact]
    public async Task RejectsArchiveWithWrongDigestWithoutChangingInstallation()
    {
        using var directory = new TemporaryDirectory();
        var installation = CreateInstallation(directory.Path);
        var archivePath = await CreateArchiveAsync(directory.Path, new Dictionary<string, string>
        {
            ["portable.mode"] = "portable",
            ["EMailMPC.Desktop.exe"] = "new application"
        });
        var archiveBytes = await File.ReadAllBytesAsync(archivePath);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PortableUpdateService().ApplyAsync(
                archivePath,
                installation,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("wrong archive"))),
                archiveBytes.Length));

        Assert.Equal("old application", await File.ReadAllTextAsync(Path.Combine(installation, "EMailMPC.Desktop.exe")));
    }

    [Fact]
    public async Task RejectsArchiveWithPathTraversalWithoutChangingInstallation()
    {
        using var directory = new TemporaryDirectory();
        var installation = CreateInstallation(directory.Path);
        var archivePath = await CreateArchiveAsync(directory.Path, new Dictionary<string, string>
        {
            ["portable.mode"] = "portable",
            ["EMailMPC.Desktop.exe"] = "new application",
            ["../outside.txt"] = "outside"
        });
        var archiveBytes = await File.ReadAllBytesAsync(archivePath);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PortableUpdateService().ApplyAsync(
                archivePath,
                installation,
                Convert.ToHexString(SHA256.HashData(archiveBytes)),
                archiveBytes.Length));

        Assert.Equal("old application", await File.ReadAllTextAsync(Path.Combine(installation, "EMailMPC.Desktop.exe")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "outside.txt")));
    }

    private static string CreateInstallation(string root)
    {
        var installation = Path.Combine(root, "portable");
        Directory.CreateDirectory(installation);
        File.WriteAllText(Path.Combine(installation, "portable.mode"), "portable");
        File.WriteAllText(Path.Combine(installation, "EMailMPC.Desktop.exe"), "old application");
        return installation;
    }

    private static async Task<string> CreateArchiveAsync(string directory, IReadOnlyDictionary<string, string> entries)
    {
        var archivePath = Path.Combine(directory, $"{Guid.NewGuid():N}.zip");
        await using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
            await using var entryStream = entry.Open();
            await using var writer = new StreamWriter(entryStream);
            await writer.WriteAsync(content);
        }

        return archivePath;
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
