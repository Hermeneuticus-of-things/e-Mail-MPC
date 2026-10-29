using System.Text.RegularExpressions;

namespace EMailMPC.Core.Search;

/// <summary>One interpreted piece of a natural-language query.</summary>
public sealed record QueryPlanItem(string Operator, string Value, double Confidence, string MatchedText);

/// <summary>
/// The structured result of interpreting a natural-language query: the matched
/// items, the equivalent eM Client style query syntax (editable), and whether the
/// text was recognized as natural language at all.
/// </summary>
public sealed record NaturalLanguageQueryPlan(
    string OriginalText,
    IReadOnlyList<QueryPlanItem> Items,
    string QuerySyntax,
    double Confidence,
    bool IsNaturalLanguage);

/// <summary>
/// Interprets plain Romanian/English search phrases ("de la ana necitite ieri",
/// "from bob with attachment this week") into a structured query plan expressed in
/// the local MailQueryParser syntax. Deterministic and fully offline — eM Client
/// solves this step with a cloud LLM, this interpreter intentionally does not.
/// Unrecognized text falls back to the literal search path (IsNaturalLanguage =
/// false, QuerySyntax = original text).
/// </summary>
public static class NaturalLanguageQueryInterpreter
{
    private const double PatternConfidence = 0.9;

    private static readonly Regex TodayPattern = BuildPattern(@"\b(?:azi|astăzi|astazi|today)\b");
    private static readonly Regex YesterdayPattern = BuildPattern(@"\b(?:ieri|yesterday)\b");
    private static readonly Regex ThisWeekPattern = BuildPattern(@"\b(?:săptămâna aceasta|saptamana aceasta|săptămâna asta|saptamana asta|this week)\b");
    private static readonly Regex LastWeekPattern = BuildPattern(@"\b(?:săptămâna trecută|saptamana trecuta|last week)\b");
    private static readonly Regex ThisMonthPattern = BuildPattern(@"\b(?:luna aceasta|luna asta|this month)\b");
    private static readonly Regex LastMonthPattern = BuildPattern(@"\b(?:luna trecută|luna trecuta|last month)\b");
    private static readonly Regex ThisYearPattern = BuildPattern(@"\b(?:anul acesta|anul asta|this year)\b");
    private static readonly Regex LastYearPattern = BuildPattern(@"\b(?:anul trecut|last year)\b");

    private static readonly (Regex Pattern, string Operator)[] SimplePatterns =
    [
        (BuildPattern(@"\b(?:cu\s+atașament(?:e|ul|ele)?|cu\s+atasament(?:e|ul|ele)?|has\s+attachments?|with\s+attachments?|attachment|atașament|atasament)\b"), "has:attachment"),
        (BuildPattern(@"\b(?:fără\s+atașament(?:e)?|fara\s+atasament(?:e)?|no\s+attachments?|without\s+attachments?)\b"), "-has:attachment"),
        (BuildPattern(@"\b(?:necitit(?:e|ul)?|unread)\b"), "is:unread"),
        (BuildPattern(@"\b(?:citit(?:e)?|read)\b"), "is:read"),
        (BuildPattern(@"\b(?:marcat(?:e)?|cu\s+stea|flagged|starred)\b"), "is:flagged"),
        (BuildPattern(@"\b(?:nemarcat(?:e)?|unflagged)\b"), "is:unflagged"),
    ];

    private static readonly (Regex Pattern, string Operator)[] ValuePatterns =
    [
        (BuildPattern(@"(?:de\s+la|from|expeditor(?:ul)?|trimis\s+de(?:\s+către)?)\s+""([^""]+)""|(?:de\s+la|from|expeditor(?:ul)?|trimis\s+de(?:\s+către)?)\s+([\p{L}\p{N}._@-]+)"), "from"),
        (BuildPattern(@"(?:către|catre|to|destinatar(?:ul)?|pentru)\s+""([^""]+)""|(?:către|catre|to|destinatar(?:ul)?|pentru)\s+([\p{L}\p{N}._@-]+)"), "to"),
        (BuildPattern(@"(?:subiect(?:ul)?|subject)\s+""([^""]+)""|(?:subiect(?:ul)?|subject)\s+([\p{L}\p{N}]+)"), "subject"),
        (BuildPattern(@"(?:conținut(?:ul)?|continut(?:ul)?|corp(?:ul)?|body)\s+""([^""]+)""|(?:conținut(?:ul)?|continut(?:ul)?|corp(?:ul)?|body)\s+([\p{L}\p{N}]+)"), "body"),
        (BuildPattern(@"(?:în\s+dosarul|in\s+dosarul|în\s+folderul|in\s+folderul|dosar(?:ul)?|folder(?:ul)?)\s+""([^""]+)""|(?:în\s+dosarul|in\s+dosarul|dosar(?:ul)?|folder(?:ul)?)\s+([\p{L}\p{N}]+)"), "in"),
    ];

    public static NaturalLanguageQueryPlan Interpret(string? text)
    {
        return Interpret(text, DateTimeOffset.Now);
    }

    public static NaturalLanguageQueryPlan Interpret(string? text, DateTimeOffset now)
    {
        var raw = text?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            return new NaturalLanguageQueryPlan(raw, [], raw, 0, false);
        }

        var items = new List<(int Position, QueryPlanItem Item)>();
        var consumed = new List<(int Start, int End)>();

        CollectDateItem(TodayPattern, raw, consumed, items, now, "after");
        CollectRange(YesterdayPattern, "yesterday", raw, consumed, items);
        CollectRange(ThisWeekPattern, "thisweek", raw, consumed, items);
        CollectRange(LastWeekPattern, "lastweek", raw, consumed, items);
        CollectRange(ThisMonthPattern, "thismonth", raw, consumed, items);
        CollectRange(LastMonthPattern, "lastmonth", raw, consumed, items);
        CollectRange(ThisYearPattern, "thisyear", raw, consumed, items);
        CollectRange(LastYearPattern, "lastyear", raw, consumed, items);

        foreach (var (pattern, op) in SimplePatterns)
        {
            Collect(pattern, raw, consumed, items, match => new QueryPlanItem(op, string.Empty, PatternConfidence, match.Value.Trim()));
        }

        foreach (var (pattern, op) in ValuePatterns)
        {
            Collect(pattern, raw, consumed, items, match =>
            {
                var value = CleanValue(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
                return value.Length == 0
                    ? null
                    : new QueryPlanItem(op, value, PatternConfidence, match.Value.Trim());
            });
        }

        if (items.Count == 0)
        {
            return new NaturalLanguageQueryPlan(raw, [], raw, 0, false);
        }

        var entries = new List<(int Position, int Order, string Fragment)>();
        foreach (var (position, item) in items)
        {
            foreach (var fragment in BuildSyntax(item, now))
            {
                entries.Add((position, entries.Count, fragment));
            }
        }

        foreach (var (position, word) in ExtractResidual(raw, consumed))
        {
            entries.Add((position, entries.Count, word));
        }

        var syntax = string.Join(' ', entries
            .OrderBy(entry => entry.Position)
            .ThenBy(entry => entry.Order)
            .Select(entry => entry.Fragment));

        var planItems = items.Select(entry => entry.Item).ToList();
        return new NaturalLanguageQueryPlan(raw, planItems, syntax, planItems.Min(item => item.Confidence), true);
    }

    private static void Collect(
        Regex pattern,
        string raw,
        List<(int Start, int End)> consumed,
        List<(int Position, QueryPlanItem Item)> items,
        Func<Match, QueryPlanItem?> map)
    {
        foreach (Match match in pattern.Matches(raw))
        {
            if (consumed.Any(span => match.Index < span.End && match.Index + match.Length > span.Start))
            {
                continue;
            }

            if (map(match) is { } item)
            {
                items.Add((match.Index, item));
                consumed.Add((match.Index, match.Index + match.Length));
            }
        }
    }

    private static void CollectDateItem(
        Regex pattern,
        string raw,
        List<(int Start, int End)> consumed,
        List<(int Position, QueryPlanItem Item)> items,
        DateTimeOffset now,
        string op)
    {
        Collect(pattern, raw, consumed, items, match =>
            new QueryPlanItem(op, now.ToString("yyyy-MM-dd"), PatternConfidence, match.Value.Trim()));
    }

    private static void CollectRange(
        Regex pattern,
        string range,
        string raw,
        List<(int Start, int End)> consumed,
        List<(int Position, QueryPlanItem Item)> items)
    {
        Collect(pattern, raw, consumed, items, match =>
            new QueryPlanItem($"range:{range}", string.Empty, PatternConfidence, match.Value.Trim()));
    }

    private static IEnumerable<string> BuildSyntax(QueryPlanItem item, DateTimeOffset now)
    {
        switch (item.Operator)
        {
            case "range:yesterday":
                yield return $"after:{now.AddDays(-1):yyyy-MM-dd}";
                yield return $"before:{now.AddDays(-1):yyyy-MM-dd}";
                break;
            case "range:thisweek":
                yield return $"after:{WeekStart(now):yyyy-MM-dd}";
                break;
            case "range:lastweek":
                yield return $"after:{WeekStart(now).AddDays(-7):yyyy-MM-dd}";
                yield return $"before:{WeekStart(now).AddDays(-1):yyyy-MM-dd}";
                break;
            case "range:thismonth":
                yield return $"after:{new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset):yyyy-MM-dd}";
                break;
            case "range:lastmonth":
                var firstOfMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
                yield return $"after:{firstOfMonth.AddMonths(-1):yyyy-MM-dd}";
                yield return $"before:{firstOfMonth.AddDays(-1):yyyy-MM-dd}";
                break;
            case "range:thisyear":
                yield return $"after:{now.Year}-01-01";
                break;
            case "range:lastyear":
                yield return $"after:{now.Year - 1}-01-01";
                yield return $"before:{now.Year - 1}-12-31";
                break;
            default:
                if (item.Value.Length == 0)
                {
                    yield return item.Operator;
                }
                else if (item.Value.Contains(' '))
                {
                    yield return $"{item.Operator}:\"{item.Value}\"";
                }
                else
                {
                    yield return $"{item.Operator}:{item.Value}";
                }

                break;
        }
    }

    private static DateTimeOffset WeekStart(DateTimeOffset now)
    {
        return now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
    }

    private static List<(int Position, string Word)> ExtractResidual(string raw, List<(int Start, int End)> consumed)
    {
        var pieces = new List<(int Position, string Word)>();
        var index = 0;
        foreach (var (start, end) in consumed.OrderBy(span => span.Start))
        {
            if (start > index)
            {
                AddWords(pieces, raw, index, start);
            }

            index = Math.Max(index, end);
        }

        if (index < raw.Length)
        {
            AddWords(pieces, raw, index, raw.Length);
        }

        return pieces;
    }

    private static void AddWords(List<(int Position, string Word)> pieces, string raw, int start, int end)
    {
        foreach (Match match in TokenWordPattern.Matches(raw[start..end]))
        {
            var clean = CleanValue(match.Value);
            if (clean.Length > 1)
            {
                pieces.Add((start + match.Index, clean));
            }
        }
    }

    private static readonly Regex TokenWordPattern = new(@"\S+", RegexOptions.Compiled);

    private static string CleanValue(string value)
    {
        return value.Trim().Trim(' ', '.', ',', ';', ':', '!', '?', '"', '\'', '»', '«');
    }

    private static Regex BuildPattern(string pattern)
    {
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
