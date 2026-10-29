using EMailMPC.Core.Accounts;
using EMailMPC.Core.Storage;
using MailKit.Security;

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
}
