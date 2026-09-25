using System.Text.Json;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Asks whether the service behind a provider has been retired.
/// </summary>
public interface IServiceRetirementProbe
{
    /// <summary>
    /// Reads the provider's website for retirement signals and, when enough of them agree, files a
    /// <c>ServiceRetired</c> finding for every field in <paramref name="urlFields"/> — a retired
    /// service has no live address left for any of them.
    /// </summary>
    Task CheckServiceRetiredAsync(
        JsonElement root,
        string fileName,
        IValidationIssueSink sink,
        IReadOnlyList<string> urlFields,
        CancellationToken ct);
}
