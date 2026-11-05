using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.IO.Compression;
using EMailMPC.Core.Storage;
using Microsoft.Data.Sqlite;

namespace EMailMPC.Core.Tests.Storage;

[SupportedOSPlatform("windows")]
public sealed class LocalMailBackupServiceTests
{
    [WindowsFact]
    public async Task BackupRestoresEncryptedMessagesDraftsAndProtectedAccountSecrets()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "source");
        var target = Path.Combine(directory.Path, "target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        var sourceKeyPath = Path.Combine(source, "keys", "database.dpapi");
        var sourceSecretsPath = Path.Combine(source, "keys", "accounts");
        var key = new WindowsUserKeyStore().LoadOrCreate(sourceKeyPath);
        var account = CreateAccount();
        try
        {
            using (var database = NewDatabase(Path.Combine(source, "mail.db"), key))
            {
                database.Initialize();
                await database.SaveAccountAsync(account);
                await database.SaveMessageAsync(CreateMessage("restored-message", account.Id));
                await database.SaveDraftAsync(new MailDraftRecord(
                    "restored-draft",
                    account.Id,
                    "recipient@example.com",
                    "Encrypted draft",
                    "Draft body",
                    DateTimeOffset.UtcNow));
            }

            var sourceSecrets = new WindowsUserSecretStore(sourceSecretsPath);
            sourceSecrets.Save(account.Id, "provider-app-password");
            var backupPath = Path.Combine(directory.Path, "mail.empbackup");
            using (var sourceDatabase = NewDatabase(Path.Combine(source, "mail.db"), key))
            {
                sourceDatabase.Initialize();
                await new LocalMailBackupService().CreateAsync(
                    sourceDatabase,
                    sourceKeyPath,
                    sourceSecretsPath,
                    backupPath);
            }

            using (var archive = ZipFile.OpenRead(backupPath))
            {
                Assert.Contains(archive.Entries, entry => entry.FullName == "mail.db");
                Assert.Contains(archive.Entries, entry => entry.FullName == "database.dpapi");
                Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("accounts/", StringComparison.Ordinal));
                Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("provider-app-password", StringComparison.Ordinal));
            }

            var targetKey = new WindowsUserKeyStore().LoadOrCreate(Path.Combine(target, "keys", "database.dpapi"));
            using (var targetDatabase = NewDatabase(Path.Combine(target, "mail.db"), targetKey))
            {
                targetDatabase.Initialize();
                await targetDatabase.SaveMessageAsync(CreateMessage("old-message", null));
            }

            CryptographicOperations.ZeroMemory(targetKey);
            await new LocalMailBackupService().RestoreAsync(backupPath, target);

            var restoredKey = new WindowsUserKeyStore().Load(Path.Combine(target, "keys", "database.dpapi"));
            try
            {
                using var restoredDatabase = NewDatabase(Path.Combine(target, "mail.db"), restoredKey);
                restoredDatabase.Initialize();
                Assert.Equal(["restored-message"], (await restoredDatabase.SearchAsync(new EMailMPC.Core.Search.SearchCriteria())).Select(message => message.Id));
                Assert.Equal(["restored-draft"], (await restoredDatabase.ListDraftsAsync(account.Id)).Select(draft => draft.Id));
                Assert.Equal("provider-app-password", new WindowsUserSecretStore(Path.Combine(target, "keys", "accounts")).Load(account.Id));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(restoredKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    [WindowsFact]
    public async Task InvalidBackupDoesNotReplaceExistingData()
    {
        using var directory = new TemporaryDirectory();
        var target = Path.Combine(directory.Path, "target");
        Directory.CreateDirectory(target);
        var keyPath = Path.Combine(target, "keys", "database.dpapi");
        var key = new WindowsUserKeyStore().LoadOrCreate(keyPath);
        try
        {
            using (var database = NewDatabase(Path.Combine(target, "mail.db"), key))
            {
                database.Initialize();
                await database.SaveMessageAsync(CreateMessage("keep-message", null));
            }

            var invalidBackup = Path.Combine(directory.Path, "invalid.empbackup");
            await File.WriteAllTextAsync(invalidBackup, "not a valid backup");

            await Assert.ThrowsAnyAsync<Exception>(() =>
                new LocalMailBackupService().RestoreAsync(invalidBackup, target));

            using var existingDatabase = NewDatabase(Path.Combine(target, "mail.db"), key);
            existingDatabase.Initialize();
            Assert.Equal(["keep-message"], (await existingDatabase.SearchAsync(new EMailMPC.Core.Search.SearchCriteria())).Select(message => message.Id));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    [WindowsFact]
    public async Task MigrationSnapshotRestoreReturnsDatabaseToSnapshotAndPreservesCurrentKey()
    {
        using var directory = new TemporaryDirectory();
        var dataDirectory = Path.Combine(directory.Path, "data");
        var keyPath = Path.Combine(dataDirectory, "keys", "database.dpapi");
        var databasePath = Path.Combine(dataDirectory, "mail.db");
        var key = new WindowsUserKeyStore().LoadOrCreate(keyPath);
        const string accountId = "snapshot-account";
        var snapshotPath = Path.Combine(
            dataDirectory,
            $"mail.db.pre-migration-v7-{DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.db");
        try
        {
            using (var database = NewDatabase(databasePath, key))
            {
                database.Initialize();
                await database.SaveAccountAsync(CreateAccount() with { Id = accountId });
                await database.SaveMessageAsync(CreateMessage("before-snapshot", null));
                using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                       {
                           DataSource = databasePath,
                           Pooling = false
                       }.ToString()))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = $"""
                        PRAGMA key = "x'{Convert.ToHexString(key)}'";
                        PRAGMA user_version = 7;
                        """;
                    command.ExecuteNonQuery();
                }

                database.CreateEncryptedSnapshot(snapshotPath);
                await database.SaveMessageAsync(CreateMessage("after-snapshot", null));
            }

            var secretStore = new WindowsUserSecretStore(Path.Combine(dataDirectory, "keys", "accounts"));
            secretStore.Save(accountId, "existing-account-secret");
            var warning = await new LocalMailBackupService().RestoreMigrationSnapshotAsync(snapshotPath, dataDirectory);

            Assert.Null(warning);
            Assert.True(File.Exists(keyPath));
            Assert.Equal("existing-account-secret", secretStore.Load(accountId));
            using var restoredDatabase = NewDatabase(databasePath, key);
            restoredDatabase.Initialize();
            Assert.Equal(
                ["before-snapshot"],
                (await restoredDatabase.SearchAsync(new EMailMPC.Core.Search.SearchCriteria()))
                    .Select(message => message.Id));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    [WindowsFact]
    public async Task MigrationSnapshotRestoreRejectsSnapshotsOutsideCurrentDataDirectory()
    {
        using var directory = new TemporaryDirectory();
        var dataDirectory = Path.Combine(directory.Path, "data");
        Directory.CreateDirectory(dataDirectory);
        var key = new WindowsUserKeyStore().LoadOrCreate(
            Path.Combine(dataDirectory, "keys", "database.dpapi"));
        var databasePath = Path.Combine(dataDirectory, "mail.db");
        var snapshotPath = Path.Combine(
            directory.Path,
            $"mail.db.pre-migration-v7-{DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.db");
        try
        {
            using (var database = NewDatabase(databasePath, key))
            {
                database.Initialize();
                await database.SaveMessageAsync(CreateMessage("keep-current", null));
                database.CreateEncryptedSnapshot(snapshotPath);
            }

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new LocalMailBackupService().RestoreMigrationSnapshotAsync(snapshotPath, dataDirectory));

            using var existingDatabase = NewDatabase(databasePath, key);
            existingDatabase.Initialize();
            Assert.Equal(
                ["keep-current"],
                (await existingDatabase.SearchAsync(new EMailMPC.Core.Search.SearchCriteria()))
                    .Select(message => message.Id));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    [WindowsFact]
    public async Task InvalidMigrationSnapshotDoesNotReplaceCurrentDatabase()
    {
        using var directory = new TemporaryDirectory();
        var dataDirectory = Path.Combine(directory.Path, "data");
        Directory.CreateDirectory(dataDirectory);
        var key = new WindowsUserKeyStore().LoadOrCreate(
            Path.Combine(dataDirectory, "keys", "database.dpapi"));
        var databasePath = Path.Combine(dataDirectory, "mail.db");
        var snapshotPath = Path.Combine(
            dataDirectory,
            $"mail.db.pre-migration-v7-{DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.db");
        try
        {
            using (var database = NewDatabase(databasePath, key))
            {
                database.Initialize();
                await database.SaveMessageAsync(CreateMessage("keep-current", null));
            }

            await File.WriteAllTextAsync(snapshotPath, "not an encrypted database");
            await Assert.ThrowsAnyAsync<Exception>(() =>
                new LocalMailBackupService().RestoreMigrationSnapshotAsync(snapshotPath, dataDirectory));

            using var existingDatabase = NewDatabase(databasePath, key);
            existingDatabase.Initialize();
            Assert.Equal(
                ["keep-current"],
                (await existingDatabase.SearchAsync(new EMailMPC.Core.Search.SearchCriteria()))
                    .Select(message => message.Id));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static MailAccountRecord CreateAccount() => new(
        "backup-account",
        "owner@example.com",
        "Owner",
        "imap.example.com",
        993,
        MailTransportSecurity.SslOnConnect,
        "smtp.example.com",
        587,
        MailTransportSecurity.StartTls,
        MailAuthenticationMethod.AppPassword);

    private static MailMessageRecord CreateMessage(string id, string? accountId) => new(
        id,
        "sender@example.com",
        "owner@example.com",
        "Restored message",
        "Encrypted message body",
        DateTimeOffset.UtcNow,
        "Inbox",
        false,
        false,
        false,
        accountId);

    private static EncryptedMailDatabase NewDatabase(string path, byte[] key) => new(path, key);

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
