using WebTools.NET.ContentAnalysis.Models;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Quality;

/// <summary>
/// Determines whether extracted HTML candidate regions contain meaningful content
/// or are dominated by UI chrome such as filter controls and parameter lists.
/// </summary>
public interface ICandidateRegionQualityAssessor
{
    /// <summary>
    /// Returns true when the supplied regions contain enough substantive text
    /// to be useful for LLM classification.
    /// </summary>
    /// <param name="regions">The candidate regions extracted from the HTML analyzer.</param>
    /// <returns>True if the regions are meaningful; otherwise false.</returns>
    bool HasMeaningfulContent(IReadOnlyList<HtmlCandidateRegion> regions);
}
