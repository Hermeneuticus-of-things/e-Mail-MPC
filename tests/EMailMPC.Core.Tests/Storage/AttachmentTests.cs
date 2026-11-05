using System.Linq;
using System.Security.Cryptography;
using System.Runtime.Versioning;
using EMailMPC.Core.Accounts;
using EMailMPC.Core.Storage;

namespace EMailMPC.Core.Tests.Storage;

/// <summary>
/// Acceptance suite for the attachment feature (Phase A-2): metadata captured at
/// sync from the IMAP BODYSTRUCTURE without downloading, on-demand download into
/// the encrypted store, per-message listing, cascade delete, content limits, and
/// draft/replace attachment round-trips.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AttachmentTests
{
    private const string MessageId = "acc-1:inbox:1";
    private const string AttachmentId = MessageId + ":2";
    private static readonly byte[] SampleContent = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31];

    [Fact]
    public async Task SyncPersistsInlineMetadataFieldsWithoutContent()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var account = CreateAccount();
        var folder = CreateInboxRecord(account);
        await database.SaveAccountAsync(account, [folder]);
        var inline = new ImapRemoteAttachment(
            "3",
            "logo.png",
            "image/png",
            2048,
            "logo-cid@example.com",
            IsInline: true);
        var remoteMessage = CreateMessage(7) with
        {
            HasAttachments = true,
            Attachments = [inline]
        };
        var fake = new FakeImapSyncClient();
        fake.AddFolder(folder.RemoteId, 11, [remoteMessage]);
        var service = new ImapMailService(database, () => fake);

        await service.SynchronizeAsync(account, "app-password", [folder]);

        var messageId = $"{account.Id}:{SyncFolderId(account, folder)}:7";
        var attachment = Assert.Single(await database.ListMessageAttachmentsAsync(messageId));
        Assert.Equal("3", attachment.PartSpecifier);
        Assert.Equal("logo.png", attachment.FileName);
        Assert.Equal("image/png", attachment.ContentType);
        Assert.Equal(2048, attachment.SizeBytes);
        Assert.Equal("logo-cid@example.com", attachment.ContentId);
        Assert.True(attachment.IsInline);
        Assert.Equal(AttachmentContentState.NotDownloaded, attachment.ContentState);
        Assert.Null(await database.GetAttachmentContentAsync(attachment.Id));
    }

    [Fact]
    public async Task DownloadedContentIsStoredEncryptedAndMarkedDownloaded()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "mail.db");
        var key = RandomNumberGenerator.GetBytes(32);
        var (messageId, attachmentId, downloaded) = await DownloadSampleAttachmentAsync(databasePath, key);

        var storedBytes = await File.ReadAllBytesAsync(databasePath);
        Assert.True(storedBytes.AsSpan().IndexOf(SampleContent) < 0, "attachment content leaked in plaintext");

        using var reopened = new EncryptedMailDatabase(databasePath, key);
        reopened.Initialize();
        var record = Assert.Single(await reopened.ListMessageAttachmentsAsync(messageId));
        Assert.Equal(AttachmentContentState.Downloaded, record.ContentState);
        Assert.Equal(SampleContent, downloaded);
        Assert.Equal(downloaded, await reopened.GetAttachmentContentAsync(attachmentId));
    }

    [Fact]
    public async Task OversizeDownloadIsRejectedBeforeConnecting()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var account = CreateAccount();
        var folder = CreateInboxRecord(account);
        await database.SaveAccountAsync(account, [folder]);
        var folderId = SyncFolderId(account, folder);
        var messageId = $"{account.Id}:{folderId}:1";
        var attachmentId = AttachmentIdFrom(messageId, "2");
        await database.SaveMessagesAsync([CreateStoredMessage(messageId)]);
        await database.ReplaceMessageAttachmentsAsync(messageId, [
            new MailAttachmentRecord(
                attachmentId, messageId, "2", "huge.zip", "application/zip",
                EncryptedMailDatabase.MaximumAttachmentBytes + 1L, null, false,
                AttachmentContentState.NotDownloaded),
        ]);
        var attachment = Assert.Single(await database.ListMessageAttachmentsAsync(messageId));
        var fake = new FakeImapSyncClient();
        var service = new ImapMailService(database, () => fake);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.DownloadAttachmentAsync(account, "app-password", folder, 1, attachment));
        Assert.Empty(fake.CallLog);
        Assert.Null(await database.GetAttachmentContentAsync(attachmentId));
    }

    [Fact]
    public async Task AttachmentListsStayScopedPerMessage()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var (messageA, messageB) = await SaveTwoMessagesWithAttachmentsAsync(database);

        var recordA = Assert.Single(await database.ListMessageAttachmentsAsync(messageA.Id));
        Assert.Equal("invoice.pdf", recordA.FileName);
        var recordB = Assert.Single(await database.ListMessageAttachmentsAsync(messageB.Id));
        Assert.Equal("photo.jpg", recordB.FileName);
        Assert.Empty(await database.ListMessageAttachmentsAsync("no-such-message"));
    }

    [Fact]
    public async Task DeletingMessageCascadesItsAttachments()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var (messageA, _) = await SaveTwoMessagesWithAttachmentsAsync(database);
        var attachment = Assert.Single(await database.ListMessageAttachmentsAsync(messageA.Id));

        await database.DeleteMessagesAsync([messageA.Id]);

        Assert.Empty(await database.ListMessageAttachmentsAsync(messageA.Id));
        Assert.Null(await database.GetAttachmentContentAsync(attachment.Id));
    }

    [Fact]
    public async Task SetAttachmentContentPersistsBytesAndDownloadedState()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var (message, _) = await SaveTwoMessagesWithAttachmentsAsync(database);
        var attachment = Assert.Single(await database.ListMessageAttachmentsAsync(message.Id));
        Assert.Equal(AttachmentContentState.NotDownloaded, attachment.ContentState);

        await database.SetAttachmentContentAsync(attachment.Id, SampleContent);

        var stored = Assert.Single(await database.ListMessageAttachmentsAsync(message.Id));
        Assert.Equal(AttachmentContentState.Downloaded, stored.ContentState);
        Assert.Equal(SampleContent.Length, stored.SizeBytes);
        Assert.Equal(SampleContent, await database.GetAttachmentContentAsync(attachment.Id));
    }

    [Fact]
    public async Task SetAttachmentContentRejectsOversizeBytesBeforeWriting()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var (message, _) = await SaveTwoMessagesWithAttachmentsAsync(database);
        var attachment = Assert.Single(await database.ListMessageAttachmentsAsync(message.Id));
        var oversize = new byte[EncryptedMailDatabase.MaximumAttachmentBytes + 1];

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            database.SetAttachmentContentAsync(attachment.Id, oversize));
        Assert.Null(await database.GetAttachmentContentAsync(attachment.Id));
    }

    [Fact]
    public async Task DraftAttachmentsRoundTripAndCascadeOnDraftDelete()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var draft = new MailDraftRecord(
            "draft-1",
            null,
            "bob@example.com",
            "With files",
            "See attached.",
            DateTimeOffset.UtcNow);
        var attachments = new List<MailDraftAttachmentRecord>
        {
            new("sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [9, 8, 7]),
            new("notes.txt", "text/plain", [0x61, 0x62]),
        };

        await database.SaveDraftAsync(draft, attachments);
        var loaded = await database.GetDraftAttachmentsAsync(draft.Id);
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, a => a.FileName == "sheet.xlsx" && a.Content.SequenceEqual<byte>([(byte)9, (byte)8, (byte)7]));
        Assert.Contains(loaded, a => a.FileName == "notes.txt");

        await database.DeleteDraftAsync(draft.Id);

        Assert.Empty(await database.GetDraftAttachmentsAsync(draft.Id));
    }

    [Fact]
    public async Task ReplaceMessageAttachmentsRemovesStaleEntriesAndKeepsOthers()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var (messageA, messageB) = await SaveTwoMessagesWithAttachmentsAsync(database);
        var original = Assert.Single(await database.ListMessageAttachmentsAsync(messageA.Id));

        var replacement = original with
        {
            Id = $"{messageA.Id}:replacement",
            PartSpecifier = "4",
            FileName = "invoice-signed.pdf",
            SizeBytes = SampleContent.Length,
            ContentState = AttachmentContentState.Downloaded,
        };
        await database.ReplaceMessageAttachmentsAsync(messageA.Id, [replacement]);
        await database.SetAttachmentContentAsync(replacement.Id, SampleContent);

        var replaced = Assert.Single(await database.ListMessageAttachmentsAsync(messageA.Id));
        Assert.Equal("invoice-signed.pdf", replaced.FileName);
        Assert.Null(await database.GetAttachmentContentAsync(original.Id));
        Assert.Equal(SampleContent, await database.GetAttachmentContentAsync(replacement.Id));
        Assert.Single(await database.ListMessageAttachmentsAsync(messageB.Id));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            database.ReplaceMessageAttachmentsAsync(messageA.Id, [replacement with { FileName = " " }]));
        Assert.Single(await database.ListMessageAttachmentsAsync(messageA.Id));
    }

    private static async Task<(string MessageId, string AttachmentId, byte[] Content)> DownloadSampleAttachmentAsync(
        string databasePath,
        byte[] key)
    {
        using var database = new EncryptedMailDatabase(databasePath, key);
        database.Initialize();
        var account = CreateAccount();
        var folder = CreateInboxRecord(account);
        await database.SaveAccountAsync(account, [folder]);
        var folderId = SyncFolderId(account, folder);
        var messageId = $"{account.Id}:{folderId}:1";
        var attachmentId = AttachmentIdFrom(messageId, "2");
        await database.SaveMessagesAsync([CreateStoredMessage(messageId)]);
        await database.ReplaceMessageAttachmentsAsync(messageId, [
            new MailAttachmentRecord(
                attachmentId, messageId, "2", "report.pdf", "application/pdf",
                SampleContent.Length, null, false, AttachmentContentState.NotDownloaded),
        ]);
        var remoteAttachment = new ImapRemoteAttachment(
            "2",
            "report.pdf",
            "application/pdf",
            SampleContent.Length,
            null,
            IsInline: false);
        var remoteMessage = CreateMessage(1) with
        {
            HasAttachments = true,
            Attachments = [remoteAttachment]
        };
        var fake = new FakeImapSyncClient();
        fake.AddFolder(folder.RemoteId, 3, [remoteMessage]);
        fake.SetAttachmentContent(folder.RemoteId, 1, "2", SampleContent);
        var service = new ImapMailService(database, () => fake);
        var attachment = Assert.Single(await database.ListMessageAttachmentsAsync(messageId));

        var content = await service.DownloadAttachmentAsync(account, "app-password", folder, 1, attachment);
        return (messageId, attachmentId, content);
    }

    private static string AttachmentIdFrom(string messageId, string partSpecifier)
    {
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(partSpecifier)));
        return $"{messageId}:attachment:{hash}";
    }

    private static async Task<(MailMessageRecord MessageA, MailMessageRecord MessageB)> SaveTwoMessagesWithAttachmentsAsync(
        EncryptedMailDatabase database)
    {
        var account = CreateAccount();
        var folder = CreateInboxRecord(account);
        await database.SaveAccountAsync(account, [folder]);
        var messageA = CreateStoredMessage("acc-1:inbox:1", "Invoice month 1");
        var messageB = CreateStoredMessage("acc-1:inbox:2", "Photos month 1");
        await database.SaveMessagesAsync([messageA, messageB]);
        await database.ReplaceMessageAttachmentsAsync(messageA.Id, [
            new MailAttachmentRecord(
                $"{messageA.Id}:attachment", messageA.Id, "2", "invoice.pdf", "application/pdf",
                10, null, false, AttachmentContentState.NotDownloaded),
        ]);
        await database.ReplaceMessageAttachmentsAsync(messageB.Id, [
            new MailAttachmentRecord(
                $"{messageB.Id}:attachment", messageB.Id, "2", "photo.jpg", "image/jpeg",
                20, null, false, AttachmentContentState.NotDownloaded),
        ]);
        return (messageA, messageB);
    }

    private static MailMessageRecord CreateStoredMessage(
        string id = MessageId,
        string subject = "Quarterly report")
    {
        return new MailMessageRecord(
            id,
            "ana@example.com",
            "bob@example.com",
            subject,
            "Please review the attachment.",
            new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero),
            "INBOX",
            false,
            false,
            true,
            "acc-1");
    }

    private static async Task<EncryptedMailDatabase> CreateDatabaseAsync(TemporaryDirectory directory)
    {
        var database = new EncryptedMailDatabase(
            Path.Combine(directory.Path, "mail.db"),
            RandomNumberGenerator.GetBytes(32));
        database.Initialize();
        return database;
    }

    private static MailAccountRecord CreateAccount()
    {
        return new MailAccountRecord(
            "acc-1",
            "ana@example.com",
            "Ana",
            "imap.example.com",
            993,
            MailTransportSecurity.SslOnConnect,
            "smtp.example.com",
            587,
            MailTransportSecurity.StartTls,
            MailAuthenticationMethod.AppPassword);
    }

    private static MailFolderRecord CreateInboxRecord(MailAccountRecord account)
    {
        return new MailFolderRecord(
            $"{account.Id}:inbox-folder",
            account.Id,
            "INBOX",
            "Inbox",
            "INBOX",
            null,
            "inbox");
    }

    private static ImapRemoteMessage CreateMessage(uint uid)
    {
        return new ImapRemoteMessage(
            uid,
            "ana@example.com",
            "bob@example.com",
            $"Subject {uid}",
            new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero),
            IsRead: false,
            IsFlagged: false,
            HasAttachments: false);
    }

    private static string SyncFolderId(MailAccountRecord account, MailFolderRecord folder)
    {
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(folder.FullName)));
        return $"{account.Id}:{hash}";
    }

    private sealed class FakeImapSyncClient : IImapSyncClient
    {
        public List<string> CallLog { get; } = [];

        private readonly Dictionary<string, (uint UidValidity, List<ImapRemoteMessage> Messages)> _folders = [];
        private readonly Dictionary<(string RemoteId, uint Uid, string PartSpecifier), byte[]> _contents = [];

        public void AddFolder(string remoteId, uint uidValidity, List<ImapRemoteMessage> messages)
        {
            _folders[remoteId] = (uidValidity, new List<ImapRemoteMessage>(messages));
        }

        public void SetAttachmentContent(string remoteId, uint uid, string partSpecifier, byte[] content)
        {
            _contents[(remoteId, uid, partSpecifier)] = content;
        }

        public int SkippedLargeBodyCount => 0;

        public Task ConnectAsync(MailAccountRecord account, string password, CancellationToken cancellationToken)
        {
            CallLog.Add("connect");
            return Task.CompletedTask;
        }

        public Task<ImapFolderSnapshot> OpenFolderAsync(string remoteId, CancellationToken cancellationToken)
        {
            CallLog.Add($"open:{remoteId}");
            var (uidValidity, messages) = _folders.TryGetValue(remoteId, out var folder)
                ? folder
                : (1u, new List<ImapRemoteMessage>());
            return Task.FromResult(new ImapFolderSnapshot(messages.Count, uidValidity));
        }

        public Task<IReadOnlyList<ImapRemoteMessage>> FetchNewestAsync(
            string remoteId,
            int count,
            CancellationToken cancellationToken)
        {
            CallLog.Add($"newest:{remoteId}:{count}");
            IReadOnlyList<ImapRemoteMessage> messages = _folders.TryGetValue(remoteId, out var folder)
                ? folder.Messages.Take(count).ToList()
                : [];
            return Task.FromResult(messages);
        }

        public Task<IReadOnlyList<ImapRemoteMessage>> FetchUidRangeAsync(
            string remoteId,
            uint fromUid,
            uint toUid,
            CancellationToken cancellationToken)
        {
            CallLog.Add($"range:{remoteId}:{fromUid}-{toUid}");
            IReadOnlyList<ImapRemoteMessage> messages = _folders.TryGetValue(remoteId, out var folder)
                ? folder.Messages.Where(m => m.Uid >= fromUid && m.Uid <= toUid).ToList()
                : [];
            return Task.FromResult(messages);
        }

        public Task<IReadOnlyList<ImapRemoteMessage>> FetchFlagsAsync(
            string remoteId,
            uint fromUid,
            uint toUid,
            CancellationToken cancellationToken)
        {
            CallLog.Add($"flags:{remoteId}:{fromUid}-{toUid}");
            IReadOnlyList<ImapRemoteMessage> messages = _folders.TryGetValue(remoteId, out var folder)
                ? folder.Messages.Where(m => m.Uid >= fromUid && m.Uid <= toUid).ToList()
                : [];
            return Task.FromResult(messages);
        }

        public Task<byte[]> DownloadAttachmentAsync(
            string remoteId,
            uint uid,
            string partSpecifier,
            CancellationToken cancellationToken)
        {
            CallLog.Add($"download:{remoteId}:{uid}:{partSpecifier}");
            if (_contents.TryGetValue((remoteId, uid, partSpecifier), out var content))
            {
                return Task.FromResult(content);
            }

            throw new InvalidDataException("The requested attachment content was not staged in the fake.");
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            CallLog.Add("disconnect");
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "emailmpc-attachment-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
