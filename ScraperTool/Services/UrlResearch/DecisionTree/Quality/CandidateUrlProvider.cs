using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Quality;

/// <summary>
/// Default implementation of <see cref="ICandidateUrlProvider"/>.
/// </summary>
public sealed class CandidateUrlProvider : ICandidateUrlProvider
{
    private static readonly HashSet<string> CandidateDataTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CandidateLink",
        "SearchResult"
    };

    public IReadOnlyList<string> GetCandidateUrls(DataStore data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var urls = new List<string>();

        foreach (var item in data.GetAll().OrderBy(d => d.CreatedAt))
        {
            if (item.Type is null || !CandidateDataTypes.Contains(item.Type))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Content))
            {
                continue;
            }

            // The address is restored to the page it points at before it is queued: an href
            // arrives with its HTML entities intact and a header link with its click attribution
            // attached, and whatever string the queue hands over is the string the tree goes on to
            // fetch, judge and propose storing.
            var url = CandidateUrlCleaner.Clean(item.Content);
            if (url.Length == 0)
            {
                continue;
            }

            // One page is one queue entry however many ways its address was written: a search
            // result and an in-page link to the same page otherwise gave the tree two visits,
            // and it spent its budget judging one page twice.
            if (seen.Add(CandidateUrlNormalizer.Normalize(url)))
            {
                urls.Add(url);
            }
        }

        return urls;
    }
}
