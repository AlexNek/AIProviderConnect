using System.Net;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Quality;

/// <summary>
/// Restores a candidate address to the page it points at: HTML entities decoded, click-attribution
/// query parameters dropped.
/// <para>
/// Candidates arrive from <c>href</c> attributes and from search results, so the queue holds
/// whatever the document contained. A header link written as
/// <c>?utm_source=docs&amp;amp;utm_medium=referral</c> reached the tree as that literal string —
/// the entity made it a malformed query, the tracking parameters made it a different address from
/// the page it decorated, and when the classifier judged the page a winner the tree proposed
/// storing the decorated address. Decoding the entities is what makes the address the one the
/// author wrote; dropping the attribution parameters is what makes it an address worth storing.
/// </para>
/// <para>
/// Nothing else about the address is touched. A surviving query is not re-encoded, the path is not
/// normalised and letter case is not folded — comparing two spellings of one page is
/// <see cref="CandidateUrlNormalizer"/>'s job, and it never publishes a value that gets written
/// into a provider definition.
/// </para>
/// </summary>
internal static class CandidateUrlCleaner
{
    /// <summary>
    /// Query parameter names that exist only to attribute a click. They select nothing about what
    /// a page returns, so an address carrying them points at the same page as one without them.
    /// </summary>
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid",
        "gclid",
        "msclkid",
        "yclid",
        "dclid",
        "igshid",
        "_ga"
    };

    /// <summary>
    /// Returns <paramref name="url"/> with HTML entities decoded and click-attribution query
    /// parameters removed. A query parameter that selects content survives, as does a fragment,
    /// and the surviving parts keep the spelling they arrived with. Blank input yields an empty
    /// string, matching what the candidate queue treats as "nothing to fetch".
    /// </summary>
    internal static string Clean(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var decoded = WebUtility.HtmlDecode(url.Trim());

        // The fragment is set aside first: a query never follows it, so searching for '?' in the
        // whole string would otherwise mistake a "#a?b" suffix for one.
        var fragmentStart = decoded.IndexOf('#');
        var fragment = fragmentStart >= 0 ? decoded[fragmentStart..] : string.Empty;
        var address = fragmentStart >= 0 ? decoded[..fragmentStart] : decoded;

        var queryStart = address.IndexOf('?');
        if (queryStart < 0)
            return address + fragment;

        var kept = new List<string>();
        foreach (var pair in address[(queryStart + 1)..].Split('&'))
        {
            if (pair.Length == 0)
                continue;

            var separator = pair.IndexOf('=');
            var name = separator >= 0 ? pair[..separator] : pair;
            if (IsTrackingParameter(name))
                continue;

            kept.Add(pair);
        }

        // Dropping the last attribution parameter has to take the now-empty query separator with
        // it: "https://console.example/?" is not the address a provider documents.
        var baseUrl = address[..queryStart];
        return kept.Count > 0
            ? baseUrl + "?" + string.Join("&", kept) + fragment
            : baseUrl + fragment;
    }

    private static bool IsTrackingParameter(string name) =>
        name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
        || TrackingParameters.Contains(name);
}
