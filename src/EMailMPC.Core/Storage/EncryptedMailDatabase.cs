using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EMailMPC.Core.Search;
using Microsoft.Data.Sqlite;

namespace EMailMPC.Core.Storage;

public sealed class EncryptedMailDatabase : IDisposable
{
    private const int CurrentSchemaVersion = 4;
    private static readonly Regex SearchTokenPattern = new(@"[\p{L}\p{M}\p{N}]+", RegexOptions.Compiled);
    private static readonly object ProviderLock = new();
    private static bool _providerInitialized;

    private readonly string _databasePath;
    private readonly byte[] _key;
    private bool _disposed;

    public EncryptedMailDatabase(string databasePath, byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32)
        {
            throw new ArgumentException("The database key must contain exactly 32 bytes.", nameof(key));
        }

        _databasePath = Path.GetFullPath(databasePath);
        _key = key.ToArray();
        InitializeProvider();
    }

    public void Initialize()
    {
        ThrowIfDisposed();
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        using var connection = OpenConnection();
        VerifyReadableDatabase(connection);
        ExecuteNonQuery(connection, "PRAGMA cipher_memory_security = ON;");
        ExecuteNonQuery(connection, "PRAGMA journal_mode = DELETE;");
        ExecuteNonQuery(connection, "PRAGMA synchronous = FULL;");
        ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON;");
        Migrate(connection);
    }

    public async Task SaveAccountAsync(MailAccountRecord account, CancellationToken cancellationToken = default)
    {
        await SaveAccountAsync(account, [], cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAccountAsync(
        MailAccountRecord account,
        IReadOnlyCollection<MailFolderRecord> folders,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(folders);
        ValidateAccount(account);
        foreach (var folder in folders)
        {
            ArgumentNullException.ThrowIfNull(folder);
            ValidateFolder(folder);
            if (!string.Equals(folder.AccountId, account.Id, StringComparison.Ordinal))
            {
                throw new ArgumentException("Every folder must belong to the account being saved.", nameof(folders));
            }
        }

        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO mail_accounts
                (id, email_address, display_name, imap_host, imap_port, imap_security,
                 smtp_host, smtp_port, smtp_security, authentication_method)
            VALUES
                ($id, $email, $display, $imapHost, $imapPort, $imapSecurity,
                 $smtpHost, $smtpPort, $smtpSecurity, $authMethod)
            ON CONFLICT(id) DO UPDATE SET
                email_address = excluded.email_address,
                display_name = excluded.display_name,
                imap_host = excluded.imap_host,
                imap_port = excluded.imap_port,
                imap_security = excluded.imap_security,
                smtp_host = excluded.smtp_host,
                smtp_port = excluded.smtp_port,
                smtp_security = excluded.smtp_security,
                authentication_method = excluded.authentication_method;
            """;
        command.Parameters.AddWithValue("$id", account.Id);
        command.Parameters.AddWithValue("$email", account.EmailAddress);
        command.Parameters.AddWithValue("$display", account.DisplayName);
        command.Parameters.AddWithValue("$imapHost", account.ImapHost);
        command.Parameters.AddWithValue("$imapPort", account.ImapPort);
        command.Parameters.AddWithValue("$imapSecurity", account.ImapSecurity.ToString());
        command.Parameters.AddWithValue("$smtpHost", account.SmtpHost);
        command.Parameters.AddWithValue("$smtpPort", account.SmtpPort);
        command.Parameters.AddWithValue("$smtpSecurity", account.SmtpSecurity.ToString());
        command.Parameters.AddWithValue("$authMethod", account.AuthenticationMethod.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        command.Parameters.Clear();
        command.CommandText = """
            INSERT INTO mail_folders
                (id, account_id, remote_id, name, full_name, parent_remote_id, special_use)
            VALUES
                ($id, $accountId, $remoteId, $name, $fullName, $parentRemoteId, $specialUse)
            ON CONFLICT(id) DO UPDATE SET
                account_id = excluded.account_id,
                remote_id = excluded.remote_id,
                name = excluded.name,
                full_name = excluded.full_name,
                parent_remote_id = excluded.parent_remote_id,
                special_use = excluded.special_use;
            """;
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var accountId = command.Parameters.Add("$accountId", SqliteType.Text);
        var remoteId = command.Parameters.Add("$remoteId", SqliteType.Text);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var fullName = command.Parameters.Add("$fullName", SqliteType.Text);
        var parentRemoteId = command.Parameters.Add("$parentRemoteId", SqliteType.Text);
        var specialUse = command.Parameters.Add("$specialUse", SqliteType.Text);
        await command.PrepareAsync(cancellationToken).ConfigureAwait(false);

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            id.Value = folder.Id;
            accountId.Value = folder.AccountId;
            remoteId.Value = folder.RemoteId;
            name.Value = folder.Name;
            fullName.Value = folder.FullName;
            parentRemoteId.Value = (object?)folder.ParentRemoteId ?? DBNull.Value;
            specialUse.Value = (object?)folder.SpecialUse ?? DBNull.Value;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MailAccountRecord>> ListAccountsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, email_address, display_name, imap_host, imap_port, imap_security,
                   smtp_host, smtp_port, smtp_security, authentication_method
            FROM mail_accounts
            ORDER BY email_address COLLATE NOCASE, id;
            """;

        var accounts = new List<MailAccountRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            accounts.Add(new MailAccountRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                Enum.Parse<MailTransportSecurity>(reader.GetString(5)),
                reader.GetString(6),
                reader.GetInt32(7),
                Enum.Parse<MailTransportSecurity>(reader.GetString(8)),
                Enum.Parse<MailAuthenticationMethod>(reader.GetString(9))));
        }

        return accounts;
    }

    public async Task SaveFolderAsync(MailFolderRecord folder, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(folder);
        ValidateFolder(folder);

        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mail_folders
                (id, account_id, remote_id, name, full_name, parent_remote_id, special_use)
            VALUES
                ($id, $accountId, $remoteId, $name, $fullName, $parentRemoteId, $specialUse)
            ON CONFLICT(id) DO UPDATE SET
                account_id = excluded.account_id,
                remote_id = excluded.remote_id,
                name = excluded.name,
                full_name = excluded.full_name,
                parent_remote_id = excluded.parent_remote_id,
                special_use = excluded.special_use;
            """;
        command.Parameters.AddWithValue("$id", folder.Id);
        command.Parameters.AddWithValue("$accountId", folder.AccountId);
        command.Parameters.AddWithValue("$remoteId", folder.RemoteId);
        command.Parameters.AddWithValue("$name", folder.Name);
        command.Parameters.AddWithValue("$fullName", folder.FullName);
        command.Parameters.AddWithValue("$parentRemoteId", (object?)folder.ParentRemoteId ?? DBNull.Value);
        command.Parameters.AddWithValue("$specialUse", (object?)folder.SpecialUse ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MailFolderRecord>> ListFoldersAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, account_id, remote_id, name, full_name, parent_remote_id, special_use
            FROM mail_folders
            WHERE account_id = $accountId
            ORDER BY full_name COLLATE NOCASE, id;
            """;
        command.Parameters.AddWithValue("$accountId", accountId.Trim());

        var folders = new List<MailFolderRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            folders.Add(new MailFolderRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return folders;
    }

    public async Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "UPDATE mail_messages SET account_id = NULL WHERE account_id = $accountId;";
        command.Parameters.AddWithValue("$accountId", accountId.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        command.Parameters.Clear();
        command.CommandText = "DELETE FROM mail_accounts WHERE id = $accountId;";
        command.Parameters.AddWithValue("$accountId", accountId.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveMessageAsync(MailMessageRecord message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await SaveMessagesAsync([message], cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveMessagesAsync(
        IReadOnlyCollection<MailMessageRecord> messages,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(messages);
        foreach (var message in messages)
        {
            ArgumentNullException.ThrowIfNull(message);
            ValidateMessage(message);
        }

        if (messages.Count == 0)
        {
            return;
        }

        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO mail_messages
                (id, sender, recipients, subject, body_text, received_at_utc, folder, is_read, is_flagged, has_attachments, account_id)
            VALUES
                ($id, $sender, $recipients, $subject, $body, $received, $folder, $isRead, $isFlagged, $hasAttachments, $accountId)
            ON CONFLICT(id) DO UPDATE SET
                sender = excluded.sender,
                recipients = excluded.recipients,
                subject = excluded.subject,
                body_text = excluded.body_text,
                received_at_utc = excluded.received_at_utc,
                folder = excluded.folder,
                is_read = excluded.is_read,
                is_flagged = excluded.is_flagged,
                has_attachments = excluded.has_attachments,
                account_id = excluded.account_id;
            """;
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var sender = command.Parameters.Add("$sender", SqliteType.Text);
        var recipients = command.Parameters.Add("$recipients", SqliteType.Text);
        var subject = command.Parameters.Add("$subject", SqliteType.Text);
        var body = command.Parameters.Add("$body", SqliteType.Text);
        var received = command.Parameters.Add("$received", SqliteType.Text);
        var folder = command.Parameters.Add("$folder", SqliteType.Text);
        var isRead = command.Parameters.Add("$isRead", SqliteType.Integer);
        var isFlagged = command.Parameters.Add("$isFlagged", SqliteType.Integer);
        var hasAttachments = command.Parameters.Add("$hasAttachments", SqliteType.Integer);
        var accountId = command.Parameters.Add("$accountId", SqliteType.Text);
        await command.PrepareAsync(cancellationToken).ConfigureAwait(false);

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            id.Value = message.Id;
            sender.Value = message.Sender;
            recipients.Value = message.Recipients;
            subject.Value = message.Subject;
            body.Value = message.BodyText;
            received.Value = message.ReceivedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            folder.Value = message.Folder;
            isRead.Value = message.IsRead ? 1 : 0;
            isFlagged.Value = message.IsFlagged ? 1 : 0;
            hasAttachments.Value = message.HasAttachments ? 1 : 0;
            accountId.Value = (object?)message.AccountId ?? DBNull.Value;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MailSearchResult>> SearchAsync(
        SearchCriteria criteria,
        int limit = 100,
        SearchSort sort = SearchSort.DateNewestFirst,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(criteria.Text))
        {
            var literalMatch = BuildFtsExpression(criteria.Text);
            if (literalMatch is null)
            {
                return [];
            }

            return await SearchCoreAsync(criteria, literalMatch, excludedTextMatch: null, sort, limit, cancellationToken)
                .ConfigureAwait(false);
        }

        return await SearchCoreAsync(criteria, textMatch: null, excludedTextMatch: null, sort, limit, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MailSearchResult>> SearchAsync(
        ParsedMailQuery query,
        int limit = 100,
        SearchSort sort = SearchSort.DateNewestFirst,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!string.IsNullOrWhiteSpace(query.Criteria.Text))
        {
            return await SearchAsync(query.Criteria, limit, sort, cancellationToken).ConfigureAwait(false);
        }

        return await SearchCoreAsync(query.Criteria, query.TextMatch, query.ExcludedTextMatch, sort, limit, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<MailSearchResult>> SearchCoreAsync(
        SearchCriteria criteria,
        string? textMatch,
        string? excludedTextMatch,
        SearchSort sort,
        int limit,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(criteria);
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Search limits must be between 1 and 1000.");
        }

        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await VerifyConnectionAsync(connection, cancellationToken).ConfigureAwait(false);

        var clauses = new List<string>();
        await using var command = connection.CreateCommand();

        if (textMatch is not null)
        {
            clauses.Add("mail_messages_fts MATCH $text");
            command.Parameters.AddWithValue("$text", textMatch);
        }
        else if (excludedTextMatch is not null)
        {
            clauses.Add("m.rowid NOT IN (SELECT rowid FROM mail_messages_fts WHERE mail_messages_fts MATCH $excludedText)");
            command.Parameters.AddWithValue("$excludedText", excludedTextMatch);
        }

        AddTextFilter(clauses, command, "m.sender", "$sender", criteria.Sender);
        AddTextFilter(clauses, command, "m.recipients", "$recipient", criteria.Recipient);
        AddTextFilter(clauses, command, "m.folder", "$folder", criteria.Folder);
        AddExactTextFilter(clauses, command, "m.account_id", "$accountId", criteria.AccountId);
        AddBooleanFilter(clauses, command, "m.is_read", "$isRead", criteria.IsRead);
        AddBooleanFilter(clauses, command, "m.is_flagged", "$isFlagged", criteria.IsFlagged);
        AddBooleanFilter(clauses, command, "m.has_attachments", "$hasAttachments", criteria.HasAttachments);

        if (criteria.After is not null)
        {
            clauses.Add("m.received_at_utc >= $after");
            command.Parameters.AddWithValue("$after", criteria.After.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }

        if (criteria.Before is not null)
        {
            clauses.Add("m.received_at_utc <= $before");
            command.Parameters.AddWithValue("$before", criteria.Before.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }

        var whereClause = clauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", clauses)}";
        var relevanceEnabled = sort == SearchSort.Relevance && textMatch is not null;
        command.CommandText = $"""
            SELECT m.id, m.sender, m.subject, m.received_at_utc, m.folder, m.is_read, m.is_flagged, m.has_attachments, m.account_id{(relevanceEnabled ? ", bm25(mail_messages_fts) AS relevance" : string.Empty)}
            FROM mail_messages AS m
            {(textMatch is null ? string.Empty : "JOIN mail_messages_fts ON mail_messages_fts.rowid = m.rowid")}
            {whereClause}
            ORDER BY {(relevanceEnabled ? "bm25(mail_messages_fts) ASC, m.received_at_utc DESC, m.id ASC" : "m.received_at_utc DESC, m.id ASC")}
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<MailSearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new MailSearchResult(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                relevanceEnabled && !reader.IsDBNull(9) ? reader.GetDouble(9) : null));
        }

        return results;
    }

    public async Task<FolderSyncStateRecord?> GetFolderSyncStateAsync(
        string accountId,
        string folderId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);

        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT uid_validity, highest_uid
            FROM folder_sync_state
            WHERE account_id = $accountId AND folder_id = $folderId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue("$folderId", folderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new FolderSyncStateRecord(
            accountId,
            folderId,
            Convert.ToUInt32(reader.GetInt64(0), CultureInfo.InvariantCulture),
            Convert.ToUInt32(reader.GetInt64(1), CultureInfo.InvariantCulture));
    }

    public async Task SetFolderSyncStateAsync(FolderSyncStateRecord state, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(state);

        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO folder_sync_state (account_id, folder_id, uid_validity, highest_uid)
            VALUES ($accountId, $folderId, $uidValidity, $highestUid)
            ON CONFLICT(account_id, folder_id) DO UPDATE SET
                uid_validity = excluded.uid_validity,
                highest_uid = excluded.highest_uid;
            """;
        command.Parameters.AddWithValue("$accountId", state.AccountId);
        command.Parameters.AddWithValue("$folderId", state.FolderId);
        command.Parameters.AddWithValue("$uidValidity", state.UidValidity);
        command.Parameters.AddWithValue("$highestUid", state.HighestUid);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListFolderMessageIdsAsync(
        string accountId,
        string folderId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);

        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM mail_messages WHERE account_id = $accountId;";
        command.Parameters.AddWithValue("$accountId", accountId);
        var ids = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            if (id.StartsWith($"{accountId}:{folderId}:", StringComparison.Ordinal))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public async Task DeleteFolderMessagesAsync(string accountId, string folderId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);

        var ids = await ListFolderMessageIdsAsync(accountId, folderId, cancellationToken).ConfigureAwait(false);
        await DeleteMessagesAsync(ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteMessagesAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return 0;
        }

        var deleted = 0;
        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        foreach (var chunk in ids.Chunk(64))
        {
            await using var command = connection.CreateCommand();
            var names = new List<string>();
            for (var index = 0; index < chunk.Length; index++)
            {
                var name = $"$id{index}";
                names.Add(name);
                command.Parameters.AddWithValue(name, chunk[index]);
            }

            command.CommandText = $"DELETE FROM mail_messages WHERE id IN ({string.Join(", ", names)});";
            deleted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    public async Task<IReadOnlyList<MailMessageFlagsRecord>> ListFolderMessageFlagsAsync(
        string accountId,
        string folderId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);

        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, is_read, is_flagged, has_attachments FROM mail_messages WHERE account_id = $accountId;";
        command.Parameters.AddWithValue("$accountId", accountId);
        var flags = new List<MailMessageFlagsRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            if (id.StartsWith($"{accountId}:{folderId}:", StringComparison.Ordinal))
            {
                flags.Add(new MailMessageFlagsRecord(
                    id,
                    reader.GetBoolean(1),
                    reader.GetBoolean(2),
                    reader.GetBoolean(3)));
            }
        }

        return flags;
    }

    public async Task UpdateMessageFlagsAsync(
        string id,
        bool isRead,
        bool isFlagged,
        bool hasAttachments,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE mail_messages
            SET is_read = $isRead, is_flagged = $isFlagged, has_attachments = $hasAttachments
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$isRead", isRead ? 1 : 0);
        command.Parameters.AddWithValue("$isFlagged", isFlagged ? 1 : 0);
        command.Parameters.AddWithValue("$hasAttachments", hasAttachments ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MailMessageRecord?> GetMessageAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await VerifyConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, sender, recipients, subject, body_text, received_at_utc, folder,
                   is_read, is_flagged, has_attachments, account_id
            FROM mail_messages
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new MailMessageRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetString(6),
            reader.GetBoolean(7),
            reader.GetBoolean(8),
            reader.GetBoolean(9),
            reader.IsDBNull(10) ? null : reader.GetString(10));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }

    private static void InitializeProvider()
    {
        lock (ProviderLock)
        {
            if (_providerInitialized)
            {
                return;
            }

            SQLitePCL.Batteries_V2.Init();
            _providerInitialized = true;
        }
    }

    private SqliteConnection OpenConnection()
    {
        ThrowIfDisposed();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());

        try
        {
            connection.Open();
            var keyHex = Convert.ToHexString(_key);
            ExecuteNonQuery(connection, $"PRAGMA key = \"x'{keyHex}'\";");
            VerifyCipher(connection);
            ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON;");
            ExecuteNonQuery(connection, "PRAGMA cipher_memory_security = ON;");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void VerifyCipher(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA cipher_version;";
        var version = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new CryptographicException("The SQLite provider does not expose SQLCipher; refusing to store mail data.");
        }
    }

    private static void VerifyReadableDatabase(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master;";
            command.ExecuteScalar();
        }
        catch (SqliteException exception)
        {
            throw new CryptographicException(
                "The local database cannot be decrypted or is corrupt. The database key was not replaced.",
                exception);
        }
    }

    private static async Task VerifyConnectionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA cipher_version;";
        var version = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new CryptographicException("The SQLite provider does not expose SQLCipher; refusing to search mail data.");
        }
    }

    private static void Migrate(SqliteConnection connection)
    {
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(versionCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version > CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"Database schema version {version} is newer than supported version {CurrentSchemaVersion}.");
        }

        if (version == CurrentSchemaVersion)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS mail_accounts (
                id TEXT NOT NULL PRIMARY KEY,
                email_address TEXT NOT NULL,
                display_name TEXT NOT NULL,
                imap_host TEXT NOT NULL,
                imap_port INTEGER NOT NULL CHECK (imap_port BETWEEN 1 AND 65535),
                imap_security TEXT NOT NULL CHECK (imap_security IN ('SslOnConnect', 'StartTls')),
                smtp_host TEXT NOT NULL,
                smtp_port INTEGER NOT NULL CHECK (smtp_port BETWEEN 1 AND 65535),
                smtp_security TEXT NOT NULL CHECK (smtp_security IN ('SslOnConnect', 'StartTls')),
                authentication_method TEXT NOT NULL CHECK (authentication_method IN ('OAuth2', 'AppPassword'))
            );

            CREATE TABLE IF NOT EXISTS mail_folders (
                id TEXT NOT NULL PRIMARY KEY,
                account_id TEXT NOT NULL REFERENCES mail_accounts(id) ON DELETE CASCADE,
                remote_id TEXT NOT NULL,
                name TEXT NOT NULL,
                full_name TEXT NOT NULL,
                parent_remote_id TEXT,
                special_use TEXT,
                UNIQUE(account_id, remote_id)
            );

            CREATE TABLE IF NOT EXISTS mail_messages (
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

            CREATE INDEX IF NOT EXISTS ix_mail_messages_received_at
                ON mail_messages(received_at_utc DESC);
            """;
        command.ExecuteNonQuery();

        if (version < 3)
        {
            command.CommandText = "ALTER TABLE mail_messages ADD COLUMN account_id TEXT REFERENCES mail_accounts(id);";
            command.ExecuteNonQuery();
        }

        command.CommandText = """
            CREATE INDEX IF NOT EXISTS ix_mail_messages_account_received
                ON mail_messages(account_id, received_at_utc DESC);
            """;
        command.ExecuteNonQuery();

        command.CommandText = """
            DROP TRIGGER IF EXISTS mail_messages_ai;
            DROP TRIGGER IF EXISTS mail_messages_ad;
            DROP TRIGGER IF EXISTS mail_messages_au;
            DROP TABLE IF EXISTS mail_messages_fts;

            CREATE VIRTUAL TABLE mail_messages_fts USING fts5(
                sender,
                recipients,
                subject,
                body_text,
                content = 'mail_messages',
                content_rowid = 'rowid',
                tokenize = 'unicode61 remove_diacritics 2'
            );

            INSERT INTO mail_messages_fts(mail_messages_fts) VALUES ('rebuild');

            CREATE TRIGGER mail_messages_ai AFTER INSERT ON mail_messages BEGIN
                INSERT INTO mail_messages_fts(rowid, sender, recipients, subject, body_text)
                VALUES (new.rowid, new.sender, new.recipients, new.subject, new.body_text);
            END;

            CREATE TRIGGER mail_messages_ad AFTER DELETE ON mail_messages BEGIN
                INSERT INTO mail_messages_fts(mail_messages_fts, rowid, sender, recipients, subject, body_text)
                VALUES ('delete', old.rowid, old.sender, old.recipients, old.subject, old.body_text);
            END;

            CREATE TRIGGER mail_messages_au AFTER UPDATE ON mail_messages BEGIN
                INSERT INTO mail_messages_fts(mail_messages_fts, rowid, sender, recipients, subject, body_text)
                VALUES ('delete', old.rowid, old.sender, old.recipients, old.subject, old.body_text);
                INSERT INTO mail_messages_fts(rowid, sender, recipients, subject, body_text)
                VALUES (new.rowid, new.sender, new.recipients, new.subject, new.body_text);
            END;

            CREATE TABLE IF NOT EXISTS folder_sync_state (
                account_id TEXT NOT NULL REFERENCES mail_accounts(id) ON DELETE CASCADE,
                folder_id TEXT NOT NULL,
                uid_validity INTEGER NOT NULL CHECK (uid_validity >= 0),
                highest_uid INTEGER NOT NULL CHECK (highest_uid >= 0),
                PRIMARY KEY(account_id, folder_id)
            );

            PRAGMA user_version = 4;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static string? BuildFtsExpression(string text)
    {
        var tokens = SearchTokenPattern.Matches(text)
            .Select(match => $"\"{match.Value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return tokens.Length == 0 ? null : string.Join(" AND ", tokens);
    }


    private static void AddTextFilter(
        ICollection<string> clauses,
        SqliteCommand command,
        string column,
        string parameterName,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        clauses.Add($"instr(lower({column}), lower({parameterName})) > 0");
        command.Parameters.AddWithValue(parameterName, value.Trim());
    }

    private static void AddBooleanFilter(
        ICollection<string> clauses,
        SqliteCommand command,
        string column,
        string parameterName,
        bool? value)
    {
        if (value is null)
        {
            return;
        }

        clauses.Add($"{column} = {parameterName}");
        command.Parameters.AddWithValue(parameterName, value.Value ? 1 : 0);
    }

    private static void AddExactTextFilter(
        ICollection<string> clauses,
        SqliteCommand command,
        string column,
        string parameterName,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        clauses.Add($"{column} = {parameterName}");
        command.Parameters.AddWithValue(parameterName, value.Trim());
    }

    private static void ValidateAccount(MailAccountRecord account)
    {
        if (string.IsNullOrWhiteSpace(account.Id) || account.Id.Length > 256)
        {
            throw new ArgumentException("An account ID between 1 and 256 characters is required.", nameof(account));
        }

        if (!IsValidEmailAddress(account.EmailAddress))
        {
            throw new ArgumentException("A valid email address is required.", nameof(account));
        }

        if (account.DisplayName is null ||
            account.DisplayName.Length > 1024 ||
            !IsValidMailHost(account.ImapHost) ||
            !IsValidMailHost(account.SmtpHost) ||
            account.ImapPort is < 1 or > 65535 ||
            account.SmtpPort is < 1 or > 65535 ||
            !Enum.IsDefined(account.ImapSecurity) ||
            !Enum.IsDefined(account.SmtpSecurity) ||
            !Enum.IsDefined(account.AuthenticationMethod))
        {
            throw new ArgumentException("Account endpoints must use valid hosts, ports, TLS, and authentication settings.", nameof(account));
        }
    }

    private static bool IsValidMailHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253)
        {
            return false;
        }

        return Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
    }

    private static bool IsValidEmailAddress(string emailAddress)
    {
        if (string.IsNullOrWhiteSpace(emailAddress) || emailAddress.Length > 320)
        {
            return false;
        }

        try
        {
            return string.Equals(
                new System.Net.Mail.MailAddress(emailAddress).Address,
                emailAddress.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void ValidateFolder(MailFolderRecord folder)
    {
        if (string.IsNullOrWhiteSpace(folder.Id) || folder.Id.Length > 512 ||
            string.IsNullOrWhiteSpace(folder.AccountId) || folder.AccountId.Length > 256 ||
            string.IsNullOrWhiteSpace(folder.RemoteId) || folder.RemoteId.Length > 4096 ||
            string.IsNullOrWhiteSpace(folder.Name) || folder.Name.Length > 1024 ||
            string.IsNullOrWhiteSpace(folder.FullName) || folder.FullName.Length > 4096 ||
            folder.ParentRemoteId?.Length > 4096 ||
            folder.SpecialUse?.Length > 128)
        {
            throw new ArgumentException("Folder identifiers and names are required and must be within supported sizes.", nameof(folder));
        }
    }

    private static void ValidateMessage(MailMessageRecord message)
    {
        if (string.IsNullOrWhiteSpace(message.Id))
        {
            throw new ArgumentException("A message ID is required.", nameof(message));
        }

        if (message.Id.Length > 512 || message.Sender.Length > 4096 ||
            message.Recipients.Length > 16_384 || message.Subject.Length > 16_384 ||
            message.Folder.Length > 4096)
        {
            throw new ArgumentException("A message field exceeds the supported size.", nameof(message));
        }
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

public sealed record MailMessageRecord(
    string Id,
    string Sender,
    string Recipients,
    string Subject,
    string BodyText,
    DateTimeOffset ReceivedAtUtc,
    string Folder,
    bool IsRead,
    bool IsFlagged,
    bool HasAttachments,
    string? AccountId = null);

public sealed record MailSearchResult(
    string Id,
    string Sender,
    string Subject,
    DateTimeOffset ReceivedAtUtc,
    string Folder,
    bool IsRead,
    bool IsFlagged,
    bool HasAttachments,
    string? AccountId = null,
    double? RelevanceScore = null);

public sealed record FolderSyncStateRecord(
    string AccountId,
    string FolderId,
    uint UidValidity,
    uint HighestUid);

public sealed record MailMessageFlagsRecord(
    string Id,
    bool IsRead,
    bool IsFlagged,
    bool HasAttachments);

public sealed record MailAccountRecord(
    string Id,
    string EmailAddress,
    string DisplayName,
    string ImapHost,
    int ImapPort,
    MailTransportSecurity ImapSecurity,
    string SmtpHost,
    int SmtpPort,
    MailTransportSecurity SmtpSecurity,
    MailAuthenticationMethod AuthenticationMethod);

public sealed record MailFolderRecord(
    string Id,
    string AccountId,
    string RemoteId,
    string Name,
    string FullName,
    string? ParentRemoteId,
    string? SpecialUse);

public enum MailTransportSecurity
{
    SslOnConnect = 1,
    StartTls = 2
}

public enum MailAuthenticationMethod
{
    OAuth2 = 1,
    AppPassword = 2
}
