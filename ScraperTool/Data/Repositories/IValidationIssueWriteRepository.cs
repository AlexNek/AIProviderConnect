using ScraperTool.Data.Entities;

namespace ScraperTool.Data.Repositories;

/// <summary>
/// Write operations for validation issues.
/// </summary>
public interface IValidationIssueWriteRepository
{
    /// <summary>
    /// Deletes the specified validation issue entries.
    /// </summary>
    Task DeleteAsync(IReadOnlyList<ValidationIssueEntry> entries);

    /// <summary>
    /// Replaces all validation issues with the provided collection.
    /// </summary>
    Task ReplaceAllAsync(IEnumerable<ValidationIssueEntry> issues);

    /// <summary>
    /// Updates AI suggestion fields on existing entries.
    /// </summary>
    Task UpdateSuggestionsAsync(IReadOnlyList<ValidationIssueEntry> updates);
}
