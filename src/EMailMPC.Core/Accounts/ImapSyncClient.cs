using System.Net;
using System.Text.RegularExpressions;
using EMailMPC.Core.Storage;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;

namespace EMailMPC.Core.Accounts;

/// <summary>A message summary fetched from the remote IMAP folder.</summary>
public sealed record ImapRemoteMessage(
    uint Uid,
    string Sender,
    string Recipients,
    string Subject,
    DateTimeOffset ReceivedAtUtc,
    bool IsRead,
    bool IsFlagged,
    bool HasAttachments,
    string BodyText = "");

/// <summary>Snapshot of a remote folder right after opening it.</summary>
public sealed record ImapFolderSnapshot(int MessageCount, uint UidValidity);

/// <summary>
/// Transport seam for IMAP synchronization. Production uses
/// <see cref="MailKitImapSyncClient"/>; tests substitute a fake so the incremental
/// sync logic can be verified without a server.
/// </summary>
public interface IImapSyncClient : IDisposable
{
    /// <summary>Bodies over the index limit that were intentionally left empty.</summary>
    int SkippedLargeBodyCount { get; }

    Task ConnectAsync(MailAccountRecord account, string password, CancellationToken cancellationToken);

    /// <summary>Opens the folder read-only and returns its message count and UIDVALIDITY.</summary>
    Task<ImapFolderSnapshot> OpenFolderAsync(string remoteId, CancellationToken cancellationToken);

    /// <summary>Fetches up to <paramref name="count"/> newest messages, with searchable bodies.</summary>
    Task<IReadOnlyList<ImapRemoteMessage>> FetchNewestAsync(string remoteId, int count, CancellationToken cancellationToken);

    /// <summary>Fetches all messages with UID in [<paramref name="fromUid"/>, <paramref name="toUid"/>], with searchable bodies.</summary>
    Task<IReadOnlyList<ImapRemoteMessage>> FetchUidRangeAsync(string remoteId, uint fromUid, uint toUid, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches metadata only (flags, attachments) for UID range
    /// [<paramref name="fromUid"/>, <paramref name="toUid"/>]; absent UIDs were expunged server-side.
    /// </summary>
    Task<IReadOnlyList<ImapRemoteMessage>> FetchFlagsAsync(string remoteId, uint fromUid, uint toUid, CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}

/// <summary>MailKit-backed <see cref="IImapSyncClient"/> used in production.</summary>
public sealed class MailKitImapSyncClient : IImapSyncClient
{
    private const int MaximumIndexedBodyBytes = 2 * 1024 * 1024;
    private static readonly Regex HtmlTagPattern = new("<[^>]+>", RegexOptions.Compiled);

    private ImapClient? _client;
    private IMailFolder? _currentFolder;
    private string? _currentRemoteId;
    private int _skippedLargeBodies;

    public int SkippedLargeBodyCount => _skippedLargeBodies;

    public async Task ConnectAsync(MailAccountRecord account, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (account.AuthenticationMethod != MailAuthenticationMethod.AppPassword)
        {
            throw new NotSupportedException(
                "This connection flow currently supports provider-issued app passwords. OAuth is not implemented.");
        }

        _client = new ImapClient
        {
            Timeout = 30_000,
            CheckCertificateRevocation = true
        };
        await _client.ConnectAsync(
            account.ImapHost,
            account.ImapPort,
            ImapMailService.GetSocketOptions(account.ImapSecurity),
            cancellationToken).ConfigureAwait(false);
        await _client.AuthenticateAsync(account.EmailAddress, password, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ImapFolderSnapshot> OpenFolderAsync(string remoteId, CancellationToken cancellationToken)
    {
        var folder = await GetFolderAsync(remoteId, cancellationToken).ConfigureAwait(false);
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
        _currentFolder = folder;
        _currentRemoteId = remoteId;
        return new ImapFolderSnapshot(folder.Count, folder.UidValidity);
    }

    public async Task<IReadOnlyList<ImapRemoteMessage>> FetchNewestAsync(
        string remoteId,
        int count,
        CancellationToken cancellationToken)
    {
        var folder = await GetFolderAsync(remoteId, cancellationToken).ConfigureAwait(false);
        if (folder.Count == 0 || count <= 0)
        {
            return [];
        }

        var firstIndex = Math.Max(0, folder.Count - count);
        var summaries = await folder.FetchAsync(
            firstIndex,
            folder.Count - 1,
            FullFetchRequest(),
            cancellationToken).ConfigureAwait(false);
        return await ToRemoteMessagesAsync(folder, summaries, includeBody: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ImapRemoteMessage>> FetchUidRangeAsync(
        string remoteId,
        uint fromUid,
        uint toUid,
        CancellationToken cancellationToken)
    {
        var folder = await GetFolderAsync(remoteId, cancellationToken).ConfigureAwait(false);
        if (fromUid > toUid)
        {
            return [];
        }

        var summaries = await folder.FetchAsync(
            new UniqueIdRange(new UniqueId(fromUid), new UniqueId(toUid)),
            FullFetchRequest(),
            cancellationToken).ConfigureAwait(false);
        return await ToRemoteMessagesAsync(folder, summaries, includeBody: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ImapRemoteMessage>> FetchFlagsAsync(
        string remoteId,
        uint fromUid,
        uint toUid,
        CancellationToken cancellationToken)
    {
        var folder = await GetFolderAsync(remoteId, cancellationToken).ConfigureAwait(false);
        if (fromUid > toUid)
        {
            return [];
        }

        var summaries = await folder.FetchAsync(
            new UniqueIdRange(new UniqueId(fromUid), new UniqueId(toUid)),
            new FetchRequest(
                MessageSummaryItems.UniqueId |
                MessageSummaryItems.Envelope |
                MessageSummaryItems.BodyStructure |
                MessageSummaryItems.Flags),
            cancellationToken).ConfigureAwait(false);
        return await ToRemoteMessagesAsync(folder, summaries, includeBody: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            await _client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
        _currentFolder = null;
    }

    private async Task<IMailFolder> GetFolderAsync(string remoteId, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("ConnectAsync must be called before using the sync client.");
        }

        if (_currentFolder is null || !string.Equals(_currentRemoteId, remoteId, StringComparison.Ordinal))
        {
            var folder = await _client.GetFolderAsync(remoteId, cancellationToken).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
            _currentFolder = folder;
            _currentRemoteId = remoteId;
        }

        return _currentFolder;
    }

    private static FetchRequest FullFetchRequest()
    {
        return new FetchRequest(
            MessageSummaryItems.UniqueId |
            MessageSummaryItems.Envelope |
            MessageSummaryItems.BodyStructure |
            MessageSummaryItems.Flags |
            MessageSummaryItems.InternalDate |
            MessageSummaryItems.Size);
    }

    private async Task<IReadOnlyList<ImapRemoteMessage>> ToRemoteMessagesAsync(
        IMailFolder folder,
        IList<IMessageSummary> summaries,
        bool includeBody,
        CancellationToken cancellationToken)
    {
        var messages = new List<ImapRemoteMessage>();
        foreach (var summary in summaries.OrderBy(item => item.UniqueId.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = string.Empty;
            if (includeBody)
            {
                body = await FetchSearchableBodyAsync(folder, summary, cancellationToken).ConfigureAwait(false);
            }

            var envelope = summary.Envelope;
            var sender = string.Join(", ", envelope?.From.Mailboxes.Select(mailbox => mailbox.Address) ?? []);
            var recipients = string.Join(", ", (envelope?.To.Mailboxes ?? [])
                .Concat(envelope?.Cc.Mailboxes ?? [])
                .Concat(envelope?.Bcc.Mailboxes ?? [])
                .Select(mailbox => mailbox.Address));
            var receivedAt = summary.InternalDate ?? envelope?.Date ?? DateTimeOffset.UtcNow;
            if (receivedAt == DateTimeOffset.MinValue)
            {
                receivedAt = DateTimeOffset.UtcNow;
            }

            messages.Add(new ImapRemoteMessage(
                summary.UniqueId.Id,
                sender,
                recipients,
                envelope?.Subject ?? string.Empty,
                receivedAt.ToUniversalTime(),
                summary.Flags?.HasFlag(MessageFlags.Seen) ?? false,
                summary.Flags?.HasFlag(MessageFlags.Flagged) ?? false,
                summary.Attachments.Any(),
                body));
        }

        return messages;
    }

    private async Task<string> FetchSearchableBodyAsync(
        IMailFolder folder,
        IMessageSummary summary,
        CancellationToken cancellationToken)
    {
        var textBody = summary.TextBody;
        if (textBody is not null)
        {
            if (textBody.Octets > MaximumIndexedBodyBytes)
            {
                _skippedLargeBodies++;
                return string.Empty;
            }

            var textPart = await folder.GetBodyPartAsync(summary.UniqueId, textBody, cancellationToken).ConfigureAwait(false);
            return textPart is TextPart { IsHtml: false } plainText ? plainText.Text : string.Empty;
        }

        var htmlBody = summary.HtmlBody;
        if (htmlBody is null || htmlBody.Octets > MaximumIndexedBodyBytes)
        {
            if (htmlBody is not null)
            {
                _skippedLargeBodies++;
            }

            return string.Empty;
        }

        var htmlPart = await folder.GetBodyPartAsync(summary.UniqueId, htmlBody, cancellationToken).ConfigureAwait(false);
        return htmlPart is TextPart htmlText ? StripHtml(htmlText.Text) : string.Empty;
    }

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var withSpacing = Regex.Replace(html, @"</(p|div|br|li|tr|h[1-6])\s*>", " ", RegexOptions.IgnoreCase);
        return WebUtility.HtmlDecode(HtmlTagPattern.Replace(withSpacing, " ")).Trim();
    }
}
