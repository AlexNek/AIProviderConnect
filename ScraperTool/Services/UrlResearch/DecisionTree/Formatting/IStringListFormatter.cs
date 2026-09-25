namespace ScraperTool.Services.UrlResearch.DecisionTree.Formatting;

/// <summary>
/// Formats a list of strings into a bounded, human-readable summary suitable for LLM state.
/// </summary>
public interface IStringListFormatter
{
    /// <summary>
    /// Produces a summary such as "5 items: a, b, ..., y, z".
    /// </summary>
    /// <param name="items">The strings to summarize.</param>
    /// <param name="maxItems">Maximum number of individual items to include in the rendered summary.</param>
    /// <param name="maxItemLength">Maximum length of each included item.</param>
    /// <returns>A bounded summary string.</returns>
    string FormatSummary(IReadOnlyList<string> items, int maxItems, int maxItemLength);
}
