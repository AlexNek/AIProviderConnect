using System.Text.Json;

using ScraperTool.Models;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// The pre-pass that settles which fields a self-hosted provider cannot carry at all.
/// </summary>
public sealed class SelfHostedApplicabilityEvaluator : ISelfHostedApplicabilityEvaluator
{
    /// <inheritdoc />
    public SelfHostedApplicabilityFlags Evaluate(
        JsonElement root,
        string fileName,
        IValidationIssueSink sink)
    {
        // The three checks run in this order because that is the order their findings have always
        // appeared in: login page first, then subscription plans, then hosted API pricing.
        if (!IsSelfHosted(root))
            return new SelfHostedApplicabilityFlags(false, false, false);

        return new SelfHostedApplicabilityFlags(
            ValidateFieldApplicability(root, fileName, ProviderJsonFields.LoginUrl,
                loginUrl => $"Field 'loginUrl' must be {ProviderJsonFields.NotApplicable} — a self-hosted provider has no hosted account to sign in to: {loginUrl}",
                ValidationIssueCodes.LoginUrlNotApplicableForSelfHosted,
                $"self-hosted provider has no web login — set to {ProviderJsonFields.NotApplicable}",
                sink),
            ValidateFieldApplicability(root, fileName, ProviderJsonFields.SubscriptionPricingUrl,
                subscriptionUrl => $"Field 'subscriptionPricingUrl' must be {ProviderJsonFields.NotApplicable} — a self-hosted provider has no subscription plans to price: {subscriptionUrl}",
                ValidationIssueCodes.SubscriptionPricingUrlNotApplicableForSelfHosted,
                $"self-hosted provider has no subscription plans — set to {ProviderJsonFields.NotApplicable}",
                sink),
            ValidateFieldApplicability(root, fileName, ProviderJsonFields.ApiPricingUrl,
                apiPricingUrl => $"Field 'apiPricingUrl' must be {ProviderJsonFields.NotApplicable} — a self-hosted provider serves a local model with no hosted API pricing: {apiPricingUrl}",
                ValidationIssueCodes.ApiPricingUrlNotApplicableForSelfHosted,
                $"self-hosted provider has no hosted API pricing — set to {ProviderJsonFields.NotApplicable}",
                sink));
    }

    /// <summary>
    /// Checks whether a single field carries a real URL that a self-hosted provider cannot use.
    /// When the field is set to a non-empty, non-N/A value, the finding is emitted and the method
    /// returns true so the URL loop skips the field.
    /// </summary>
    private static bool ValidateFieldApplicability(
        JsonElement root,
        string fileName,
        string field,
        Func<string, string> messageFactory,
        string issueCode,
        string resultMessage,
        IValidationIssueSink sink)
    {
        if (!root.TryGetProperty(field, out var prop)
            || prop.ValueKind != JsonValueKind.String)
            return false;

        var fieldValue = prop.GetString();
        if (string.IsNullOrWhiteSpace(fieldValue)
            || fieldValue == ProviderJsonFields.NotApplicable)
            return false;

        sink.FailWith(
            fileName,
            field,
            fieldValue,
            issueCode,
            messageFactory(fieldValue),
            resultMessage);

        return true;
    }

    private static bool IsSelfHosted(JsonElement root) =>
        root.TryGetProperty(ProviderJsonFields.Category, out var catProp)
        && catProp.ValueKind == JsonValueKind.String
        && string.Equals(catProp.GetString(), ProviderJsonFields.CategorySelfHosted, StringComparison.OrdinalIgnoreCase);
}
