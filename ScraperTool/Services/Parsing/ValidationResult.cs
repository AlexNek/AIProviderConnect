using ScraperTool.Models;

namespace ScraperTool.Services.Parsing;

/// <summary>
/// Result of price validation — separates valid results from suspect ones.
/// </summary>
public sealed class ValidationResult
{
    public IReadOnlyList<PriceValidationIssue> Issues { get; }

    public IReadOnlyList<ProviderPriceResult> Valid { get; }

    public ValidationResult(
        IReadOnlyList<ProviderPriceResult> valid,
        IReadOnlyList<PriceValidationIssue> issues)
    {
        Valid = valid;
        Issues = issues;
    }
}
