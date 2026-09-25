namespace ScraperTool.Services.UrlResearch.DecisionTree.Formatting;

/// <summary>
/// Removes HTML markup from text and normalizes whitespace so downstream stages receive readable strings.
/// </summary>
public interface IHtmlTagCleaner
{
    /// <summary>
    /// Strips HTML tags, decodes HTML entities, and collapses whitespace.
    /// </summary>
    /// <param name="html">The input text that may contain HTML markup.</param>
    /// <returns>A cleaned, human-readable string.</returns>
    string Clean(string html);
}
