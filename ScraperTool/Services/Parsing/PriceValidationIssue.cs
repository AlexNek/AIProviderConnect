using ScraperTool.Models;

namespace ScraperTool.Services.Parsing;

/// <summary>
/// Describes why a scraped price entry was flagged as suspect.
/// </summary>
public sealed class PriceValidationIssue
{
    public required ProviderPriceResult Entry { get; init; }

    public required string Reason { get; init; }

    public PriceIssueSeverity Severity { get; init; } = PriceIssueSeverity.Warning;
}
