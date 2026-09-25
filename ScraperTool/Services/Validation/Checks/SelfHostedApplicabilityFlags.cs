namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// What the self-hosted pre-pass settled about the three fields a locally hosted provider cannot
/// carry: a sign-in page, a subscription price list, and hosted API pricing.
/// <para>
/// A set flag means the field has been resolved as not applicable, so the URL loop leaves the stored
/// value alone instead of probing an address that cannot mean what it claims — and the matching
/// decision tree never sees a second finding for the same answer.
/// </para>
/// </summary>
public sealed record SelfHostedApplicabilityFlags(
    bool LoginUrlIsNotApplicable,
    bool SubscriptionPricingUrlIsNotApplicable,
    bool ApiPricingUrlIsNotApplicable);
