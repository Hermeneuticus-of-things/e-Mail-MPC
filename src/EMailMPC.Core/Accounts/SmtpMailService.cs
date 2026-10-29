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

    public async Task SendAsync(
        MailAccountRecord account,
        string password,
        string recipients,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var message = CreateMessage(account, recipients, subject, body);
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

    private static MimeMessage CreateMessage(
        MailAccountRecord account,
        string recipients,
        string subject,
        string body)
    {
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

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(account.DisplayName, account.EmailAddress));
        message.To.AddRange(parsedRecipients);
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };
        return message;
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
