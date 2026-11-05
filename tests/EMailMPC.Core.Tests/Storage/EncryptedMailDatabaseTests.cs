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
    public async Task PersistsMessageThreadHeadersAndMigratesVersionSevenSchema()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "mail.db");
        var key = RandomNumberGenerator.GetBytes(32);
        CreateLegacyDatabase(databasePath, key, schemaVersion: 7);

        using var database = new EncryptedMailDatabase(databasePath, key);
        database.Initialize();
        var message = CreateMessage("threaded-message", "Threaded", "Message body") with
        {
            MessageId = "<message@example.com>",
            InReplyTo = "<parent@example.com>",
            References = "<root@example.com> <parent@example.com>"
        };
        var draft = new MailDraftRecord(
            "threaded-draft",
            null,
            "sender@example.com",
            "Re: Threaded",
            "Reply text",
            DateTimeOffset.UtcNow,
            "<message@example.com>",
            "<root@example.com> <message@example.com>");

        await database.SaveMessageAsync(message);
        await database.SaveDraftAsync(draft);

        Assert.Equal(message, await database.GetMessageAsync(message.Id));
        Assert.Equal(draft, await database.GetDraftAsync(draft.Id));
    }

    [Fact]
    public async Task SentFolderFilterReturnsOnlyLocallySentMessagesForSelectedAccount()
    {
        using var directory = new TemporaryDirectory();
        using var database = new EncryptedMailDatabase(
            Path.Combine(directory.Path, "mail.db"),
            RandomNumberGenerator.GetBytes(32));
        database.Initialize();
        await database.SaveAccountAsync(CreateAccount("account-1", "ana@example.com"));
        var sent = CreateMessage(
            "sent-local:account-1:message-1",
            "Sent locally",
            "Accepted by SMTP",
            folder: "Sent (local)",
            accountId: "account-1");
        var inbox = CreateMessage(
            "inbox:account-1:message-1",
            "Received locally",
            "Stored from IMAP",
            folder: "Inbox",
            accountId: "account-1");
        await database.SaveMessagesAsync([sent, inbox]);

        var results = await database.SearchAsync(
            new SearchCriteria(folder: "Sent (local)", accountId: "account-1"));

        var result = Assert.Single(results);
        Assert.Equal(sent.Id, result.Id);
        Assert.Equal(sent.Subject, result.Subject);
    }

    [Fact]
    public async Task StoresAttachmentMetadataAndEncryptedContentWithSizeLimit()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "mail.db");
        var key = RandomNumberGenerator.GetBytes(32);
        var message = CreateMessage("message-with-attachment", "Report", "See attached");
        var attachment = new MailAttachmentRecord(
            "attachment-1",
            message.Id,
            "2",
            "report.pdf",
            "application/pdf",
            4,
            "cid-1",
            IsInline: false,
            AttachmentContentState.NotDownloaded);
        var content = new byte[] { 10, 20, 30, 40 };

        using (var database = new EncryptedMailDatabase(databasePath, key))
        {
            database.Initialize();
            await database.SaveMessageAsync(message);
            await database.ReplaceMessageAttachmentsAsync(message.Id, [attachment]);

            var storedMetadata = Assert.Single(await database.ListMessageAttachmentsAsync(message.Id));
            Assert.Equal(attachment.Id, storedMetadata.Id);
            Assert.Equal(AttachmentContentState.NotDownloaded, storedMetadata.ContentState);
            Assert.Null(await database.GetAttachmentContentAsync(attachment.Id));

            await database.SetAttachmentContentAsync(attachment.Id, content);
            var downloadedMetadata = Assert.Single(await database.ListMessageAttachmentsAsync(message.Id));
            Assert.Equal(AttachmentContentState.Downloaded, downloadedMetadata.ContentState);
            Assert.Equal(content, await database.GetAttachmentContentAsync(attachment.Id));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                database.SetAttachmentContentAsync(
                    attachment.Id,
                    new byte[EncryptedMailDatabase.MaximumAttachmentBytes + 1]));
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                database.SetAttachmentContentAsync("missing-attachment", content));

            await database.ReplaceMessageAttachmentsAsync(message.Id, [attachment]);
            Assert.Null(await database.GetAttachmentContentAsync(attachment.Id));
        }

        var storedBytes = await File.ReadAllBytesAsync(databasePath);
        Assert.DoesNotContain(content, storedBytes);
    }

    [Fact]
    public async Task DraftsPersistEncryptedUpdatesAndSurviveAccountRemoval()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "mail.db");
        var key = RandomNumberGenerator.GetBytes(32);
        var account = CreateAccount("draft-account", "draft@example.com");
        var updatedAttachmentContent = System.Text.Encoding.UTF8.GetBytes("private-draft-attachment-payload");
        var firstDraft = new MailDraftRecord(
            "draft-1",
            account.Id,
            "team@example.com",
            "First subject",
            "First body",
            DateTimeOffset.Parse("2026-10-30T12:00:00Z"));

        using (var database = new EncryptedMailDatabase(databasePath, key))
        {
            database.Initialize();
            await database.SaveAccountAsync(account);
            await database.SaveDraftAsync(firstDraft, [
                new MailDraftAttachmentRecord("first.bin", "application/octet-stream", [1, 2, 3])
            ]);
            await database.SaveDraftAsync(
                firstDraft with
                {
                    Subject = "Updated subject",
                    BodyText = "Updated body",
                    UpdatedAtUtc = firstDraft.UpdatedAtUtc.AddMinutes(1)
                },
                [new MailDraftAttachmentRecord("second.bin", "application/octet-stream", updatedAttachmentContent)]);

            var storedDraft = Assert.Single(await database.ListDraftsAsync(account.Id));
            Assert.Equal("Updated subject", storedDraft.Subject);
            Assert.Equal("Updated body", storedDraft.BodyText);
            Assert.Equal(storedDraft, await database.GetDraftAsync(storedDraft.Id));
            var storedAttachment = Assert.Single(await database.GetDraftAttachmentsAsync(storedDraft.Id));
            Assert.Equal("second.bin", storedAttachment.FileName);
            Assert.Equal(updatedAttachmentContent, storedAttachment.Content);

            await database.DeleteAccountAsync(account.Id);
            var preservedDraft = Assert.Single(await database.ListDraftsAsync());
            Assert.Null(preservedDraft.AccountId);
            Assert.Equal("Updated body", preservedDraft.BodyText);
            var preservedAttachment = Assert.Single(await database.GetDraftAttachmentsAsync(preservedDraft.Id));
            Assert.Equal(storedAttachment.FileName, preservedAttachment.FileName);
            Assert.Equal(storedAttachment.ContentType, preservedAttachment.ContentType);
            Assert.Equal(storedAttachment.Content, preservedAttachment.Content);
            await database.DeleteDraftAsync(preservedDraft.Id);
            Assert.Empty(await database.ListDraftsAsync());
            Assert.Empty(await database.GetDraftAttachmentsAsync(preservedDraft.Id));
        }

        var storedBytes = await File.ReadAllBytesAsync(databasePath);
        var storedText = System.Text.Encoding.UTF8.GetString(storedBytes);
        Assert.DoesNotContain("Updated subject", storedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Updated body", storedText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private-draft-attachment-payload",
            System.Text.Encoding.UTF8.GetString(storedBytes),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsDraftAttachmentsAboveCombinedLimitWithoutSavingDraft()
    {
        using var directory = new TemporaryDirectory();
        using var database = new EncryptedMailDatabase(
            Path.Combine(directory.Path, "mail.db"),
            RandomNumberGenerator.GetBytes(32));
        database.Initialize();

        var attachments = new[]
        {
            new MailDraftAttachmentRecord(
                "first.bin",
                "application/octet-stream",
                new byte[EncryptedMailDatabase.MaximumAttachmentBytes]),
            new MailDraftAttachmentRecord("second.bin", "application/octet-stream", [1])
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            database.SaveDraftAsync(
                new MailDraftRecord("oversized-draft", null, string.Empty, string.Empty, string.Empty, DateTimeOffset.UtcNow),
                attachments));
        Assert.Empty(await database.ListDraftsAsync());
    }

    [Fact]
    public async Task MigratesVersionSixDraftsToEncryptedAttachmentStorage()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "mail.db");
        var key = RandomNumberGenerator.GetBytes(32);
        var draft = new MailDraftRecord(
            "existing-draft",
            null,
            "team@example.com",
            "Existing draft",
            "Draft content remains intact.",
            DateTimeOffset.UtcNow);
        using (var database = new EncryptedMailDatabase(databasePath, key))
        {
            database.Initialize();
            await database.SaveDraftAsync(draft);
        }

        SQLitePCL.Batteries_V2.Init();
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
                DROP TABLE draft_attachments;
                PRAGMA user_version = 6;
                """;
            command.ExecuteNonQuery();
        }

        using var migrated = new EncryptedMailDatabase(databasePath, key);
        migrated.Initialize();

        Assert.Equal(draft, await migrated.GetDraftAsync(draft.Id));
        Assert.Empty(await migrated.GetDraftAttachmentsAsync(draft.Id));
        await migrated.SaveDraftAsync(draft, [
            new MailDraftAttachmentRecord("new.bin", "application/octet-stream", [1, 2, 3])
        ]);
        Assert.Equal([1, 2, 3], Assert.Single(await migrated.GetDraftAttachmentsAsync(draft.Id)).Content);
        Assert.Single(Directory.GetFiles(directory.Path, "mail.db.pre-migration-v6-*.db"));
    }

    [Fact]
    public async Task MigratesVersionFourDatabaseToLatestMailSchema()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "mail.db");
        var key = RandomNumberGenerator.GetBytes(32);
        var account = CreateAccount("migration-account", "migration@example.com");
        var folder = new MailFolderRecord(
            "migration-folder",
            account.Id,
            "INBOX",
            "Inbox",
            "INBOX",
            null,
            "inbox");
        using (var database = new EncryptedMailDatabase(databasePath, key))
        {
            database.Initialize();
            await database.SaveAccountAsync(account, [folder]);
            await database.SaveMessageAsync(CreateMessage("pre-attachment-schema", "Existing mail", "Preserve this body"));
            await database.SetFolderSyncStateAsync(new FolderSyncStateRecord(account.Id, account.Id, 41, 5));
        }

        SQLitePCL.Batteries_V2.Init();
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
                DROP TABLE attachments;
                DROP TABLE mail_drafts;
                PRAGMA user_version = 4;
                """;
            command.ExecuteNonQuery();
        }

        using var migrated = new EncryptedMailDatabase(databasePath, key);
        migrated.Initialize();

        var snapshotPath = Assert.Single(Directory.GetFiles(
            directory.Path,
            "mail.db.pre-migration-v4-*.db"));
        using (var snapshot = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = snapshotPath,
                   Pooling = false
               }.ToString()))
        {
            snapshot.Open();
            using var keyCommand = snapshot.CreateCommand();
            keyCommand.CommandText = $"PRAGMA key = \"x'{Convert.ToHexString(key)}'\";";
            keyCommand.ExecuteNonQuery();

            using var versionCommand = snapshot.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version;";
            Assert.Equal(4, Convert.ToInt32(versionCommand.ExecuteScalar()));

            using var messageCommand = snapshot.CreateCommand();
            messageCommand.CommandText = "SELECT body_text FROM mail_messages WHERE id = 'pre-attachment-schema';";
            Assert.Equal("Preserve this body", messageCommand.ExecuteScalar()?.ToString());

            using var integrityCommand = snapshot.CreateCommand();
            integrityCommand.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", integrityCommand.ExecuteScalar()?.ToString());
        }

        var preserved = await migrated.GetMessageAsync("pre-attachment-schema");
        Assert.NotNull(preserved);
        Assert.Equal("Preserve this body", preserved.BodyText);
        Assert.Null(await migrated.GetFolderSyncStateAsync(account.Id, account.Id));
        await migrated.ReplaceMessageAttachmentsAsync(
            preserved.Id,
            [new MailAttachmentRecord(
                "migrated-attachment",
                preserved.Id,
                "2",
                "report.pdf",
                "application/pdf",
                0,
                null,
                false,
                AttachmentContentState.NotDownloaded)]);
        Assert.Single(await migrated.ListMessageAttachmentsAsync(preserved.Id));
        await migrated.SaveDraftAsync(
            new MailDraftRecord(
                "migrated-draft",
                account.Id,
                string.Empty,
                string.Empty,
                "Drafts are available after migration.",
                DateTimeOffset.UtcNow),
            [new MailDraftAttachmentRecord("migrated.txt", "application/octet-stream", [7, 8, 9])]);
        Assert.Single(await migrated.ListDraftsAsync(account.Id));
        Assert.Equal([7, 8, 9], Assert.Single(await migrated.GetDraftAttachmentsAsync("migrated-draft")).Content);
    }

    [Fact]
    public async Task FailedSchemaMigrationCanBeRetriedAfterRepairWithoutLosingMail()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "mail.db");
        var key = RandomNumberGenerator.GetBytes(32);
        using (var database = new EncryptedMailDatabase(databasePath, key))
        {
            database.Initialize();
            await database.SaveMessageAsync(CreateMessage(
                "migration-rollback-message",
                "Preserved message",
                "Original database stays readable."));
        }

        SQLitePCL.Batteries_V2.Init();
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
                DROP TABLE attachments;
                DROP TABLE mail_drafts;
                CREATE TABLE mail_drafts (id TEXT NOT NULL PRIMARY KEY);
                PRAGMA user_version = 4;
                """;
            command.ExecuteNonQuery();
        }

        using var migration = new EncryptedMailDatabase(databasePath, key);
        Assert.Throws<SqliteException>(() => migration.Initialize());

        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = databasePath,
                   Pooling = false
               }.ToString()))
        {
            connection.Open();
            using var keyCommand = connection.CreateCommand();
            keyCommand.CommandText = $"PRAGMA key = \"x'{Convert.ToHexString(key)}'\";";
            keyCommand.ExecuteNonQuery();

            using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version;";
            Assert.Equal(4, Convert.ToInt32(versionCommand.ExecuteScalar()));

            using var messageCommand = connection.CreateCommand();
            messageCommand.CommandText = "SELECT body_text FROM mail_messages WHERE id = 'migration-rollback-message';";
            Assert.Equal("Original database stays readable.", messageCommand.ExecuteScalar()?.ToString());

            using var integrityCommand = connection.CreateCommand();
            integrityCommand.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", integrityCommand.ExecuteScalar()?.ToString());
        }

        var snapshotPath = Assert.Single(Directory.GetFiles(
            directory.Path,
            "mail.db.pre-migration-v4-*.db"));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
        using var preservedSnapshot = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Pooling = false
        }.ToString());
        preservedSnapshot.Open();
        using var snapshotKeyCommand = preservedSnapshot.CreateCommand();
        snapshotKeyCommand.CommandText = $"PRAGMA key = \"x'{Convert.ToHexString(key)}'\";";
        snapshotKeyCommand.ExecuteNonQuery();
        using var snapshotMessageCommand = preservedSnapshot.CreateCommand();
        snapshotMessageCommand.CommandText = "SELECT body_text FROM mail_messages WHERE id = 'migration-rollback-message';";
        Assert.Equal("Original database stays readable.", snapshotMessageCommand.ExecuteScalar()?.ToString());

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
                DROP TABLE mail_drafts;
                """;
            command.ExecuteNonQuery();
        }

        using var retriedMigration = new EncryptedMailDatabase(databasePath, key);
        retriedMigration.Initialize();
        var recoveredMessage = await retriedMigration.GetMessageAsync("migration-rollback-message");
        Assert.NotNull(recoveredMessage);
        Assert.Equal("Original database stays readable.", recoveredMessage.BodyText);
        Assert.Equal(2, Directory.GetFiles(directory.Path, "mail.db.pre-migration-v4-*.db").Length);
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
    public async Task DeletingAccountKeepsLocalMessagesAndAttachmentsAndRemovesItsFolders()
    {
        using var directory = new TemporaryDirectory();
        var key = RandomNumberGenerator.GetBytes(32);
        using var database = new EncryptedMailDatabase(Path.Combine(directory.Path, "mail.db"), key);
        database.Initialize();
        var account = CreateAccount("account-1", "ana@example.com");
        await database.SaveAccountAsync(account, [
            new MailFolderRecord("folder-1", account.Id, "remote-inbox", "Inbox", "Inbox", null, "inbox")
        ]);
        var message = CreateMessage("message-1", "Preserve local mail", "Keep this locally", accountId: account.Id);
        await database.SaveMessageAsync(message);
        var attachment = new MailAttachmentRecord(
            "attachment-1",
            message.Id,
            "2",
            "preserved.pdf",
            "application/pdf",
            4,
            null,
            IsInline: false,
            AttachmentContentState.NotDownloaded);
        await database.ReplaceMessageAttachmentsAsync(message.Id, [attachment]);
        var attachmentContent = new byte[] { 10, 20, 30, 40 };
        await database.SetAttachmentContentAsync(attachment.Id, attachmentContent);

        await database.DeleteAccountAsync(account.Id);

        Assert.Empty(await database.ListAccountsAsync());
        Assert.Empty(await database.ListFoldersAsync(account.Id));
        var preserved = await database.SearchAsync(new SearchCriteria(text: "preserve"));
        Assert.Equal("message-1", Assert.Single(preserved).Id);
        Assert.Null(preserved[0].AccountId);
        Assert.Equal(attachmentContent, await database.GetAttachmentContentAsync(attachment.Id));
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

    [WindowsFact]
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

    [WindowsFact]
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
            7 => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(schemaVersion))
        };
        var accountIdColumn = schemaVersion >= 3 ? ", account_id TEXT" : string.Empty;
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
                {accountIdColumn}
            );
            CREATE TABLE IF NOT EXISTS mail_drafts (
                id TEXT NOT NULL PRIMARY KEY,
                account_id TEXT,
                recipients TEXT NOT NULL,
                subject TEXT NOT NULL,
                body_text TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
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
