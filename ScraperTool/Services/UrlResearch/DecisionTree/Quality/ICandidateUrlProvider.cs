using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Quality;

/// <summary>
/// Reads the candidate URL queue from the decision data store.
/// </summary>
public interface ICandidateUrlProvider
{
    /// <summary>
    /// Returns candidate URLs discovered by link scanning and web search,
    /// deduplicated and ordered by discovery time.
    /// </summary>
    /// <param name="data">The decision data store.</param>
    /// <returns>The ordered list of candidate URLs.</returns>
    IReadOnlyList<string> GetCandidateUrls(DataStore data);
}
