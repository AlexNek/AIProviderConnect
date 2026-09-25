namespace ScraperTool.Services.UrlResearch.DecisionTree.Formatting;

/// <summary>
/// Default implementation of <see cref="ITextSummarizer"/>.
/// </summary>
public sealed class TextSummarizer : ITextSummarizer
{
    public string Summarize(string content, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (maxLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), "Maximum length must be greater than zero.");
        }

        if (content.Length <= maxLength)
        {
            return content;
        }

        const string Marker = "...";
        var keepLength = Math.Max(0, maxLength - Marker.Length);

        return keepLength == 0
            ? Marker
            : string.Concat(content.AsSpan(0, keepLength), Marker);
    }
}
