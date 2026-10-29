using System.Runtime.Versioning;
using System.Security.Cryptography;
using EMailMPC.Core.Search;
using EMailMPC.Core.Storage;

namespace EMailMPC.Core.Tests.Search;

[SupportedOSPlatform("windows")]
public sealed class SearchParityTests
{
    [Fact]
    public void ParsesAddressFolderAndFlagOperatorsIntoStructuredCriteria()
    {
        var parsed = MailQueryParser.Parse("from:ana to:bob in:Archive is:unread has:attachment");

        Assert.Null(parsed.Criteria.Text);
        Assert.Null(parsed.TextMatch);
        Assert.Equal("ana", parsed.Criteria.Sender);
        Assert.Equal("bob", parsed.Criteria.Recipient);
        Assert.Equal("Archive", parsed.Criteria.Folder);
        Assert.False(parsed.Criteria.IsRead);
        Assert.True(parsed.Criteria.HasAttachments);
    }

    [Fact]
    public void BuildsPhraseMatchExpressionForQuotedText()
    {
        var parsed = MailQueryParser.Parse("\"invoice paid\"");

        Assert.Null(parsed.Criteria.Text);
        Assert.Equal("\"invoice paid\"", parsed.TextMatch);
    }

    [Fact]
    public void ScopesSubjectOperatorToTheSubjectColumn()
    {
        var parsed = MailQueryParser.Parse("subject:invoice");

        Assert.Equal("subject : (\"invoice\")", parsed.TextMatch);
    }

    [Fact]
    public void CombinesFreeTermsOrGroupsAndNegationIntoOneMatchExpression()
    {
        var parsed = MailQueryParser.Parse("alpha (beta gamma) -delta");

        Assert.Equal("\"alpha\" AND (\"beta\" OR \"gamma\") NOT (\"delta\")", parsed.TextMatch);
        Assert.Null(parsed.ExcludedTextMatch);
    }

    [Fact]
    public void DegradesUnknownKeysToPlainTextTokens()
    {
        var parsed = MailQueryParser.Parse("foo:bar baz");

        Assert.Equal("(\"foo\" AND \"bar\") AND \"baz\"", parsed.TextMatch);
    }

    [Theory]
    [InlineData("size:larger")]
    [InlineData("-in:Archive")]
    [InlineData("after:not-a-date")]
    [InlineData("category:promotions")]
    public void FallsBackToLiteralSearchForUnsupportedKeyedSyntax(string query)
    {
        var parsed = MailQueryParser.Parse(query);

        Assert.Equal(query, parsed.Criteria.Text);
        Assert.Null(parsed.TextMatch);
        Assert.Null(parsed.ExcludedTextMatch);
    }

    [Fact]
    public void FallsBackToLiteralSearchOnUnterminatedQuote()
    {
        var parsed = MailQueryParser.Parse("alpha \"unterminated");

        Assert.Equal("alpha \"unterminated", parsed.Criteria.Text);
        Assert.Null(parsed.TextMatch);
    }

    [Fact]
    public void NegatesFlagAndAttachmentOperators()
    {
        var parsed = MailQueryParser.Parse("-is:read is:flagged -has:attachment");

        Assert.False(parsed.Criteria.IsRead);
        Assert.True(parsed.Criteria.IsFlagged);
        Assert.False(parsed.Criteria.HasAttachments);
        Assert.Null(parsed.TextMatch);
    }

    [Fact]
    public void NegatesAddressOperatorsAsColumnPrefixFilters()
    {
        var parsed = MailQueryParser.Parse("-from:ana");

        Assert.Null(parsed.Criteria.Sender);
        Assert.Null(parsed.TextMatch);
        Assert.Equal("sender : (\"ana\"*)", parsed.ExcludedTextMatch);
    }

    [Fact]
    public void ParsesIsoDateBoundsAsAnInclusiveDayRange()
    {
        var parsed = MailQueryParser.Parse("after:2026-01-01 before:2026-01-31");

        Assert.Equal(new DateTime(2026, 1, 1), parsed.Criteria.After!.Value.Date);
        Assert.Equal(new DateTime(2026, 1, 31), parsed.Criteria.Before!.Value.Date);
        Assert.True(parsed.Criteria.Before > parsed.Criteria.After);
    }

    [Fact]
    public void FallsBackToLiteralSearchOnInvertedDateRange()
    {
        var parsed = MailQueryParser.Parse("after:2026-02-01 before:2026-01-01");

        Assert.Equal("after:2026-02-01 before:2026-01-01", parsed.Criteria.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyQueriesMatchEverythingLikeTheLiteralPath(string? query)
    {
        var parsed = MailQueryParser.Parse(query);

        Assert.Null(parsed.Criteria.Text);
        Assert.Null(parsed.TextMatch);
        Assert.Null(parsed.ExcludedTextMatch);
    }

    [Fact]
    public void PunctuationOnlyQueryFallsBackToLiteralSearch()
    {
        var parsed = MailQueryParser.Parse("!!! ???");

        Assert.Equal("!!! ???" , parsed.Criteria.Text);
        Assert.Null(parsed.TextMatch);
    }

    [Fact]
    public async Task PhraseOperatorMatchesAdjacentTokensOnly()
    {
        using var directory = new TemporaryDirectory();
        using var database = NewDatabase(directory);
        await database.SaveMessageAsync(CreateMessage("adjacent", "Invoice paid today", "see the totals"));
        await database.SaveMessageAsync(CreateMessage("separated", "Payment status", "the invoice and paid amounts"));

        var matches = await database.SearchAsync(MailQueryParser.Parse("\"invoice paid\""));

        var ids = matches.Select(match => match.Id).ToList();
        Assert.Equal(["adjacent"], ids);
    }

    [Fact]
    public async Task NegatedTermExcludesMatchesFromPositiveResults()
    {
        using var directory = new TemporaryDirectory();
        using var database = NewDatabase(directory);
        await database.SaveMessageAsync(CreateMessage("reviewed", "Status", "invoice please review it"));
        await database.SaveMessageAsync(CreateMessage("clean", "Status", "invoice attached here"));

        var matches = await database.SearchAsync(MailQueryParser.Parse("invoice -review"));

        var ids = matches.Select(match => match.Id).ToList();
        Assert.Equal(["clean"], ids);
    }

    [Fact]
    public async Task NegationOnlyQueryReturnsEverythingExceptTheExcludedTerm()
    {
        using var directory = new TemporaryDirectory();
        using var database = NewDatabase(directory);
        await database.SaveMessageAsync(CreateMessage("spammy", "Offers", "review this spam content"));
        await database.SaveMessageAsync(CreateMessage("first", "Status", "plain content"));
        await database.SaveMessageAsync(CreateMessage("second", "Status", "more plain content"));

        var matches = await database.SearchAsync(MailQueryParser.Parse("-spam"));

        var ids = matches.Select(match => match.Id).ToList();
        Assert.Equal(["first", "second"], ids);
    }

    [Fact]
    public async Task OrGroupMatchesAnyAlternative()
    {
        using var directory = new TemporaryDirectory();
        using var database = NewDatabase(directory);
        await database.SaveMessageAsync(CreateMessage("alpha", "Alpha doc", "nothing else"));
        await database.SaveMessageAsync(CreateMessage("beta", "Beta doc", "nothing else"));
        await database.SaveMessageAsync(CreateMessage("other", "Other doc", "nothing else"));

        var matches = await database.SearchAsync(MailQueryParser.Parse("(alpha beta)"));

        var ids = matches.Select(match => match.Id).ToList();
        Assert.Equal(["alpha", "beta"], ids);
    }

    [Fact]
    public async Task SubjectOperatorIgnoresBodyOnlyOccurrences()
    {
        using var directory = new TemporaryDirectory();
        using var database = NewDatabase(directory);
        await database.SaveMessageAsync(CreateMessage("in-subject", "Zebra report", "plain body"));
        await database.SaveMessageAsync(CreateMessage("in-body", "Plain subject", "the zebra escaped"));

        var matches = await database.SearchAsync(MailQueryParser.Parse("subject:zebra"));

        var ids = matches.Select(match => match.Id).ToList();
        Assert.Equal(["in-subject"], ids);
    }

    [Fact]
    public async Task StructuredOperatorsCombineWithTextAndAccountScope()
    {
        using var directory = new TemporaryDirectory();
        using var database = NewDatabase(directory);
        await database.SaveAccountAsync(CreateAccount("acc-1", "ana@example.com"));
        await database.SaveAccountAsync(CreateAccount("acc-2", "bob@example.com"));
        await database.SaveMessageAsync(CreateMessage("unread-ana", "Quarterly report", "numbers inside", accountId: "acc-1"));
        await database.SaveMessageAsync(CreateMessage("read-bob", "Quarterly report", "numbers inside", isRead: true, folder: "Archive", sender: "bob@example.com", accountId: "acc-2"));
        await database.SaveMessageAsync(CreateMessage("read-ana", "Quarterly report", "numbers inside", isRead: true, accountId: "acc-1"));

        var parsed = MailQueryParser.Parse("from:ana is:unread quarterly");
        var matches = await database.SearchAsync(parsed);
        Assert.Equal(["unread-ana"], matches.Select(match => match.Id).ToList());

        var scoped = MailQueryParser.Parse("quarterly", "acc-2");
        var scopedMatches = await database.SearchAsync(scoped);
        Assert.Equal(["read-bob"], scopedMatches.Select(match => match.Id).ToList());
    }

    private static MailAccountRecord CreateAccount(string id, string emailAddress)
    {
        return new MailAccountRecord(
            id,
            emailAddress,
            "Ana",
            "imap.example.com",
            993,
            MailTransportSecurity.SslOnConnect,
            "smtp.example.com",
            587,
            MailTransportSecurity.StartTls,
            MailAuthenticationMethod.OAuth2);
    }

    [Fact]
    public async Task DateSortIsTheDefaultAndBreaksTiesByInsertionId()
    {
        using var directory = new TemporaryDirectory();
        using var database = NewDatabase(directory);
        var stamp = DateTimeOffset.Parse("2026-10-12T12:00:00Z");
        await database.SaveMessageAsync(CreateMessage("b-new", "Same subject", "shared body", receivedAt: stamp));
        await database.SaveMessageAsync(CreateMessage("old", "Same subject", "shared body", receivedAt: stamp.AddDays(-1)));
        await database.SaveMessageAsync(CreateMessage("a-new", "Same subject", "shared body", receivedAt: stamp));

        var matches = await database.SearchAsync(new SearchCriteria());

        Assert.Equal(["a-new", "b-new", "old"], matches.Select(match => match.Id).ToList());
        Assert.All(matches, match => Assert.Null(match.RelevanceScore));
    }

    [Fact]
    public async Task RelevanceSortRanksStrongerMatchesFirstAndExposesScores()
    {
        using var directory = new TemporaryDirectory();
        using var database = NewDatabase(directory);
        await database.SaveMessageAsync(CreateMessage("weak-new", "Newsletter", "one alpha mention", receivedAt: DateTimeOffset.Parse("2026-10-20T12:00:00Z")));
        await database.SaveMessageAsync(CreateMessage("strong-old", "Newsletter", "alpha alpha alpha alpha deep", receivedAt: DateTimeOffset.Parse("2026-10-05T12:00:00Z")));

        var byDate = await database.SearchAsync(new SearchCriteria(text: "alpha"));
        Assert.Equal(["weak-new", "strong-old"], byDate.Select(match => match.Id).ToList());

        var byRelevance = await database.SearchAsync(new SearchCriteria(text: "alpha"), sort: SearchSort.Relevance);
        Assert.Equal(["strong-old", "weak-new"], byRelevance.Select(match => match.Id).ToList());
        Assert.NotNull(byRelevance[0].RelevanceScore);
        Assert.NotNull(byRelevance[1].RelevanceScore);
        Assert.True(byRelevance[0].RelevanceScore < byRelevance[1].RelevanceScore);
    }

    [Fact]
    public async Task RelevanceSortWithoutFullTextFallsBackToDateOrdering()
    {
        using var directory = new TemporaryDirectory();
        using var database = NewDatabase(directory);
        await database.SaveMessageAsync(CreateMessage("old-unread", "Report", "no keywords", receivedAt: DateTimeOffset.Parse("2026-10-01T12:00:00Z")));
        await database.SaveMessageAsync(CreateMessage("new-unread", "Report", "no keywords", receivedAt: DateTimeOffset.Parse("2026-10-15T12:00:00Z")));

        var matches = await database.SearchAsync(
            new SearchCriteria(isRead: false),
            sort: SearchSort.Relevance);

        Assert.Equal(["new-unread", "old-unread"], matches.Select(match => match.Id).ToList());
        Assert.All(matches, match => Assert.Null(match.RelevanceScore));
    }

    [Fact]
    public async Task RelevanceSortWithNegationOnlyQueryFallsBackToDateOrdering()
    {
        using var directory = new TemporaryDirectory();
        using var database = NewDatabase(directory);
        await database.SaveMessageAsync(CreateMessage("spammy", "Offers", "spam content", receivedAt: DateTimeOffset.Parse("2026-10-16T12:00:00Z")));
        await database.SaveMessageAsync(CreateMessage("newest", "Status", "plain content", receivedAt: DateTimeOffset.Parse("2026-10-18T12:00:00Z")));
        await database.SaveMessageAsync(CreateMessage("older", "Status", "plain content", receivedAt: DateTimeOffset.Parse("2026-10-10T12:00:00Z")));

        var matches = await database.SearchAsync(
            MailQueryParser.Parse("-spam"),
            sort: SearchSort.Relevance);

        Assert.Equal(["newest", "older"], matches.Select(match => match.Id).ToList());
        Assert.All(matches, match => Assert.Null(match.RelevanceScore));
    }

    [Fact]
    public void InterpretsRomanianSenderFlagAndUnreadPatterns()
    {
        var plan = NaturalLanguageQueryInterpreter.Interpret("de la ana marcate necitite");

        Assert.True(plan.IsNaturalLanguage);
        Assert.Equal("from:ana is:flagged is:unread", plan.QuerySyntax);
        Assert.Contains(plan.Items, item => item.Operator == "from" && item.Value == "ana");
    }

    [Fact]
    public void InterpretsEnglishAttachmentAndSubjectPatterns()
    {
        var plan = NaturalLanguageQueryInterpreter.Interpret("has attachment subject invoice from bob@x.com");

        Assert.True(plan.IsNaturalLanguage);
        Assert.Equal("has:attachment subject:invoice from:bob@x.com", plan.QuerySyntax);
    }

    [Fact]
    public void InterpretsQuotedSubjectValues()
    {
        var plan = NaturalLanguageQueryInterpreter.Interpret("subiect \"factura lumii\"");

        Assert.True(plan.IsNaturalLanguage);
        Assert.Equal("subject:\"factura lumii\"", plan.QuerySyntax);
    }

    [Fact]
    public void InterpretsRelativeDatesWithAFixedClock()
    {
        var now = DateTimeOffset.Parse("2026-10-29T15:00:00+02:00");

        var yesterday = NaturalLanguageQueryInterpreter.Interpret("facturi ieri", now);
        Assert.True(yesterday.IsNaturalLanguage);
        Assert.Equal("facturi after:2026-10-28 before:2026-10-28", yesterday.QuerySyntax);

        var lastWeek = NaturalLanguageQueryInterpreter.Interpret("de la ana săptămâna trecută", now);
        Assert.True(lastWeek.IsNaturalLanguage);
        Assert.Equal("from:ana after:2026-10-19 before:2026-10-25", lastWeek.QuerySyntax);
    }

    [Fact]
    public void InterpretsYearRanges()
    {
        var now = DateTimeOffset.Parse("2026-10-29T15:00:00Z");

        var plan = NaturalLanguageQueryInterpreter.Interpret("rapoarte anul trecut", now);

        Assert.True(plan.IsNaturalLanguage);
        Assert.Equal("rapoarte after:2025-01-01 before:2025-12-31", plan.QuerySyntax);
    }

    [Fact]
    public void FallsBackToLiteralWhenNothingMatches()
    {
        var plan = NaturalLanguageQueryInterpreter.Interpret("hello random world");

        Assert.False(plan.IsNaturalLanguage);
        Assert.Equal("hello random world", plan.QuerySyntax);
        Assert.Empty(plan.Items);
    }

    [Fact]
    public void PlanSyntaxRoundTripsThroughTheQueryParser()
    {
        var plan = NaturalLanguageQueryInterpreter.Interpret("de la ana@example.com cu atașament necitite");
        var parsed = MailQueryParser.Parse(plan.QuerySyntax);

        Assert.Equal("ana@example.com", parsed.Criteria.Sender);
        Assert.True(parsed.Criteria.HasAttachments);
        Assert.Equal(false, parsed.Criteria.IsRead);
    }

    [Fact]
    public async Task InterpretedQueryFindsExpectedMessagesEndToEnd()
    {
        using var directory = new TemporaryDirectory();
        using var database = NewDatabase(directory);
        await database.SaveMessageAsync(CreateMessage("hit", "Invoice", "totals inside", hasAttachments: true));
        await database.SaveMessageAsync(CreateMessage("miss", "Invoice", "totals inside", hasAttachments: true, isRead: true));
        await database.SaveMessageAsync(CreateMessage("miss2", "Note", "plain body"));

        var plan = NaturalLanguageQueryInterpreter.Interpret("subiect invoice cu atașament necitit");
        Assert.True(plan.IsNaturalLanguage);
        var matches = await database.SearchAsync(MailQueryParser.Parse(plan.QuerySyntax));

        Assert.Equal(["hit"], matches.Select(match => match.Id).ToList());
    }

    private static EncryptedMailDatabase NewDatabase(TemporaryDirectory directory)
    {
        var database = new EncryptedMailDatabase(
            System.IO.Path.Combine(directory.Path, "mail.db"),
            RandomNumberGenerator.GetBytes(32));
        database.Initialize();
        return database;
    }

    private static MailMessageRecord CreateMessage(
        string id,
        string subject,
        string body,
        bool isRead = false,
        bool isFlagged = false,
        bool hasAttachments = false,
        string folder = "Inbox",
        string? sender = null,
        string? accountId = null,
        DateTimeOffset? receivedAt = null)
    {
        return new MailMessageRecord(
            id,
            sender ?? "ana@example.com",
            "team@example.com",
            subject,
            body,
            receivedAt ?? DateTimeOffset.Parse("2026-10-12T12:00:00Z"),
            folder,
            isRead,
            isFlagged,
            hasAttachments,
            accountId);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
