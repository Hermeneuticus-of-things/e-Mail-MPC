using System.Net.Mail;
using EMailMPC.Core.Storage;

namespace EMailMPC.Core.Accounts;

public sealed record MessageResponseDraft(
    string Recipients,
    string Subject,
    string Body,
    string? InReplyTo = null,
    IReadOnlyList<string>? References = null);

public static class MessageResponseComposer
{
    public static MessageResponseDraft Reply(MailMessageRecord message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new MessageResponseDraft(
            FirstAddress(message.Sender),
            AddSubjectPrefix(message.Subject, "Re:"),
            QuoteOriginal(message),
            message.MessageId,
            BuildReferences(message));
    }

    public static MessageResponseDraft ReplyAll(MailMessageRecord message, string accountAddress)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountAddress);

        var ownAddress = ParseAddress(accountAddress);
        var recipients = ParseAddresses(message.Sender)
            .Concat(ParseAddresses(message.Recipients))
            .Where(address => !string.Equals(address, ownAddress, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MessageResponseDraft(
            string.Join(", ", recipients),
            AddSubjectPrefix(message.Subject, "Re:"),
            QuoteOriginal(message),
            message.MessageId,
            BuildReferences(message));
    }

    public static MessageResponseDraft Forward(MailMessageRecord message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var body = string.Join(
            Environment.NewLine,
            "---------- Forwarded message ----------",
            $"From: {message.Sender}",
            $"To: {message.Recipients}",
            $"Date: {message.ReceivedAtUtc.ToLocalTime():g}",
            $"Subject: {message.Subject}",
            string.Empty,
            message.BodyText);
        return new MessageResponseDraft(string.Empty, AddSubjectPrefix(message.Subject, "Fwd:"), body);
    }

    private static string QuoteOriginal(MailMessageRecord message)
    {
        var quotedBody = string.Join(
            Environment.NewLine,
            message.BodyText
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => $"> {line}"));
        return string.Join(
            Environment.NewLine,
            $"On {message.ReceivedAtUtc.ToLocalTime():g}, {message.Sender} wrote:",
            quotedBody);
    }

    private static IReadOnlyList<string> BuildReferences(MailMessageRecord message)
    {
        var references = (message.References ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (!string.IsNullOrWhiteSpace(message.MessageId))
        {
            references.Add(message.MessageId);
        }

        return references
            .Distinct(StringComparer.Ordinal)
            .TakeLast(100)
            .ToArray();
    }

    private static string FirstAddress(string addresses) =>
        ParseAddresses(addresses).FirstOrDefault()
        ?? throw new InvalidDataException("The selected message does not contain a replyable sender address.");

    private static IEnumerable<string> ParseAddresses(string addresses)
    {
        foreach (var value in addresses.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!MailAddress.TryCreate(value, out var address))
            {
                throw new InvalidDataException("The selected message contains an invalid email address and cannot be replied to safely.");
            }

            yield return address.Address;
        }
    }

    private static string ParseAddress(string address) =>
        MailAddress.TryCreate(address, out var parsed)
            ? parsed.Address
            : throw new ArgumentException("The account email address is invalid.", nameof(address));

    private static string AddSubjectPrefix(string subject, string prefix)
    {
        var trimmed = subject.Trim();
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{prefix} {trimmed}";
    }
}
