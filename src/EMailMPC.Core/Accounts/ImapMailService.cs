using EMailMPC.Core.Storage;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace EMailMPC.Core.Accounts;

/// <summary>
/// Orchestrates incremental IMAP synchronization: per-folder sync state
/// (UIDVALIDITY + highest UID), UID-range fetch for newly arrived mail, a bounded
/// flag refresh that also removes locally stored messages expunged server-side,
/// and a full bounded re-sync whenever the server reports a new UIDVALIDITY.
/// </summary>
public sealed class ImapMailService(EncryptedMailDatabase database, Func<IImapSyncClient>? clientFactory = null)
{
    private const int MessagesPerFolderLimit = 200;
    private const int FlagsRefreshLimit = 100;

    public async Task<IReadOnlyList<MailFolderRecord>> TestAndListFoldersAsync(
        MailAccountRecord account,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        EnsureSupportedAuthentication(account);
        using var client = CreateClient();
        await client.ConnectAsync(
            account.ImapHost,
            account.ImapPort,
            GetSocketOptions(account.ImapSecurity),
            cancellationToken).ConfigureAwait(false);
        await client.AuthenticateAsync(account.EmailAddress, password, cancellationToken).ConfigureAwait(false);

        if (client.PersonalNamespaces.Count == 0)
        {
            throw new InvalidOperationException("The IMAP server did not expose a personal mailbox namespace.");
        }

        var remoteFolders = await client.GetFoldersAsync(
            client.PersonalNamespaces[0],
            StatusItems.None,
            subscribedOnly: false,
            cancellationToken).ConfigureAwait(false);
        var folders = remoteFolders.Select(folder => new MailFolderRecord(
                GetFolderId(account.Id, folder.FullName),
                account.Id,
                folder.FullName,
                folder.Name,
                folder.FullName,
                folder.ParentFolder?.FullName,
                GetSpecialUse(folder.Attributes)))
            .OrderBy(folder => folder.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
        return folders;
    }

    public async Task<ImapSyncResult> SynchronizeAsync(
        MailAccountRecord account,
        string password,
        IReadOnlyCollection<MailFolderRecord> folders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentNullException.ThrowIfNull(folders);

        EnsureSupportedAuthentication(account);
        using var client = clientFactory is not null ? clientFactory() : new MailKitImapSyncClient();
        await client.ConnectAsync(account, password, cancellationToken).ConfigureAwait(false);

        var newMessageCount = 0;
        var updatedFlagCount = 0;
        var deletedMessageCount = 0;
        var resyncedFolderCount = 0;
        foreach (var folderRecord in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folderId = GetFolderId(account.Id, folderRecord.FullName);
            var snapshot = await client.OpenFolderAsync(folderRecord.RemoteId, cancellationToken).ConfigureAwait(false);
            var state = await database.GetFolderSyncStateAsync(account.Id, folderId, cancellationToken).ConfigureAwait(false);

            uint highestUid;
            int folderNewMessages;
            var updatedFlagCountFolder = 0;
            var deletedMessageCountFolder = 0;
            var resyncedFolder = false;
            if (state is null || state.UidValidity != snapshot.UidValidity)
            {
                if (state is not null)
                {
                    // UIDVALIDITY changed: stored UIDs no longer name the same messages.
                    await database.DeleteFolderMessagesAsync(account.Id, folderId, cancellationToken).ConfigureAwait(false);
                }

                var newest = await client.FetchNewestAsync(folderRecord.RemoteId, MessagesPerFolderLimit, cancellationToken).ConfigureAwait(false);
                await database.SaveMessagesAsync(
                    newest.Select(message => ToRecord(account.Id, folderId, folderRecord.FullName, message)).ToList(),
                    cancellationToken).ConfigureAwait(false);
                highestUid = newest.Count == 0 ? 0 : newest.Max(message => message.Uid);
                folderNewMessages = newest.Count;
                resyncedFolder = true;
            }
            else
            {
                var arrived = await client.FetchUidRangeAsync(
                    folderRecord.RemoteId,
                    state.HighestUid + 1,
                    uint.MaxValue,
                    cancellationToken).ConfigureAwait(false);
                await database.SaveMessagesAsync(
                    arrived.Select(message => ToRecord(account.Id, folderId, folderRecord.FullName, message)).ToList(),
                    cancellationToken).ConfigureAwait(false);
                highestUid = arrived.Count == 0 ? state.HighestUid : Math.Max(state.HighestUid, arrived.Max(message => message.Uid));
                folderNewMessages = arrived.Count;

                var (updatedFlags, deletedLocal) = await RefreshFlagsAndDeletionsAsync(
                    client,
                    folderRecord.RemoteId,
                    account.Id,
                    folderId,
                    highestUid,
                    cancellationToken).ConfigureAwait(false);
                updatedFlagCountFolder = updatedFlags;
                deletedMessageCountFolder = deletedLocal;
            }

            newMessageCount += folderNewMessages;
            updatedFlagCount += updatedFlagCountFolder;
            deletedMessageCount += deletedMessageCountFolder;
            if (resyncedFolder)
            {
                resyncedFolderCount++;
            }

            await database.SetFolderSyncStateAsync(
                new FolderSyncStateRecord(account.Id, folderId, snapshot.UidValidity, highestUid),
                cancellationToken).ConfigureAwait(false);
        }

        var skippedLargeBodies = client.SkippedLargeBodyCount;
        await client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        return new ImapSyncResult(
            newMessageCount,
            skippedLargeBodies,
            newMessageCount,
            updatedFlagCount,
            deletedMessageCount,
            resyncedFolderCount);
    }

    /// <summary>
    /// Refreshes flags for the newest locally known UIDs and deletes local messages
    /// the server no longer reports in that window (UIDs are never reused while
    /// UIDVALIDITY is stable, so absence means expunged).
    /// </summary>
    private async Task<(int UpdatedFlags, int DeletedLocal)> RefreshFlagsAndDeletionsAsync(
        IImapSyncClient client,
        string remoteId,
        string accountId,
        string folderId,
        uint highestUid,
        CancellationToken cancellationToken)
    {
        var allFlags = await database.ListFolderMessageFlagsAsync(accountId, folderId, cancellationToken).ConfigureAwait(false);
        var stubs = allFlags
            .Select(flags => (flags.Id, Uid: TryParseUid(flags.Id)))
            .Where(stub => stub.Uid is not null)
            .Select(stub => (stub.Id, Uid: stub.Uid!.Value))
            .OrderByDescending(stub => stub.Uid)
            .Take(FlagsRefreshLimit)
            .ToList();
        if (stubs.Count == 0)
        {
            return (0, 0);
        }

        var minUid = stubs.Min(stub => stub.Uid);
        var maxUid = Math.Max(stubs.Max(stub => stub.Uid), highestUid);
        var server = await client.FetchFlagsAsync(remoteId, minUid, maxUid, cancellationToken).ConfigureAwait(false);
        var byUid = server.ToDictionary(message => message.Uid);

        var flagsById = allFlags.ToDictionary(flags => flags.Id);
        var updatedFlags = 0;
        var deletedIds = new List<string>();
        foreach (var (id, uid) in stubs)
        {
            if (!byUid.TryGetValue(uid, out var remote))
            {
                deletedIds.Add(id);
                continue;
            }

            var local = flagsById[id];
            if (local.IsRead != remote.IsRead
                || local.IsFlagged != remote.IsFlagged
                || local.HasAttachments != remote.HasAttachments)
            {
                await database.UpdateMessageFlagsAsync(id, remote.IsRead, remote.IsFlagged, remote.HasAttachments, cancellationToken).ConfigureAwait(false);
                updatedFlags++;
            }
        }

        if (deletedIds.Count > 0)
        {
            await database.DeleteMessagesAsync(deletedIds, cancellationToken).ConfigureAwait(false);
        }

        return (updatedFlags, deletedIds.Count);
    }

    public static MailKit.Security.SecureSocketOptions GetSocketOptions(MailTransportSecurity security)
    {
        return security switch
        {
            MailTransportSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            MailTransportSecurity.StartTls => SecureSocketOptions.StartTls,
            _ => throw new ArgumentOutOfRangeException(nameof(security), security, "Unsupported IMAP transport security mode.")
        };
    }

    private static ImapClient CreateClient()
    {
        return new ImapClient
        {
            Timeout = 30_000,
            CheckCertificateRevocation = true
        };
    }

    private static void EnsureSupportedAuthentication(MailAccountRecord account)
    {
        if (account.AuthenticationMethod != MailAuthenticationMethod.AppPassword)
        {
            throw new NotSupportedException(
                "This connection flow currently supports provider-issued app passwords. OAuth is not implemented.");
        }
    }

    private static MailMessageRecord ToRecord(
        string accountId,
        string folderId,
        string folderName,
        ImapRemoteMessage message)
    {
        return new MailMessageRecord(
            $"{accountId}:{folderId}:{message.Uid}",
            message.Sender,
            message.Recipients,
            message.Subject,
            message.BodyText,
            message.ReceivedAtUtc,
            folderName,
            message.IsRead,
            message.IsFlagged,
            message.HasAttachments,
            accountId);
    }

    private static uint? TryParseUid(string id)
    {
        var lastSeparator = id.LastIndexOf(':');
        return lastSeparator >= 0 && uint.TryParse(id[(lastSeparator + 1)..], out var uid)
            ? uid
            : null;
    }

    private static string GetFolderId(string accountId, string fullName)
    {
        var folderHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fullName)));
        return $"{accountId}:{folderHash}";
    }

    private static string? GetSpecialUse(FolderAttributes attributes)
    {
        if (attributes.HasFlag(FolderAttributes.Inbox)) return "inbox";
        if (attributes.HasFlag(FolderAttributes.Sent)) return "sent";
        if (attributes.HasFlag(FolderAttributes.Drafts)) return "drafts";
        if (attributes.HasFlag(FolderAttributes.Trash)) return "trash";
        if (attributes.HasFlag(FolderAttributes.Junk)) return "junk";
        if (attributes.HasFlag(FolderAttributes.Archive)) return "archive";
        if (attributes.HasFlag(FolderAttributes.All)) return "all";
        return null;
    }
}

public sealed record ImapSyncResult(
    int MessageCount,
    int SkippedLargeBodyCount,
    int NewMessageCount,
    int UpdatedFlagCount,
    int DeletedMessageCount,
    int ResyncedFolderCount);
