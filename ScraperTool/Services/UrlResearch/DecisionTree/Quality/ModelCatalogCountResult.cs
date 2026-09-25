namespace ScraperTool.Services.UrlResearch.DecisionTree.Quality;

/// <summary>
/// Result of <see cref="IModelCatalogCounter.Count"/>.
/// </summary>
/// <param name="Count">Number of distinct model entries linked from the page.</param>
/// <param name="CollectionPath">The URL path the model entries share (e.g. <c>/api/docs/models</c>).</param>
/// <param name="SampleModelNames">A few entry names, for evidence and diagnostics.</param>
public sealed record ModelCatalogCountResult(
    int Count,
    string CollectionPath,
    IReadOnlyList<string> SampleModelNames);
