using EMailMPC.Core.Accounts;
using EMailMPC.Core.Storage;
using MailKit.Security;
using MimeKit;

namespace EMailMPC.Core.Tests.Accounts;

public sealed class SmtpMailServiceTests
{
    [Theory]
    [InlineData(MailTransportSecurity.SslOnConnect, SecureSocketOptions.SslOnConnect)]
    [InlineData(MailTransportSecurity.StartTls, SecureSocketOptions.StartTls)]
    public void SmtpTransportSecurityNeverSelectsPlaintext(
        MailTransportSecurity security,
        SecureSocketOptions expected)
    {
        Assert.Equal(expected, SmtpMailService.GetSocketOptions(security));
    }

    [Fact]
    public void RejectsUnsupportedSmtpTransportSecurity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SmtpMailService.GetSocketOptions((MailTransportSecurity)0));
    }

    [Fact]
    public async Task RefusesOAuthAccountsInsteadOfSendingAnAppPasswordAsOAuth()
    {
        var account = new MailAccountRecord(
            "oauth-account",
            "ana@example.com",
            "Ana",
            "imap.example.com",
            993,
            MailTransportSecurity.SslOnConnect,
            "127.0.0.1",
            1,
            MailTransportSecurity.SslOnConnect,
            MailAuthenticationMethod.OAuth2);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new SmtpMailService().SendAsync(account, "not-an-oauth-token", "to@example.com", "Subject", "Body"));
    }

    [Fact]
    public async Task RejectsOversizedRecipientListBeforeConnectingToSmtp()
    {
        var account = new MailAccountRecord(
            "account-1",
            "ana@example.com",
            "Ana",
            "imap.example.com",
            993,
            MailTransportSecurity.SslOnConnect,
            "127.0.0.1",
            1,
            MailTransportSecurity.SslOnConnect,
            MailAuthenticationMethod.AppPassword);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SmtpMailService().SendAsync(
                account,
                "app-password",
                new string('a', 4097),
                "Subject",
                "Body"));
    }

    [Fact]
    public async Task RejectsAttachmentPathsAndControlCharactersBeforeConnectingToSmtp()
    {
        var attachment = new SmtpMailService.OutgoingAttachment("..\\secret.txt", [1, 2, 3]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SmtpMailService().SendAsync(
                CreateAccount(),
                "app-password",
                "to@example.com",
                "Subject",
                "Body",
                [attachment]));
    }

    [Fact]
    public async Task RejectsCombinedAttachmentsOverTwentyFiveMibBeforeConnectingToSmtp()
    {
        var maximumSize = EncryptedMailDatabase.MaximumAttachmentBytes;
        var attachments = new[]
        {
            new SmtpMailService.OutgoingAttachment("first.bin", new byte[maximumSize]),
            new SmtpMailService.OutgoingAttachment("second.bin", [1])
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SmtpMailService().SendAsync(
                CreateAccount(),
                "app-password",
                "to@example.com",
                "Subject",
                "Body",
                attachments));
    }

    [Fact]
    public void AddsSelectedFilesAsMimeAttachmentsWithoutChangingThePlainTextBody()
    {
        var attachmentBytes = new byte[] { 0, 1, 2, 255 };
        var message = SmtpMailService.CreateMessage(
            CreateAccount(),
            new SmtpMailService.OutgoingMessage(
                "to@example.com",
                "Subject",
                "Message text",
                [new SmtpMailService.OutgoingAttachment("report.bin", attachmentBytes)]));

        var multipart = Assert.IsType<Multipart>(message.Body);
        var textPart = Assert.IsType<TextPart>(Assert.Single(multipart.OfType<TextPart>()));
        Assert.Equal("Message text", textPart.Text);
        var attachment = Assert.IsType<MimePart>(Assert.Single(message.Attachments));
        Assert.Equal("report.bin", attachment.FileName);
        using var content = new MemoryStream();
        Assert.NotNull(attachment.Content);
        attachment.Content.DecodeTo(content);
        Assert.Equal(attachmentBytes, content.ToArray());
    }

    [Fact]
    public void AddsReplyThreadHeadersAndGeneratedMessageId()
    {
        var message = SmtpMailService.CreateMessage(
            CreateAccount(),
            new SmtpMailService.OutgoingMessage(
                "to@example.com",
                "Re: Subject",
                "Reply text",
                InReplyTo: "<parent@example.com>",
                References: ["<root@example.com>", "<parent@example.com>"]));

        Assert.Equal("parent@example.com", message.InReplyTo);
        Assert.Equal(
            ["<root@example.com>", "<parent@example.com>"],
            message.References);
        Assert.False(string.IsNullOrWhiteSpace(message.MessageId));
    }

    [Fact]
    public void RejectsLineBreaksInThreadHeaders()
    {
        Assert.Throws<ArgumentException>(() => SmtpMailService.CreateMessage(
            CreateAccount(),
            new SmtpMailService.OutgoingMessage(
                "to@example.com",
                "Re: Subject",
                "Reply text",
                InReplyTo: "<parent@example.com>\r\nBcc: other@example.com")));
    }

    private static MailAccountRecord CreateAccount() =>
        new(
            "account-1",
            "ana@example.com",
            "Ana",
            "imap.example.com",
            993,
            MailTransportSecurity.SslOnConnect,
            "127.0.0.1",
            1,
            MailTransportSecurity.SslOnConnect,
            MailAuthenticationMethod.AppPassword);
}
