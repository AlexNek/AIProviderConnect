using System.Text.Json;

using ScraperTool.Models;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// The completeness check behind <see cref="ISubscriptionConfiguredChecker"/>: a pure read of the
/// manifest, with no transport to hold.
/// </summary>
public sealed class SubscriptionConfiguredChecker : ISubscriptionConfiguredChecker
{
    /// <inheritdoc />
    public Task ValidateAsync(
        JsonElement root,
        string fileName,
        IValidationIssueSink sink,
        CancellationToken ct = default)
    {
        var category = root.TryGetProperty(ProviderJsonFields.Category, out var catProp)
                       && catProp.ValueKind == JsonValueKind.String
                           ? catProp.GetString()
                           : null;

        if (string.Equals(category, ProviderJsonFields.CategorySelfHosted, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        var subUrl =
            root.TryGetProperty(ProviderJsonFields.SubscriptionPricingUrl, out var subProp)
            && subProp.ValueKind == JsonValueKind.String
                ? subProp.GetString()
                : null;

        if (!string.IsNullOrWhiteSpace(subUrl))
            return Task.CompletedTask;

        var message =
            $"Field 'subscriptionPricingUrl' is not set. Set to {ProviderJsonFields.NotApplicable} if no subscription plans exist.";
        sink.Append(
            new ValidationIssue(
                fileName,
                ValidationIssueCodes.MissingSubscriptionPricingUrl,
                message)
            { Field = ProviderJsonFields.SubscriptionPricingUrl });
        sink.FailAppended(
            fileName,
            ProviderJsonFields.SubscriptionPricingUrl,
            "",
            "subscriptionPricingUrl not set");

        return Task.CompletedTask;
    }
}
