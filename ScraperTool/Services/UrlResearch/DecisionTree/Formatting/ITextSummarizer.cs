namespace ScraperTool.Services.UrlResearch.DecisionTree.Formatting;

/// <summary>
/// Truncates long text to a bounded length with a clear truncation marker.
/// </summary>
public interface ITextSummarizer
{
    /// <summary>
    /// Returns the full content if it fits; otherwise returns a prefix ending with "...".
    /// </summary>
    /// <param name="content">The content to bound.</param>
    /// <param name="maxLength">The maximum length of the returned string.</param>
    /// <returns>A bounded representation of the content.</returns>
    string Summarize(string content, int maxLength);
}
