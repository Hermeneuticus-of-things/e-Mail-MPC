namespace EMailMPC.Core.Search;

/// <summary>
/// Result ordering for local searches. <see cref="DateNewestFirst"/> is the
/// historical default; <see cref="Relevance"/> orders full-text matches by their
/// FTS5 bm25 score (lower is a better match) and falls back to the date ordering
/// whenever the query has no full-text component.
/// </summary>
public enum SearchSort
{
    DateNewestFirst = 0,
    Relevance = 1,
}
