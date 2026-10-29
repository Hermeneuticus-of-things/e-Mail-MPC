using EMailMPC.Core.Search;

namespace EMailMPC.Core.Tests.Search;

public sealed class SearchCriteriaTests
{
    [Fact]
    public void NormalizesTextFiltersAndPreservesExplicitFlags()
    {
        var criteria = new SearchCriteria(
            text: "  invoice  ",
            sender: " ana@example.com ",
            accountId: " account-1 ",
            isRead: false,
            hasAttachments: true);

        Assert.Equal("invoice", criteria.Text);
        Assert.Equal("ana@example.com", criteria.Sender);
        Assert.Equal("account-1", criteria.AccountId);
        Assert.Null(criteria.Recipient);
        Assert.False(criteria.IsRead);
        Assert.True(criteria.HasAttachments);
        Assert.Null(criteria.IsFlagged);
    }

    [Fact]
    public void ConvertsBlankTextFiltersToUnspecified()
    {
        var criteria = new SearchCriteria(text: "  ", folder: "", accountId: " ");

        Assert.Null(criteria.Text);
        Assert.Null(criteria.Folder);
        Assert.Null(criteria.AccountId);
    }

    [Fact]
    public void RejectsAnInvertedDateRange()
    {
        var start = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => new SearchCriteria(after: start, before: end));
    }
}
