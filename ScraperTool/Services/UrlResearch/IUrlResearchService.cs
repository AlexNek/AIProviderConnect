namespace ScraperTool.Services.UrlResearch;

/// <summary>
/// Researches a provider field to determine the correct URL value (or confirms that no valid URL exists).
/// </summary>
public interface IUrlResearchService
{
    Task<UrlResearchResult> ResearchAsync(ResearchContext context, CancellationToken ct = default);
}
