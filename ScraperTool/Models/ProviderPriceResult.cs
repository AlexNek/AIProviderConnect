using System.Text.RegularExpressions;

namespace ScraperTool.Models;

public sealed partial class ProviderPriceResult
{
    private string _modelDisplayName = string.Empty;

    public string ApiPricingUrl { get; set; } = string.Empty;

    public decimal? CompletionPrice { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string ModelDisplayName
    {
        get => _modelDisplayName;
        set => _modelDisplayName = CollapseWhitespace(value ?? string.Empty);
    }

    public string ModelId { get; set; } = string.Empty;

    public EPriceUnit PriceUnit { get; set; } = EPriceUnit.Per1M;

    public decimal? PromptPrice { get; set; }

    public string ProviderId { get; set; } = string.Empty;

    public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;

    public string? Source { get; set; }

    public string Website { get; set; } = string.Empty;

    private static string CollapseWhitespace(string s) =>
        string.IsNullOrWhiteSpace(s) ? string.Empty : WhitespaceRegex().Replace(s.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
