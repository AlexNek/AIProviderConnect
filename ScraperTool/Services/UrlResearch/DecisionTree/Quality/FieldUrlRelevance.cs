namespace ScraperTool.Services.UrlResearch.DecisionTree.Quality;

/// <summary>
/// The field's address vocabulary: which words in a URL say what page it is, and how strongly.
/// <para>
/// Two callers need the same answer and used to compute it separately. The scan ranks the links it
/// found, so it needs a score for every candidate; the research service has to decide whether the
/// page a tree declared a winner is a worse address for the field than the one already stored, so
/// it needs the same vocabulary over two addresses. Both read it here.
/// </para>
/// <para>
/// The vocabulary comes from the field name plus the extra words that
/// <c>Config/field-definitions.json</c> declares for it, because providers name one destination in
/// many words: a sign-in link on <c>https://provider.example/go</c> can be <c>/auth</c> under the
/// anchor text "Subscribe", which matches nothing in "loginUrl".
/// </para>
/// </summary>
internal static class FieldUrlRelevance
{
    /// <summary>
    /// Splits a camel-case field name into lower-case words and adds the field's configured
    /// relevance vocabulary on top, dropping the words that describe a URL rather than a
    /// destination.
    /// </summary>
    internal static List<string> BuildKeywords(
        string fieldKind,
        IReadOnlyList<string> relevanceTerms)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var ch in fieldKind)
        {
            if (char.IsUpper(ch) && current.Length > 0)
            {
                words.Add(current.ToString().ToLowerInvariant());
                current.Clear();
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            words.Add(current.ToString().ToLowerInvariant());

        words.AddRange(relevanceTerms);

        // Remove generic words that don't help distinguish relevant links.
        return words
            .Where(w => w.Length > 2 && w != "url" && w != "page" && w != "link")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The part of an address a vocabulary can be read from: the path, lower-cased. A string that
    /// is not an absolute URL is used as it stands, which keeps a relative link comparable without
    /// pretending to understand it. The query and the host are deliberately not read — the query
    /// carries tracking decoration and the host says which service answered, not which page.
    /// </summary>
    internal static string GetPath(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath.ToLowerInvariant()
            : url.ToLowerInvariant();

    /// <summary>
    /// How many distinct vocabulary words appear in any of <paramref name="texts"/>. A word that
    /// appears twice counts once: the question is what the address says, not how often it says it.
    /// </summary>
    internal static int CountKeywordMatches(IReadOnlyList<string> keywords, params string[] texts) =>
        keywords.Count(keyword =>
            texts.Any(text => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Whether the address a tree declared a winner says nothing about the field while the stored
    /// address says something.
    /// <para>
    /// This is a weak test on purpose. A page's path is a hint about its content and never proof of
    /// it — <c>/docs/pricing</c> displays prices at several providers and <c>/login</c> is a
    /// marketing page at others — so the comparison only ever fires on the one shape that cannot be
    /// argued: the winner's address carries no word from the field's vocabulary at all, while the
    /// stored address carries one. Two addresses that both say something are left alone, however
    /// many words each says, because ranking them is the scan's business and deciding between them
    /// needs the page, not its name.
    /// </para>
    /// </summary>
    internal static bool WinnerSaysNothingWhileStoredDoes(
        string? winnerUrl,
        string? storedUrl,
        string fieldKind,
        IReadOnlyList<string> relevanceTerms)
    {
        if (string.IsNullOrWhiteSpace(winnerUrl) || string.IsNullOrWhiteSpace(storedUrl))
            return false;

        var keywords = BuildKeywords(fieldKind, relevanceTerms);
        if (keywords.Count == 0)
            return false;

        return CountKeywordMatches(keywords, GetPath(winnerUrl)) == 0
               && CountKeywordMatches(keywords, GetPath(storedUrl)) > 0;
    }
}
