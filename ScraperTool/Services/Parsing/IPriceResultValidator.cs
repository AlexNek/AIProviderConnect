using ScraperTool.Models;

namespace ScraperTool.Services.Parsing;

/// <summary>
/// Validates scraped price results for sanity — reasonable ranges, completeness, etc.
/// </summary>
public interface IPriceResultValidator
{
    /// <summary>
    /// Validates a list of scraped results. Returns only the valid ones.
    /// </summary>
    ValidationResult Validate(IReadOnlyList<ProviderPriceResult> results);
}
