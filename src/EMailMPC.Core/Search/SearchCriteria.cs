namespace EMailMPC.Core.Search;

public sealed record SearchCriteria
{
    public SearchCriteria(
        string? text = null,
        string? sender = null,
        string? recipient = null,
        string? folder = null,
        DateTimeOffset? after = null,
        DateTimeOffset? before = null,
        bool? isRead = null,
        bool? isFlagged = null,
        bool? hasAttachments = null,
        string? accountId = null)
    {
        if (after is not null && before is not null && after > before)
        {
            throw new ArgumentException("The start of the date range must not be after its end.");
        }

        Text = Normalize(text);
        Sender = Normalize(sender);
        Recipient = Normalize(recipient);
        Folder = Normalize(folder);
        After = after;
        Before = before;
        IsRead = isRead;
        IsFlagged = isFlagged;
        HasAttachments = hasAttachments;
        AccountId = Normalize(accountId);
    }

    public string? Text { get; }
    public string? Sender { get; }
    public string? Recipient { get; }
    public string? Folder { get; }
    public DateTimeOffset? After { get; }
    public DateTimeOffset? Before { get; }
    public bool? IsRead { get; }
    public bool? IsFlagged { get; }
    public bool? HasAttachments { get; }
    public string? AccountId { get; }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
