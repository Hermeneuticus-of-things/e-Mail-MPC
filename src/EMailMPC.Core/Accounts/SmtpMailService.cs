using EMailMPC.Core.Storage;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace EMailMPC.Core.Accounts;

public sealed class SmtpMailService
{
    private const int MaximumRecipientsLength = 4096;
    private const int MaximumSubjectLength = 998;
    private const int MaximumBodyLength = 1_048_576;

    public sealed record OutgoingAttachment(string FileName, byte[] Content);

    public sealed record OutgoingMessage(
        string Recipients,
        string Subject,
        string Body,
        IReadOnlyCollection<OutgoingAttachment>? Attachments = null,
        string? InReplyTo = null,
        IReadOnlyCollection<string>? References = null);

    public async Task SendAsync(
        MailAccountRecord account,
        string password,
        string recipients,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        await SendWithMessageIdAsync(
            account,
            password,
            new OutgoingMessage(recipients, subject, body),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync(
        MailAccountRecord account,
        string password,
        string recipients,
        string subject,
        string body,
        IReadOnlyCollection<OutgoingAttachment>? attachments,
        CancellationToken cancellationToken = default)
    {
        await SendWithMessageIdAsync(
            account,
            password,
            new OutgoingMessage(recipients, subject, body, attachments),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> SendWithMessageIdAsync(
        MailAccountRecord account,
        string password,
        OutgoingMessage outgoing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentNullException.ThrowIfNull(outgoing);
        var message = CreateMessage(account, outgoing);
        EnsureSupportedAuthentication(account);

        using var client = new SmtpClient
        {
            Timeout = 30_000,
            CheckCertificateRevocation = true
        };
        await client.ConnectAsync(
            account.SmtpHost,
            account.SmtpPort,
            GetSocketOptions(account.SmtpSecurity),
            cancellationToken).ConfigureAwait(false);
        await client.AuthenticateAsync(account.EmailAddress, password, cancellationToken).ConfigureAwait(false);
        await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        return message.MessageId
            ?? throw new InvalidOperationException("The generated outgoing message has no Message-ID.");
    }

    public static SecureSocketOptions GetSocketOptions(MailTransportSecurity security)
    {
        return security switch
        {
            MailTransportSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            MailTransportSecurity.StartTls => SecureSocketOptions.StartTls,
            _ => throw new ArgumentOutOfRangeException(nameof(security), security, "Unsupported SMTP transport security mode.")
        };
    }

    internal static MimeMessage CreateMessage(MailAccountRecord account, OutgoingMessage outgoing)
    {
        ArgumentNullException.ThrowIfNull(outgoing);
        var recipients = outgoing.Recipients;
        var subject = outgoing.Subject;
        var body = outgoing.Body;
        var attachments = outgoing.Attachments;
        var inReplyTo = outgoing.InReplyTo;
        var references = outgoing.References;
        ArgumentException.ThrowIfNullOrWhiteSpace(recipients);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);
        if (recipients.Length > MaximumRecipientsLength || subject.Length > MaximumSubjectLength ||
            body.Length > MaximumBodyLength || subject.Contains('\0') || body.Contains('\0'))
        {
            throw new ArgumentException("A message field exceeds the supported size or contains invalid characters.");
        }

        InternetAddressList parsedRecipients;
        try
        {
            parsedRecipients = InternetAddressList.Parse(recipients);
        }
        catch (ParseException exception)
        {
            throw new ArgumentException("Enter one or more valid email addresses.", nameof(recipients), exception);
        }

        if (parsedRecipients.Count == 0 || parsedRecipients.Any(address => address is not MailboxAddress))
        {
            throw new ArgumentException("Enter one or more valid email addresses.", nameof(recipients));
        }

        ValidateAttachments(attachments);
        ValidateThreadHeaders(inReplyTo, references);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(account.DisplayName, account.EmailAddress));
        message.To.AddRange(parsedRecipients);
        message.Subject = subject;
        message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId();
        if (inReplyTo is not null)
        {
            message.InReplyTo = inReplyTo;
        }

        if (references is not null)
        {
            message.References.AddRange(references);
        }

        var bodyBuilder = new BodyBuilder { TextBody = body };
        if (attachments is not null)
        {
            foreach (var attachment in attachments)
            {
                bodyBuilder.Attachments.Add(
                    attachment.FileName,
                    attachment.Content,
                    ContentType.Parse("application/octet-stream"));
            }
        }

        message.Body = bodyBuilder.ToMessageBody();
        return message;
    }

    private static void ValidateThreadHeaders(
        string? inReplyTo,
        IReadOnlyCollection<string>? references)
    {
        if (inReplyTo is not null &&
            (inReplyTo.Length > MaximumSubjectLength || inReplyTo.Any(char.IsControl)))
        {
            throw new ArgumentException("The In-Reply-To header is invalid.", nameof(inReplyTo));
        }

        if (references is null)
        {
            return;
        }

        if (references.Count > 100 ||
            references.Any(reference =>
                string.IsNullOrWhiteSpace(reference) ||
                reference.Length > MaximumSubjectLength ||
                reference.Any(char.IsControl)))
        {
            throw new ArgumentException("The References header is invalid.", nameof(references));
        }
    }

    private static void ValidateAttachments(IReadOnlyCollection<OutgoingAttachment>? attachments)
    {
        if (attachments is null)
        {
            return;
        }

        long totalBytes = 0;
        foreach (var attachment in attachments)
        {
            if (attachment is null ||
                string.IsNullOrWhiteSpace(attachment.FileName) ||
                attachment.FileName.Length > 255 ||
                attachment.FileName.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0 ||
                attachment.FileName.Any(char.IsControl) ||
                attachment.Content is null)
            {
                throw new ArgumentException("An attachment has an invalid file name or content.");
            }

            if (attachment.Content.Length > EncryptedMailDatabase.MaximumAttachmentBytes)
            {
                throw new ArgumentException(
                    $"Attachments cannot exceed {EncryptedMailDatabase.MaximumAttachmentBytes / (1024 * 1024)} MiB each.");
            }

            totalBytes += attachment.Content.Length;
            if (totalBytes > EncryptedMailDatabase.MaximumAttachmentBytes)
            {
                throw new ArgumentException(
                    $"The combined attachment size cannot exceed {EncryptedMailDatabase.MaximumAttachmentBytes / (1024 * 1024)} MiB.");
            }
        }
    }

    private static void EnsureSupportedAuthentication(MailAccountRecord account)
    {
        if (account.AuthenticationMethod != MailAuthenticationMethod.AppPassword)
        {
            throw new NotSupportedException(
                "This connection flow currently supports provider-issued app passwords. OAuth is not implemented.");
        }
    }
}
