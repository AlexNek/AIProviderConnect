namespace ScraperTool.Services.UrlResearch.DecisionTree.Quality;

/// <summary>
/// Counts the model entries a page actually lists, without an LLM.
/// Used by the minModelCount tree so the suggested value is derived from the whole
/// fetched page rather than from a bounded excerpt or from a model's free-text reply.
/// </summary>
public interface IModelCatalogCounter
{
    /// <summary>
    /// Counts the distinct model entries linked from <paramref name="markdown"/>.
    /// Links are grouped by the URL path they share; the largest group whose path
    /// identifies a model collection wins.
    /// </summary>
    /// <param name="markdown">The full Markdown of the page.</param>
    /// <param name="pageUri">The URL the Markdown was fetched from, used to resolve relative links.</param>
    /// <returns>The count and the collection it came from, or null when the page lists no model collection.</returns>
    ModelCatalogCountResult? Count(string markdown, Uri? pageUri);
}
