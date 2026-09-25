using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;
using AiCleverness.Runtime.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Quality;

/// <summary>
/// Decision data policy for URL research that delegates to the default policy
/// but filters out navigation/link noise before classify nodes so the LLM focuses on page content.
/// </summary>
public sealed class UrlResearchDecisionDataPolicy : IDecisionDataPolicy
{
    private const string PageTextType = "PageText";
    private const string LastFetchedUrlKey = "lastFetchedUrl";

    private readonly IDecisionDataPolicy _defaultPolicy;

    public UrlResearchDecisionDataPolicy(DecisionDataPolicyOptions? options = null)
    {
        _defaultPolicy = new DefaultDecisionDataPolicy(options);
    }

    public DecisionDataSelection Select(IReadOnlyList<DecisionData> data, DecisionDataSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.ClassifyNode);

        var effectiveData = context.ClassifyNode.Type == EDecisionNodeType.Classify
            ? KeepCurrentPageOnly(FilterNavigationNoise(data), context.State)
            : data;

        return _defaultPolicy.Select(effectiveData, context);
    }

    private static IReadOnlyList<DecisionData> FilterNavigationNoise(IReadOnlyList<DecisionData> data)
    {
        return data
            .Where(item => !string.Equals(item.Type, "CandidateLink", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(item.Type, "SearchResult", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Keeps only the page content belonging to the candidate currently being classified.
    /// The default policy bounds prompt data oldest-first against an aggregate length
    /// budget, so once the first few fetches fill that budget every later page is omitted
    /// and the classifier keeps re-judging stale content — it then returns the same answer
    /// for every remaining candidate until the tree runs out of budget.
    /// </summary>
    private static IReadOnlyList<DecisionData> KeepCurrentPageOnly(
        IReadOnlyList<DecisionData> data,
        DecisionState state)
    {
        var pageIndexes = new List<int>();
        for (var i = 0; i < data.Count; i++)
        {
            if (string.Equals(data[i].Type, PageTextType, StringComparison.OrdinalIgnoreCase))
                pageIndexes.Add(i);
        }

        if (pageIndexes.Count <= 1)
            return data;

        var dropped = new HashSet<int>(pageIndexes);
        dropped.Remove(FindCurrentPageIndex(data, pageIndexes, state));

        var result = new List<DecisionData>(data.Count - dropped.Count);
        for (var i = 0; i < data.Count; i++)
        {
            if (!dropped.Contains(i))
                result.Add(data[i]);
        }

        return result;
    }

    /// <summary>
    /// Returns the position of the page fetched for the candidate under classification.
    /// Falls back to the most recent page when no source matches the recorded URL.
    /// </summary>
    private static int FindCurrentPageIndex(
        IReadOnlyList<DecisionData> data,
        IReadOnlyList<int> pageIndexes,
        DecisionState state)
    {
        if (state.Properties.TryGetValue(LastFetchedUrlKey, out var urlObj)
            && urlObj is string lastFetchedUrl
            && !string.IsNullOrWhiteSpace(lastFetchedUrl))
        {
            for (var i = pageIndexes.Count - 1; i >= 0; i--)
            {
                if (string.Equals(
                        data[pageIndexes[i]].Source,
                        lastFetchedUrl,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return pageIndexes[i];
                }
            }
        }

        return pageIndexes[^1];
    }
}
