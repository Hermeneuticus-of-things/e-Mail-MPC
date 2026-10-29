using System.Security.Cryptography;
using System.Runtime.Versioning;
using EMailMPC.Core.Search;
using EMailMPC.Core.Storage;
using EMailMPC.Core.Accounts;
using Microsoft.Data.Sqlite;

namespace EMailMPC.Core.Tests.Storage;

[SupportedOSPlatform("windows")]
public sealed class EncryptedMailDatabaseTests
{
    [Fact]
    public async Task PersistsEncryptedMessagesAndFindsTextAcrossFieldsAfterReopen()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "mail.db");
        var key = RandomNumberGenerator.GetBytes(32);
        var message = CreateMessage("message-1", "Quarterly report", "Please review the details.");

        using (var database = new EncryptedMailDatabase(databasePath, key))
        {
            database.Initialize();
            await database.SaveMessageAsync(message);

            var matches = await database.SearchAsync(new SearchCriteria(text: "quarterly review"));

            Assert.Single(matches);
            Assert.Equal(message.Id, matches[0].Id);
        }

        var storedBytes = await File.ReadAllBytesAsync(databasePath);
        var header = new byte[16];
        storedBytes.AsSpan(0, header.Length).CopyTo(header);

        Assert.NotEqual("SQLite format 3\0", System.Text.Encoding.ASCII.GetString(header));
        Assert.DoesNotContain("Quarterly report", System.Text.Encoding.UTF8.GetString(storedBytes), StringComparison.Ordinal);
        Assert.DoesNotContain("Please review the details.", System.Text.Encoding.UTF8.GetString(storedBytes), StringComparison.Ordinal);

        using var reopened = new EncryptedMailDatabase(databasePath, key);
        reopened.Initialize();
        var persisted = await reopened.SearchAsync(new SearchCriteria(sender: "ana@example.com"));
        Assert.Single(persisted);
        Assert.Equal(message.Subject, persisted[0].Subject);
    }

    [Fact]
    public async Task LoadsFullMessageBodyForLocalReading()
    {
        using var directory = new TemporaryDirectory();
        using var database = new EncryptedMailDatabase(
            Path.Combine(directory.Path, "mail.db"),
            RandomNumberGenerator.GetBytes(32));
        database.Initialize();
        var message = CreateMessage("read-message", "Read this", "Full locally stored body.");
        await database.SaveMessageAsync(message);

        var loaded = await database.GetMessageAsync(message.Id);

        Assert.Equal(message, loaded);
        Assert.Null(await database.GetMessageAsync("missing-message"));
    }

    [Fact]
    public async Task SearchAppliesTextFiltersAndDateBounds()
    {
        using var directory = new TemporaryDirectory();
        var key = RandomNumberGenerator.GetBytes(32);
        using var database = new EncryptedMailDatabase(Path.Combine(directory.Path, "mail.db"), key);
        database.Initialize();
        await database.SaveMessageAsync(CreateMessage("match", "Project update", "Review the project", receivedAt: DateTimeOffset.Parse("2026-10-10T12:00:00Z")));
        await database.SaveMessageAsync(CreateMessage("read", "Project update", "Review the project", isRead: true, receivedAt: DateTimeOffset.Parse("2026-10-11T12:00:00Z")));
        await database.SaveMessageAsync(CreateMessage("old", "Project update", "Review the project", folder: "Archive", receivedAt: DateTimeOffset.Parse("2026-09-10T12:00:00Z")));

        var matches = await database.SearchAsync(new SearchCriteria(
            text: "project review",
            sender: "ANA@EXAMPLE.COM",
            folder: "Inbox",
            after: DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
            before: DateTimeOffset.Parse("2026-10-31T23:59:59Z"),
            isRead: false,
            isFlagged: false,
            hasAttachments: true));

        Assert.Single(matches);
        Assert.Equal("match", matches[0].Id);
    }

    [Fact]
    public async Task PersistsAccountAndFoldersWithoutPersistingCredentials()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "mail.db");
        var key = RandomNumberGenerator.GetBytes(32);
        var account = CreateAccount("account-1", "ana@example.com");
        var inbox = new MailFolderRecord("folder-1", account.Id, "remote-inbox", "Inbox", "Inbox", null, "inbox");

        using (var database = new EncryptedMailDatabase(databasePath, key))
        {
            database.Initialize();
            await database.SaveAccountAsync(account);
            await database.SaveFolderAsync(inbox);

            var accounts = await database.ListAccountsAsync();
            var folders = await database.ListFoldersAsync(account.Id);
            Assert.Equal(account, Assert.Single(accounts));
            Assert.Equal(inbox, Assert.Single(folders));
        }

        var storedText = System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(databasePath));
        Assert.DoesNotContain("access-token", storedText, StringComparison.Ordinal);
        Assert.DoesNotContain("app-password", storedText, StringComparison.Ordinal);

        using var reopened = new EncryptedMailDatabase(databasePath, key);
        reopened.Initialize();
        Assert.Equal(account, Assert.Single(await reopened.ListAccountsAsync()));
        Assert.Equal(inbox, Assert.Single(await reopened.ListFoldersAsync(account.Id)));
    }

    [Fact]
    public async Task SavesAccountAndFoldersAtomically()
    {
        using var directory = new TemporaryDirectory();
        var key = RandomNumberGenerator.GetBytes(32);
        using var database = new EncryptedMailDatabase(Path.Combine(directory.Path, "mail.db"), key);
        database.Initialize();
        var account = CreateAccount("account-1", "ana@example.com");
        var folder = new MailFolderRecord("folder-1", "different-account", "remote-inbox", "Inbox", "Inbox", null, "inbox");

        await Assert.ThrowsAsync<ArgumentException>(() => database.SaveAccountAsync(account, [folder]));

        Assert.Empty(await database.ListAccountsAsync());
    }

    [Fact]
    public async Task BatchMessageWriteRollsBackAllMessagesOnForeignKeyFailure()
    {
        using var directory = new TemporaryDirectory();
        var key = RandomNumberGenerator.GetBytes(32);
        using var database = new EncryptedMailDatabase(Path.Combine(directory.Path, "mail.db"), key);
        database.Initialize();
        var account = CreateAccount("account-1", "ana@example.com");
        await database.SaveAccountAsync(account);
        var validMessage = CreateMessage("valid", "First", "Should roll back", accountId: account.Id);
        var invalidMessage = CreateMessage("invalid", "Second", "Invalid owner", accountId: "missing-account");

        await Assert.ThrowsAsync<SqliteException>(() => database.SaveMessagesAsync([validMessage, invalidMessage]));

        Assert.Empty(await database.SearchAsync(new SearchCriteria(text: "rollback")));
    }

    [Fact]
    public async Task DeletingAccountKeepsLocalMessagesAndRemovesItsFolders()
    {
        using var directory = new TemporaryDirectory();
        var key = RandomNumberGenerator.GetBytes(32);
        using var database = new EncryptedMailDatabase(Path.Combine(directory.Path, "mail.db"), key);
        database.Initialize();
        var account = CreateAccount("account-1", "ana@example.com");
        await database.SaveAccountAsync(account, [
            new MailFolderRecord("folder-1", account.Id, "remote-inbox", "Inbox", "Inbox", null, "inbox")
        ]);
        await database.SaveMessageAsync(CreateMessage("message-1", "Preserve local mail", "Keep this locally", accountId: account.Id));

        await database.DeleteAccountAsync(account.Id);

        Assert.Empty(await database.ListAccountsAsync());
        Assert.Empty(await database.ListFoldersAsync(account.Id));
        var preserved = await database.SearchAsync(new SearchCriteria(text: "preserve"));
        Assert.Equal("message-1", Assert.Single(preserved).Id);
        Assert.Null(preserved[0].AccountId);
    }

    [Fact]
    public async Task KeepsFoldersScopedToTheirAccountAndSearchCanBeAccountScoped()
    {
        using var directory = new TemporaryDirectory();
        var key = RandomNumberGenerator.GetBytes(32);
        using var database = new EncryptedMailDatabase(Path.Combine(directory.Path, "mail.db"), key);
        database.Initialize();
        var firstAccount = CreateAccount("account-a", "ana@example.com");
        var secondAccount = CreateAccount("account-b", "ana@example.org");
        await database.SaveAccountAsync(firstAccount);
        await database.SaveAccountAsync(secondAccount);
        await database.SaveFolderAsync(new MailFolderRecord("inbox-a", firstAccount.Id, "remote-inbox", "Inbox", "Inbox", null, "inbox"));
        await database.SaveFolderAsync(new MailFolderRecord("inbox-b", secondAccount.Id, "remote-inbox", "Inbox", "Inbox", null, "inbox"));
        await database.SaveMessageAsync(CreateMessage("message-a", "Shared subject", "Same body", accountId: firstAccount.Id));
        await database.SaveMessageAsync(CreateMessage("message-b", "Shared subject", "Same body", accountId: secondAccount.Id));

        var firstAccountFolders = await database.ListFoldersAsync(firstAccount.Id);
        var firstAccountMessages = await database.SearchAsync(new SearchCriteria(text: "shared", accountId: firstAccount.Id));

        Assert.Equal("inbox-a", Assert.Single(firstAccountFolders).Id);
        Assert.Equal("message-a", Assert.Single(firstAccountMessages).Id);
        Assert.Equal(firstAccount.Id, firstAccountMessages[0].AccountId);
    }

    [Fact]
    public async Task RejectsInvalidAccountEndpointsAndFoldersForUnknownAccounts()
    {
        using var directory = new TemporaryDirectory();
        var key = RandomNumberGenerator.GetBytes(32);
        using var database = new EncryptedMailDatabase(Path.Combine(directory.Path, "mail.db"), key);
        database.Initialize();
        var account = CreateAccount("account-1", "ana@example.com");

        await Assert.ThrowsAsync<ArgumentException>(() => database.SaveAccountAsync(account with { ImapPort = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => database.SaveAccountAsync(account with { ImapSecurity = (MailTransportSecurity)0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => database.SaveAccountAsync(account with { EmailAddress = "not-an-email" }));
        await Assert.ThrowsAsync<ArgumentException>(() => database.SaveAccountAsync(account with { ImapHost = "imap.example.com:993" }));
        await Assert.ThrowsAsync<SqliteException>(() => database.SaveFolderAsync(
            new MailFolderRecord("orphan-folder", "missing-account", "remote-inbox", "Inbox", "Inbox", null, "inbox")));
    }

    [Fact]
    public void ImapTransportSecurityNeverSelectsPlaintext()
    {
        Assert.Equal(MailKit.Security.SecureSocketOptions.SslOnConnect,
            ImapMailService.GetSocketOptions(MailTransportSecurity.SslOnConnect));
        Assert.Equal(MailKit.Security.SecureSocketOptions.StartTls,
            ImapMailService.GetSocketOptions(MailTransportSecurity.StartTls));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImapMailService.GetSocketOptions((MailTransportSecurity)0));
    }

    [Fact]
    public async Task RefusesOAuthAccountsInsteadOfSendingAnAppPasswordAsOAuth()
    {
        using var directory = new TemporaryDirectory();
        using var database = new EncryptedMailDatabase(
            Path.Combine(directory.Path, "mail.db"),
            RandomNumberGenerator.GetBytes(32));
        var account = CreateAccount("oauth-account", "ana@example.com") with
        {
            AuthenticationMethod = MailAuthenticationMethod.OAuth2
        };

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new ImapMailService(database).TestAndListFoldersAsync(account, "not-an-oauth-token"));
    }

    [Fact]
    public void ProtectsAccountSecretsAndSupportsReplacementAndDeletion()
    {
        using var directory = new TemporaryDirectory();
        var secretStore = new WindowsUserSecretStore(Path.Combine(directory.Path, "accounts"));
        const string accountId = "account/unsafe-id";
        const string firstPassword = "app-password-secret";
        const string secondPassword = "replacement-password";

        secretStore.Save(accountId, firstPassword);
        var secretFiles = Directory.GetFiles(Path.Combine(directory.Path, "accounts"));
        Assert.Single(secretFiles);
        Assert.DoesNotContain(accountId, secretFiles[0], StringComparison.Ordinal);
        Assert.DoesNotContain(firstPassword, System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(secretFiles[0])), StringComparison.Ordinal);
        Assert.Equal(firstPassword, secretStore.Load(accountId));
        var otherAccountPath = Path.Combine(
            Path.GetDirectoryName(secretFiles[0])!,
            $"{Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("different-account")))}.dpapi");
        File.Copy(secretFiles[0], otherAccountPath);
        Assert.Throws<CryptographicException>(() => secretStore.Load("different-account"));
        File.Delete(otherAccountPath);

        secretStore.Save(accountId, secondPassword);
        Assert.Equal(secondPassword, secretStore.Load(accountId));

        secretStore.Delete(accountId);
        Assert.Throws<FileNotFoundException>(() => secretStore.Load(accountId));
    }

    [Fact]
    public async Task UpdatingMessageReplacesItsSearchIndexEntry()
    {
        using var directory = new TemporaryDirectory();
        var key = RandomNumberGenerator.GetBytes(32);
        using var database = new EncryptedMailDatabase(Path.Combine(directory.Path, "mail.db"), key);
        database.Initialize();
        await database.SaveMessageAsync(CreateMessage("message-1", "Old unique term", "Old body"));

        await database.SaveMessageAsync(CreateMessage("message-1", "New unique term", "New body"));

        Assert.Empty(await database.SearchAsync(new SearchCriteria(text: "old")));
        Assert.Single(await database.SearchAsync(new SearchCriteria(text: "new")));
    }

    [Fact]
    public async Task MigratesVersionOneDatabaseAndRebuildsItsFullTextIndex()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "mail.db");
        var key = RandomNumberGenerator.GetBytes(32);
        CreateLegacyDatabase(databasePath, key, schemaVersion: 1);

        using var database = new EncryptedMailDatabase(databasePath, key);
        database.Initialize();

        var priorMessage = await database.SearchAsync(new SearchCriteria(text: "legacy unique"));
        Assert.Single(priorMessage);
        Assert.Equal("legacy-1", priorMessage[0].Id);

        await database.SaveMessageAsync(CreateMessage("new-message", "Post-migration", "trigger indexes this"));
        Assert.Single(await database.SearchAsync(new SearchCriteria(text: "trigger indexes")));
    }

    [Fact]
    public async Task MigratesVersionTwoDatabaseAndPreservesExistingMessages()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "mail.db");
        var key = RandomNumberGenerator.GetBytes(32);
        CreateLegacyDatabase(databasePath, key, schemaVersion: 2);

        using var database = new EncryptedMailDatabase(databasePath, key);
        database.Initialize();

        var priorMessage = await database.SearchAsync(new SearchCriteria(text: "legacy unique"));
        Assert.Equal("legacy-1", Assert.Single(priorMessage).Id);
        Assert.Null(priorMessage[0].AccountId);
    }

    [Fact]
    public async Task SearchHandlesUnicodeAndDoesNotInterpretFtsOperatorsAsSyntax()
    {
        using var directory = new TemporaryDirectory();
        var key = RandomNumberGenerator.GetBytes(32);
        using var database = new EncryptedMailDatabase(Path.Combine(directory.Path, "mail.db"), key);
        database.Initialize();
        await database.SaveMessageAsync(CreateMessage("accented", "Cafe meeting", "Discuss the alpha release"));

        var matches = await database.SearchAsync(new SearchCriteria(text: "café alpha OR"));

        Assert.Empty(matches);
        var accentedMatch = await database.SearchAsync(new SearchCriteria(text: "café alpha"));
        Assert.Single(accentedMatch);
        Assert.Equal("accented", accentedMatch[0].Id);
    }

    [Fact]
    public async Task RejectsWrongKeyWithoutReplacingKeyOrDamagingDatabase()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "mail.db");
        var correctKey = RandomNumberGenerator.GetBytes(32);
        using (var database = new EncryptedMailDatabase(databasePath, correctKey))
        {
            database.Initialize();
            await database.SaveMessageAsync(CreateMessage("message-1", "Keep this", "Existing content"));
        }

        using var wrongKeyDatabase = new EncryptedMailDatabase(databasePath, RandomNumberGenerator.GetBytes(32));
        Assert.Throws<CryptographicException>(wrongKeyDatabase.Initialize);

        using var reopened = new EncryptedMailDatabase(databasePath, correctKey);
        reopened.Initialize();
        Assert.Single(await reopened.SearchAsync(new SearchCriteria(text: "existing")));
    }

    [Fact]
    public void WindowsKeyStoreReloadsTheSameProtectedKey()
    {
        using var directory = new TemporaryDirectory();
        var keyPath = Path.Combine(directory.Path, "keys", "database.dpapi");
        var keyStore = new WindowsUserKeyStore();

        var firstKey = keyStore.LoadOrCreate(keyPath);
        var secondKey = keyStore.LoadOrCreate(keyPath);

        Assert.Equal(32, firstKey.Length);
        Assert.Equal(firstKey, secondKey);
        Assert.NotEqual(firstKey, File.ReadAllBytes(keyPath));
    }

    private static MailMessageRecord CreateMessage(
        string id,
        string subject,
        string body,
        bool isRead = false,
        bool isFlagged = false,
        bool hasAttachments = true,
        string folder = "Inbox",
        DateTimeOffset? receivedAt = null,
        string? accountId = null)
    {
        return new MailMessageRecord(
            id,
            "ana@example.com",
            "team@example.com",
            subject,
            body,
            receivedAt ?? DateTimeOffset.Parse("2026-10-12T12:00:00Z"),
            folder,
            isRead,
            isFlagged,
            hasAttachments,
            accountId);
    }

    private static MailAccountRecord CreateAccount(string id, string emailAddress)
    {
        return new MailAccountRecord(
            id,
            emailAddress,
            "Ana",
            "imap.example.com",
            993,
            MailTransportSecurity.SslOnConnect,
            "smtp.example.com",
            587,
            MailTransportSecurity.StartTls,
            MailAuthenticationMethod.OAuth2);
    }

    private static void CreateLegacyDatabase(string databasePath, byte[] key, int schemaVersion)
    {
        SQLitePCL.Batteries_V2.Init();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        var legacyIndex = schemaVersion switch
        {
            1 => """
                CREATE VIRTUAL TABLE mail_messages_fts USING fts5(
                    id UNINDEXED, sender, recipients, subject, body_text,
                    tokenize = 'unicode61 remove_diacritics 2'
                );
                INSERT INTO mail_messages_fts (id, sender, recipients, subject, body_text)
                VALUES ('legacy-1', 'ana@example.com', 'team@example.com', 'Legacy unique subject', 'Old body stays searchable');
                """,
            2 => """
                CREATE VIRTUAL TABLE mail_messages_fts USING fts5(
                    sender, recipients, subject, body_text,
                    content = 'mail_messages',
                    content_rowid = 'rowid',
                    tokenize = 'unicode61 remove_diacritics 2'
                );
                INSERT INTO mail_messages_fts(mail_messages_fts) VALUES ('rebuild');
                CREATE TRIGGER mail_messages_ai AFTER INSERT ON mail_messages BEGIN
                    INSERT INTO mail_messages_fts(rowid, sender, recipients, subject, body_text)
                    VALUES (new.rowid, new.sender, new.recipients, new.subject, new.body_text);
                END;
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(schemaVersion))
        };
        command.CommandText = $"""
            PRAGMA key = "x'{Convert.ToHexString(key)}'";
            CREATE TABLE mail_messages (
                id TEXT NOT NULL PRIMARY KEY,
                sender TEXT NOT NULL,
                recipients TEXT NOT NULL,
                subject TEXT NOT NULL,
                body_text TEXT NOT NULL,
                received_at_utc TEXT NOT NULL,
                folder TEXT NOT NULL,
                is_read INTEGER NOT NULL CHECK (is_read IN (0, 1)),
                is_flagged INTEGER NOT NULL CHECK (is_flagged IN (0, 1)),
                has_attachments INTEGER NOT NULL CHECK (has_attachments IN (0, 1))
            );
            INSERT INTO mail_messages
                (id, sender, recipients, subject, body_text, received_at_utc, folder, is_read, is_flagged, has_attachments)
            VALUES
                ('legacy-1', 'ana@example.com', 'team@example.com', 'Legacy unique subject',
                 'Old body stays searchable', '2026-10-12T12:00:00.0000000+00:00', 'Inbox', 0, 0, 1);
            """;
        command.ExecuteNonQuery();
        command.CommandText = $"{legacyIndex} PRAGMA user_version = {schemaVersion};";
        command.ExecuteNonQuery();
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
