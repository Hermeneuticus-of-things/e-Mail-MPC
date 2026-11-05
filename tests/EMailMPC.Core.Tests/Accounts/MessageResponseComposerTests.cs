using EMailMPC.Core.Accounts;
using EMailMPC.Core.Storage;

namespace EMailMPC.Core.Tests.Accounts;

public sealed class MessageResponseComposerTests
{
    [Fact]
    public void ReplyUsesSenderAndQuotesOriginalMessage()
    {
        var message = CreateMessage(subject: "Re: Project", body: "First line\nSecond line") with
        {
            MessageId = "<message@example.com>",
            References = "<root@example.com>"
        };

        var response = MessageResponseComposer.Reply(message);

        Assert.Equal("sender@example.com", response.Recipients);
        Assert.Equal("Re: Project", response.Subject);
        Assert.Equal("<message@example.com>", response.InReplyTo);
        Assert.Equal(
            ["<root@example.com>", "<message@example.com>"],
            response.References);
        Assert.Contains("> First line", response.Body);
        Assert.Contains("> Second line", response.Body);
    }

    [Fact]
    public void ReplyWithoutMessageIdPreservesExistingReferencesWithoutInventingThreadParent()
    {
        var message = CreateMessage() with { References = "<root@example.com> <parent@example.com>" };

        var response = MessageResponseComposer.Reply(message);

        Assert.Null(response.InReplyTo);
        Assert.Equal(["<root@example.com>", "<parent@example.com>"], response.References);
    }

    [Fact]
    public void ReplyAllIncludesOtherRecipientsOnceAndExcludesTheCurrentAccount()
    {
        var message = CreateMessage(
            sender: "sender@example.com",
            recipients: "me@example.com, teammate@example.com, SENDER@example.com");

        var response = MessageResponseComposer.ReplyAll(message, "me@example.com");

        Assert.Equal("sender@example.com, teammate@example.com", response.Recipients);
        Assert.StartsWith("Re: ", response.Subject);
    }

    [Fact]
    public void ReplyAllDoesNotAddASecondReplyPrefix()
    {
        var response = MessageResponseComposer.ReplyAll(CreateMessage(subject: "RE: Project"), "me@example.com");

        Assert.Equal("RE: Project", response.Subject);
    }

    [Fact]
    public void ForwardLeavesRecipientsBlankAndIncludesOriginalHeadersAndBody()
    {
        var message = CreateMessage(subject: "Project", body: "Original text");

        var response = MessageResponseComposer.Forward(message);

        Assert.Equal(string.Empty, response.Recipients);
        Assert.Equal("Fwd: Project", response.Subject);
        Assert.Contains("From: sender@example.com", response.Body);
        Assert.Contains("To: me@example.com, teammate@example.com", response.Body);
        Assert.Contains("Subject: Project", response.Body);
        Assert.EndsWith("Original text", response.Body);
    }

    [Fact]
    public void ReplyRejectsMalformedSenderInsteadOfOpeningAnEmptyRecipient()
    {
        var message = CreateMessage(sender: "not-an-email");

        Assert.Throws<InvalidDataException>(() => MessageResponseComposer.Reply(message));
    }

    private static MailMessageRecord CreateMessage(
        string sender = "sender@example.com",
        string recipients = "me@example.com, teammate@example.com",
        string subject = "Project",
        string body = "Original text") =>
        new(
            "message-1",
            sender,
            recipients,
            subject,
            body,
            new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero),
            "INBOX",
            false,
            false,
            false,
            "account-1");
}
