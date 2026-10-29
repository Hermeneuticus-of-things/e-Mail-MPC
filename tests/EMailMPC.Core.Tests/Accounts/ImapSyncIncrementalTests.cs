using System.Runtime.Versioning;
using System.Security.Cryptography;
using EMailMPC.Core.Accounts;
using EMailMPC.Core.Search;
using EMailMPC.Core.Storage;

namespace EMailMPC.Core.Tests.Accounts;

[SupportedOSPlatform("windows")]
public sealed class ImapSyncIncrementalTests
{
    [Fact]
    public async Task FirstSyncFetchesNewestWindowAndStoresSyncState()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var account = CreateAccount();
        var folder = CreateInboxRecord(account);
        await database.SaveAccountAsync(account, [folder]);
        var fake = new FakeImapSyncClient();
        fake.AddFolder(folder.RemoteId, 42, CreateMessages(1, 5));

        var service = new ImapMailService(database, () => fake);
        var result = await service.SynchronizeAsync(account, "app-password", [folder]);

        Assert.Equal(5, result.NewMessageCount);
        Assert.Equal(1, result.ResyncedFolderCount);
        var stored = await database.SearchAsync(new SearchCriteria(accountId: account.Id));
        Assert.Equal(5, stored.Count);
        var state = await database.GetFolderSyncStateAsync(account.Id, SyncFolderId(account, folder));
        Assert.NotNull(state);
        Assert.Equal(42u, state.UidValidity);
        Assert.Equal(5u, state.HighestUid);
        Assert.Contains(fake.CallLog, call => call.StartsWith($"newest:{folder.RemoteId}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SecondSyncWithoutChangesFetchesNothingNew()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var account = CreateAccount();
        var folder = CreateInboxRecord(account);
        await database.SaveAccountAsync(account, [folder]);
        var fake = new FakeImapSyncClient();
        fake.AddFolder(folder.RemoteId, 42, CreateMessages(1, 5));
        var service = new ImapMailService(database, () => fake);

        await service.SynchronizeAsync(account, "app-password", [folder]);
        var result = await service.SynchronizeAsync(account, "app-password", [folder]);

        Assert.Equal(0, result.NewMessageCount);
        Assert.Equal(0, result.UpdatedFlagCount);
        Assert.Equal(0, result.DeletedMessageCount);
        var secondRunCalls = fake.CallLog.Skip(fake.CallLog.Count - 5).ToList();
        Assert.Contains(secondRunCalls, call => call.StartsWith("uidrange:", StringComparison.Ordinal));
        Assert.DoesNotContain(secondRunCalls, call => call.StartsWith("newest:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NewMailAboveHighestUidIsFetchedIncrementally()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var account = CreateAccount();
        var folder = CreateInboxRecord(account);
        await database.SaveAccountAsync(account, [folder]);
        var fake = new FakeImapSyncClient();
        fake.AddFolder(folder.RemoteId, 42, CreateMessages(1, 5));
        var service = new ImapMailService(database, () => fake);
        await service.SynchronizeAsync(account, "app-password", [folder]);

        fake.AppendMessage(folder.RemoteId, CreateMessage(6));
        var result = await service.SynchronizeAsync(account, "app-password", [folder]);

        Assert.Equal(1, result.NewMessageCount);
        Assert.Contains(fake.CallLog, call => call == $"uidrange:{folder.RemoteId}:6-{uint.MaxValue}");
        var state = await database.GetFolderSyncStateAsync(account.Id, SyncFolderId(account, folder));
        Assert.Equal(6u, state!.HighestUid);
        var loaded = await database.GetMessageAsync($"{account.Id}:{SyncFolderId(account, folder)}:6");
        Assert.NotNull(loaded);
        Assert.Equal("body 6", loaded.BodyText);
    }

    [Fact]
    public async Task ServerFlagChangePropagatesWithoutTouchingTheBody()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var account = CreateAccount();
        var folder = CreateInboxRecord(account);
        await database.SaveAccountAsync(account, [folder]);
        var fake = new FakeImapSyncClient();
        fake.AddFolder(folder.RemoteId, 42, CreateMessages(1, 5));
        var service = new ImapMailService(database, () => fake);
        await service.SynchronizeAsync(account, "app-password", [folder]);

        fake.MarkRead(folder.RemoteId, 3);
        var result = await service.SynchronizeAsync(account, "app-password", [folder]);

        Assert.Equal(1, result.UpdatedFlagCount);
        var loaded = await database.GetMessageAsync($"{account.Id}:{SyncFolderId(account, folder)}:3");
        Assert.NotNull(loaded);
        Assert.True(loaded.IsRead);
        Assert.Equal("body 3", loaded.BodyText);
    }

    [Fact]
    public async Task ServerExpungedMessageIsRemovedLocally()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var account = CreateAccount();
        var folder = CreateInboxRecord(account);
        await database.SaveAccountAsync(account, [folder]);
        var fake = new FakeImapSyncClient();
        fake.AddFolder(folder.RemoteId, 42, CreateMessages(1, 5));
        var service = new ImapMailService(database, () => fake);
        await service.SynchronizeAsync(account, "app-password", [folder]);

        fake.RemoveMessage(folder.RemoteId, 2);
        var result = await service.SynchronizeAsync(account, "app-password", [folder]);

        Assert.Equal(1, result.DeletedMessageCount);
        Assert.Null(await database.GetMessageAsync($"{account.Id}:{SyncFolderId(account, folder)}:2"));
        Assert.NotNull(await database.GetMessageAsync($"{account.Id}:{SyncFolderId(account, folder)}:1"));
        Assert.NotNull(await database.GetMessageAsync($"{account.Id}:{SyncFolderId(account, folder)}:5"));
    }

    [Fact]
    public async Task UidValidityChangePurgesLocalsAndResyncs()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var account = CreateAccount();
        var folder = CreateInboxRecord(account);
        await database.SaveAccountAsync(account, [folder]);
        var fake = new FakeImapSyncClient();
        fake.AddFolder(folder.RemoteId, 42, CreateMessages(1, 5));
        var service = new ImapMailService(database, () => fake);
        await service.SynchronizeAsync(account, "app-password", [folder]);

        fake.ResetFolder(folder.RemoteId, 77, CreateMessages(100, 102));
        var result = await service.SynchronizeAsync(account, "app-password", [folder]);

        Assert.Equal(1, result.ResyncedFolderCount);
        Assert.Equal(3, result.NewMessageCount);
        Assert.Null(await database.GetMessageAsync($"{account.Id}:{SyncFolderId(account, folder)}:1"));
        Assert.NotNull(await database.GetMessageAsync($"{account.Id}:{SyncFolderId(account, folder)}:100"));
        var state = await database.GetFolderSyncStateAsync(account.Id, SyncFolderId(account, folder));
        Assert.Equal(77u, state!.UidValidity);
        Assert.Equal(102u, state.HighestUid);
    }

    [Fact]
    public async Task EmptyFolderWritesZeroStateAndSurvivesResync()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var account = CreateAccount();
        var folder = CreateInboxRecord(account);
        await database.SaveAccountAsync(account, [folder]);
        var fake = new FakeImapSyncClient();
        fake.AddFolder(folder.RemoteId, 42, []);
        var service = new ImapMailService(database, () => fake);

        await service.SynchronizeAsync(account, "app-password", [folder]);
        var state = await database.GetFolderSyncStateAsync(account.Id, SyncFolderId(account, folder));
        Assert.NotNull(state);
        Assert.Equal(0u, state.HighestUid);

        var result = await service.SynchronizeAsync(account, "app-password", [folder]);
        Assert.Equal(0, result.NewMessageCount);
    }

    [Fact]
    public async Task MultipleFoldersSyncIndependently()
    {
        using var directory = new TemporaryDirectory();
        using var database = await CreateDatabaseAsync(directory);
        var account = CreateAccount();
        var inbox = CreateInboxRecord(account);
        var archive = new MailFolderRecord(
            $"{account.Id}:archive-folder",
            account.Id,
            "Archive",
            "Archive",
            "Archive",
            null,
            "archive");
        await database.SaveAccountAsync(account, [inbox, archive]);
        var fake = new FakeImapSyncClient();
        fake.AddFolder(inbox.RemoteId, 42, CreateMessages(1, 3));
        fake.AddFolder(archive.RemoteId, 9, CreateMessages(50, 51));
        var service = new ImapMailService(database, () => fake);

        await service.SynchronizeAsync(account, "app-password", [inbox, archive]);
        fake.AppendMessage(archive.RemoteId, CreateMessage(52));
        var result = await service.SynchronizeAsync(account, "app-password", [inbox, archive]);

        Assert.Equal(1, result.NewMessageCount);
        var inboxState = await database.GetFolderSyncStateAsync(account.Id, SyncFolderId(account, inbox));
        var archiveState = await database.GetFolderSyncStateAsync(account.Id, SyncFolderId(account, archive));
        Assert.Equal(3u, inboxState!.HighestUid);
        Assert.Equal(52u, archiveState!.HighestUid);
        Assert.Equal(9u, archiveState.UidValidity);
    }

    private static string SyncFolderId(MailAccountRecord account, MailFolderRecord folder)
    {
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(folder.FullName)));
        return $"{account.Id}:{hash}";
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

    private static List<ImapRemoteMessage> CreateMessages(uint fromUid, uint toUid)
    {
        var messages = new List<ImapRemoteMessage>();
        for (var uid = fromUid; uid <= toUid; uid++)
        {
            messages.Add(CreateMessage(uid));
        }

        return messages;
    }

    private static ImapRemoteMessage CreateMessage(uint uid)
    {
        return new ImapRemoteMessage(
            uid,
            "sender@example.com",
            "ana@example.com",
            $"Subject {uid}",
            DateTimeOffset.Parse("2026-10-30T10:00:00Z").AddMinutes(uid),
            IsRead: false,
            IsFlagged: false,
            HasAttachments: false,
            $"body {uid}");
    }

    private sealed class FakeImapSyncClient : IImapSyncClient
    {
        private sealed class FakeFolder
        {
            public uint UidValidity { get; set; }
            public Dictionary<uint, ImapRemoteMessage> Messages { get; } = new();
        }

        private readonly Dictionary<string, FakeFolder> _folders = new(StringComparer.Ordinal);

        public List<string> CallLog { get; } = [];

        public int SkippedLargeBodyCount => 0;

        public void AddFolder(string remoteId, uint uidValidity, IEnumerable<ImapRemoteMessage> messages)
        {
            var folder = new FakeFolder { UidValidity = uidValidity };
            foreach (var message in messages)
            {
                folder.Messages[message.Uid] = message;
            }

            _folders[remoteId] = folder;
        }

        public void AppendMessage(string remoteId, ImapRemoteMessage message)
        {
            _folders[remoteId].Messages[message.Uid] = message;
        }

        public void RemoveMessage(string remoteId, uint uid)
        {
            _folders[remoteId].Messages.Remove(uid);
        }

        public void MarkRead(string remoteId, uint uid)
        {
            var message = _folders[remoteId].Messages[uid];
            _folders[remoteId].Messages[uid] = message with { IsRead = true };
        }

        public void ResetFolder(string remoteId, uint uidValidity, IEnumerable<ImapRemoteMessage> messages)
        {
            AddFolder(remoteId, uidValidity, messages);
        }

        public Task ConnectAsync(MailAccountRecord account, string password, CancellationToken cancellationToken)
        {
            CallLog.Add("connect");
            return Task.CompletedTask;
        }

        public Task<ImapFolderSnapshot> OpenFolderAsync(string remoteId, CancellationToken cancellationToken)
        {
            var folder = _folders[remoteId];
            CallLog.Add($"open:{remoteId}");
            return Task.FromResult(new ImapFolderSnapshot(folder.Messages.Count, folder.UidValidity));
        }

        public Task<IReadOnlyList<ImapRemoteMessage>> FetchNewestAsync(
            string remoteId,
            int count,
            CancellationToken cancellationToken)
        {
            CallLog.Add($"newest:{remoteId}:{count}");
            var messages = _folders[remoteId].Messages.Values
                .OrderByDescending(message => message.Uid)
                .Take(count)
                .ToList();
            return Task.FromResult<IReadOnlyList<ImapRemoteMessage>>(messages);
        }

        public Task<IReadOnlyList<ImapRemoteMessage>> FetchUidRangeAsync(
            string remoteId,
            uint fromUid,
            uint toUid,
            CancellationToken cancellationToken)
        {
            CallLog.Add($"uidrange:{remoteId}:{fromUid}-{toUid}");
            var messages = _folders[remoteId].Messages.Values
                .Where(message => message.Uid >= fromUid && message.Uid <= toUid)
                .ToList();
            return Task.FromResult<IReadOnlyList<ImapRemoteMessage>>(messages);
        }

        public Task<IReadOnlyList<ImapRemoteMessage>> FetchFlagsAsync(
            string remoteId,
            uint fromUid,
            uint toUid,
            CancellationToken cancellationToken)
        {
            CallLog.Add($"flags:{remoteId}:{fromUid}-{toUid}");
            var messages = _folders[remoteId].Messages.Values
                .Where(message => message.Uid >= fromUid && message.Uid <= toUid)
                .Select(message => message with { BodyText = string.Empty })
                .ToList();
            return Task.FromResult<IReadOnlyList<ImapRemoteMessage>>(messages);
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
