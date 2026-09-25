namespace ScraperTool.Services.UrlResearch.DecisionTree.Quality;

/// <summary>
/// Result of <see cref="ICandidateRegionContentSelector.Select"/>.
/// </summary>
/// <param name="Content">The selected content for LLM consumption.</param>
/// <param name="RegionCount">The total number of candidate regions found (0 if analysis was skipped or failed).</param>
public sealed record CandidateRegionContentResult(string Content, int RegionCount);
