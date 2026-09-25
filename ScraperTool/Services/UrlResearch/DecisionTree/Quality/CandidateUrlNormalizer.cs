namespace ScraperTool.Services.UrlResearch.DecisionTree.Quality;

/// <summary>
/// Reduces a candidate URL to the key that identifies the page it addresses, so that one page
/// written two ways is queued, fetched and judged once.
/// <para>
/// Candidate strings arrive from search results, from <c>href</c> attributes and from page text,
/// so the same address reaches the queue as <c>/go</c> and <c>/go/</c>, in either letter case,
/// with or without a <c>#section</c> suffix. Comparing those strings verbatim left the queue
/// holding duplicates of one page and the visited-page record unable to recognise an address it
/// had stored itself.
/// </para>
/// </summary>
internal static class CandidateUrlNormalizer
{
    /// <summary>
    /// Returns a case-insensitive identity for the page <paramref name="url"/> addresses:
    /// scheme, host, path and query folded to lower case, fragment dropped, trailing slashes
    /// trimmed from the path. The query survives because it selects what a page returns. A
    /// string that is not an absolute HTTP URL is returned trimmed and lower-cased, which keeps
    /// other candidate entries comparable without pretending to understand them.
    /// </summary>
    internal static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return trimmed.ToLowerInvariant();
        }

        // Case is folded over the whole address, path included: the queue has never treated two
        // spellings that differ only by case as two pages, and a search snippet that upper-cases
        // a host or a link written as "/GO" is the same page the scan already queued.
        return string.Concat(
            uri.Scheme,
            "://",
            uri.Authority,
            uri.AbsolutePath.TrimEnd('/'),
            uri.Query).ToLowerInvariant();
    }
}
