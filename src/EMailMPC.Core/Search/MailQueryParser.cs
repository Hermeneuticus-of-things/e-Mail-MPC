using System.Globalization;
using System.Text.RegularExpressions;

namespace EMailMPC.Core.Search;

/// <summary>
/// A search query split by <see cref="MailQueryParser"/> into structured criteria and a
/// full-text match expression. <see cref="Criteria.Text"/> is only populated when the
/// parser fell back to the literal whole-string search; in that case both match
/// expressions are null.
/// </summary>
public sealed record ParsedMailQuery(SearchCriteria Criteria, string? TextMatch, string? ExcludedTextMatch);

/// <summary>
/// Parses eM Client style query syntax (keyed operators, quoted phrases, negation,
/// parenthesized OR groups) into a <see cref="ParsedMailQuery"/>. The parser never
/// throws: queries it cannot represent exactly fall back to the literal search that
/// treats the raw text as a conjunction of tokens.
/// </summary>
public static class MailQueryParser
{
    private static readonly Regex TokenPattern = new(@"[\p{L}\p{M}\p{N}]+", RegexOptions.Compiled);
    private static readonly Regex DateOnlyPattern = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    public static ParsedMailQuery Parse(string? query)
    {
        return Parse(query, accountId: null);
    }

    public static ParsedMailQuery Parse(string? query, string? accountId)
    {
        var raw = query?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            return new ParsedMailQuery(new SearchCriteria(accountId: accountId), null, null);
        }

        var terms = Tokenize(raw);
        if (terms is null)
        {
            return Literal(raw, accountId);
        }

        var builder = new QueryBuilder();
        foreach (var term in terms)
        {
            if (!builder.Add(term))
            {
                return Literal(raw, accountId);
            }
        }

        return builder.Build(raw, accountId);
    }

    private static ParsedMailQuery Literal(string raw, string? accountId)
    {
        return new ParsedMailQuery(new SearchCriteria(text: raw, accountId: accountId), null, null);
    }

    private static List<QueryTerm>? Tokenize(string raw)
    {
        var terms = new List<QueryTerm>();
        var index = 0;
        while (index < raw.Length)
        {
            if (char.IsWhiteSpace(raw[index]))
            {
                index++;
                continue;
            }

            var negated = false;
            if (raw[index] == '-')
            {
                negated = true;
                index++;
                if (index >= raw.Length || char.IsWhiteSpace(raw[index]))
                {
                    return null;
                }
            }

            if (raw[index] == '(')
            {
                var closing = raw.IndexOf(')', index + 1);
                var nested = raw.IndexOf('(', index + 1);
                if (closing < 0 || (nested >= 0 && nested < closing))
                {
                    return null;
                }

                var inner = Tokenize(raw[(index + 1)..closing]);
                if (inner is null || inner.Count == 0)
                {
                    return null;
                }

                terms.Add(new QueryTerm(negated, null, string.Empty, false, inner));
                index = closing + 1;
                continue;
            }

            if (raw[index] == ')')
            {
                return null;
            }

            if (raw[index] == '"')
            {
                var closing = raw.IndexOf('"', index + 1);
                if (closing < 0)
                {
                    return null;
                }

                terms.Add(new QueryTerm(negated, null, raw[(index + 1)..closing], Quoted: true));
                index = closing + 1;
                continue;
            }

            var start = index;
            while (index < raw.Length
                && !char.IsWhiteSpace(raw[index])
                && raw[index] is not '(' and not ')' and not '"' and not ':')
            {
                index++;
            }

            var word = raw[start..index];
            if (index >= raw.Length || raw[index] != ':')
            {
                terms.Add(new QueryTerm(negated, null, word, Quoted: false));
                continue;
            }

            index++;
            string value;
            var valueQuoted = false;
            if (index < raw.Length && raw[index] == '"')
            {
                var closing = raw.IndexOf('"', index + 1);
                if (closing < 0)
                {
                    return null;
                }

                value = raw[(index + 1)..closing];
                valueQuoted = true;
                index = closing + 1;
            }
            else
            {
                var valueStart = index;
                while (index < raw.Length
                    && !char.IsWhiteSpace(raw[index])
                    && raw[index] is not '(' and not ')' and not '"')
                {
                    index++;
                }

                value = raw[valueStart..index];
            }

            if (word.Length == 0 || value.Length == 0)
            {
                return null;
            }

            terms.Add(new QueryTerm(negated, word, value, valueQuoted));
        }

        return terms;
    }

    private sealed class QueryBuilder
    {
        private readonly List<string> _positiveClauses = [];
        private readonly List<string> _negativeClauses = [];
        private string? _sender;
        private string? _recipient;
        private string? _folder;
        private DateTimeOffset? _after;
        private DateTimeOffset? _before;
        private bool? _isRead;
        private bool? _isFlagged;
        private bool? _hasAttachments;

        public bool Add(QueryTerm term)
        {
            if (term.GroupTerms is not null)
            {
                return AddGroup(term);
            }

            if (term.Key is null)
            {
                var clause = BuildTextClause(term.Value, term.Quoted);
                if (clause is not null)
                {
                    AddClause(clause, term.Negated);
                }

                return true;
            }

            return AddKeyedTerm(term);
        }

        private bool AddGroup(QueryTerm group)
        {
            var innerClauses = new List<string>();
            foreach (var inner in group.GroupTerms!)
            {
                string? clause;
                if (inner.Key is null)
                {
                    clause = BuildTextClause(inner.Value, inner.Quoted);
                }
                else
                {
                    // Keyed operators cannot take part in an OR group, so they degrade
                    // to plain text tokens.
                    var tokens = ExtractTokens($"{inner.Key} {inner.Value}");
                    clause = tokens.Count == 0
                        ? null
                        : $"({string.Join(" AND ", tokens.Select(QuoteToken))})";
                }

                if (clause is not null)
                {
                    innerClauses.Add(clause);
                }
            }

            if (innerClauses.Count == 0)
            {
                return true;
            }

            AddClause($"({string.Join(" OR ", innerClauses)})", group.Negated);
            return true;
        }

        private bool AddKeyedTerm(QueryTerm term)
        {
            switch (term.Key.ToLowerInvariant())
            {
                case "from":
                    if (!term.Negated)
                    {
                        _sender = term.Value;
                        return true;
                    }

                    AddClause($"sender : ({BuildPrefixClause(term.Value)})", negated: true);
                    return true;
                case "to" or "cc" or "bcc":
                    if (!term.Negated)
                    {
                        _recipient = term.Value;
                        return true;
                    }

                    AddClause($"recipients : ({BuildPrefixClause(term.Value)})", negated: true);
                    return true;
                case "subject" or "body":
                    return AddColumnTerm(term, term.Key.Equals("subject", StringComparison.OrdinalIgnoreCase) ? "subject" : "body_text");
                case "in":
                    if (term.Negated)
                    {
                        return false;
                    }

                    _folder = term.Value;
                    return true;
                case "is":
                    return AddFlagValue(term);
                case "has":
                    if (!term.Value.Equals("attachment", StringComparison.OrdinalIgnoreCase)
                        && !term.Value.Equals("attachments", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    _hasAttachments = !term.Negated;
                    return true;
                case "read" or "unread" or "flagged":
                    return AddStandaloneFlag(term);
                case "after" or "before":
                    if (term.Negated
                        || !TryParseDateValue(
                            term.Value,
                            isBefore: term.Key.Equals("before", StringComparison.OrdinalIgnoreCase),
                            out var date))
                    {
                        return false;
                    }

                    if (term.Key.Equals("after", StringComparison.OrdinalIgnoreCase))
                    {
                        _after = date;
                    }
                    else
                    {
                        _before = date;
                    }

                    return true;
                case "date" or "size" or "tag" or "category" or "importance" or "attachment":
                    // Keys eM Client supports but the local schema has no counterpart for.
                    return false;
                default:
                    var degraded = ExtractTokens($"{term.Key} {term.Value}");
                    if (degraded.Count > 0)
                    {
                        AddClause($"({string.Join(" AND ", degraded.Select(QuoteToken))})", term.Negated);
                    }

                    return true;
            }
        }

        private bool AddColumnTerm(QueryTerm term, string column)
        {
            var tokens = ExtractTokens(term.Value);
            if (tokens.Count == 0)
            {
                return false;
            }

            var inner = term.Quoted
                ? QuoteToken(string.Join(' ', tokens))
                : string.Join(" AND ", tokens.Select(QuoteToken));
            AddClause($"{column} : ({inner})", term.Negated);
            return true;
        }

        private bool AddFlagValue(QueryTerm term)
        {
            var truth = term.Value.ToLowerInvariant() switch
            {
                "read" or "flagged" => true,
                "unread" or "unflagged" => false,
                _ => (bool?)null
            };

            if (truth is null)
            {
                return false;
            }

            var effective = truth.Value != term.Negated;
            var targetsFlags = term.Value.Equals("flagged", StringComparison.OrdinalIgnoreCase)
                || term.Value.Equals("unflagged", StringComparison.OrdinalIgnoreCase);
            if (targetsFlags)
            {
                _isFlagged = effective;
            }
            else
            {
                _isRead = effective;
            }

            return true;
        }

        private bool AddStandaloneFlag(QueryTerm term)
        {
            if (!TryParseBoolean(term.Value, out var value))
            {
                return false;
            }

            var truth = term.Key.Equals("unread", StringComparison.OrdinalIgnoreCase) ? !value : value;
            var effective = truth != term.Negated;
            if (term.Key.Equals("flagged", StringComparison.OrdinalIgnoreCase))
            {
                _isFlagged = effective;
            }
            else
            {
                _isRead = effective;
            }

            return true;
        }

        private void AddClause(string clause, bool negated)
        {
            if (negated)
            {
                _negativeClauses.Add(clause);
            }
            else
            {
                _positiveClauses.Add(clause);
            }
        }

        public ParsedMailQuery Build(string raw, string? accountId)
        {
            if (_after is not null && _before is not null && _after > _before)
            {
                return Literal(raw, accountId);
            }

            string? textMatch = null;
            string? excludedTextMatch = null;
            if (_positiveClauses.Count > 0)
            {
                // FTS5 has no unary NOT, so negated clauses chain onto the positive ones.
                textMatch = string.Join(" AND ", _positiveClauses)
                    + string.Join(string.Empty, _negativeClauses.Select(negated => $" NOT ({negated})"));
            }
            else if (_negativeClauses.Count > 0)
            {
                excludedTextMatch = string.Join(" OR ", _negativeClauses);
            }

            var criteria = new SearchCriteria(
                sender: _sender,
                recipient: _recipient,
                folder: _folder,
                after: _after,
                before: _before,
                isRead: _isRead,
                isFlagged: _isFlagged,
                hasAttachments: _hasAttachments,
                accountId: accountId);
            if (textMatch is null
                && excludedTextMatch is null
                && criteria.Sender is null
                && criteria.Recipient is null
                && criteria.Folder is null
                && criteria.After is null
                && criteria.Before is null
                && criteria.IsRead is null
                && criteria.IsFlagged is null
                && criteria.HasAttachments is null)
            {
                // Every term vanished (e.g. punctuation only): keep the literal search
                // contract that such a query matches nothing.
                return Literal(raw, accountId);
            }

            return new ParsedMailQuery(criteria, textMatch, excludedTextMatch);
        }
    }

    private static string? BuildTextClause(string value, bool quoted)
    {
        var tokens = ExtractTokens(value);
        if (tokens.Count == 0)
        {
            return null;
        }

        if (quoted)
        {
            return QuoteToken(string.Join(' ', tokens));
        }

        return tokens.Count == 1
            ? QuoteToken(tokens[0])
            : $"({string.Join(" AND ", tokens.Select(QuoteToken))})";
    }

    private static string BuildPrefixClause(string value)
    {
        return string.Join(" OR ", ExtractTokens(value).Select(token => $"\"{token}\"*"));
    }

    private static List<string> ExtractTokens(string value)
    {
        return TokenPattern.Matches(value)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string QuoteToken(string token)
    {
        return $"\"{token.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static bool TryParseBoolean(string value, out bool result)
    {
        switch (value.ToLowerInvariant())
        {
            case "true" or "yes" or "1":
                result = true;
                return true;
            case "false" or "no" or "0":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }

    private static bool TryParseDateValue(string value, bool isBefore, out DateTimeOffset result)
    {
        if (DateOnlyPattern.IsMatch(value))
        {
            if (!DateTimeOffset.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var date))
            {
                result = default;
                return false;
            }

            // A bare day used as the upper bound stays inclusive of that whole day.
            result = isBefore ? date.AddDays(1).AddTicks(-1) : date;
            return true;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result);
    }

    private sealed record QueryTerm(
        bool Negated,
        string? Key,
        string Value,
        bool Quoted,
        List<QueryTerm>? GroupTerms = null);
}
