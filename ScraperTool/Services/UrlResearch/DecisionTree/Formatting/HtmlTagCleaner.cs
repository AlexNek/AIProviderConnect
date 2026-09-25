using System.Text.RegularExpressions;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Formatting;

/// <summary>
/// Compiled-regex implementation of <see cref="IHtmlTagCleaner"/>.
/// </summary>
public sealed partial class HtmlTagCleaner : IHtmlTagCleaner
{
    public string Clean(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withoutTags = HtmlTagRegex().Replace(html, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        var normalized = WhitespaceRegex().Replace(decoded, " ").Trim();

        return normalized;
    }

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
