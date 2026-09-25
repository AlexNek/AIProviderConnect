using System.Text.Json;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Requires a hosted provider to say something about its subscription plans.
/// </summary>
public interface ISubscriptionConfiguredChecker
{
    /// <summary>
    /// Files <c>MissingSubscriptionPricingUrl</c> for a provider that is not self-hosted and leaves
    /// the field empty, pointing at the not-applicable marker that answers the question instead.
    /// </summary>
    Task ValidateAsync(
        JsonElement root,
        string fileName,
        IValidationIssueSink sink,
        CancellationToken ct = default);
}
