namespace ScraperTool.Services;

/// <summary>
/// Constants for validation issue codes.
/// Eliminates magic strings throughout the validation system.
/// </summary>
public static class ValidationIssueCodes
{
    public const string ApiPricingUrlNotApplicableForSelfHosted = "ApiPricingUrlNotApplicableForSelfHosted";

    public const string BaseUrlNotApiEndpoint = "BaseUrlNotApiEndpoint";

    public const string BotProtected = "BotProtected";

    public const string DeserializationError = "DeserializationError";

    public const string DeserializationNull = "DeserializationNull";

    public const string DuplicateId = "DuplicateId";

    public const string EmptyField = "EmptyField";

    public const string InvalidProtocol = "InvalidProtocol";

    public const string InvalidUrl = "InvalidUrl";

    public const string LoginUrlNotApplicableForSelfHosted = "LoginUrlNotApplicableForSelfHosted";

    public const string LoginUrlNotLoginPage = "LoginUrlNotLoginPage";

    public const string LoginUrlSameAsWebsite = "LoginUrlSameAsWebsite";

    public const string MinModelCountInvalid = "MinModelCountInvalid";

    public const string MissingDiscoveryEndpoint = "MissingDiscoveryEndpoint";

    // Structural issues
    public const string MissingRequired = "MissingRequired";

    public const string MissingSubscriptionPricingUrl = "MissingSubscriptionPricingUrl";

    public const string PricingContentInvalid = "PricingContentInvalid";

    public const string PricingUrlRedirected = "PricingUrlRedirected";

    public const string ServiceRetired = "ServiceRetired";

    public const string SubscriptionPricingUrlNotApplicableForSelfHosted = "SubscriptionPricingUrlNotApplicableForSelfHosted";

    public const string UrlError = "UrlError";

    public const string UrlLocalHost = "UrlLocalHost";

    // URL-related issues
    public const string UrlNotFound = "UrlNotFound";

    public const string UrlNotReachable = "UrlNotReachable";

    public const string UrlRequiresAuth = "UrlRequiresAuth";

    public const string UrlTimeout = "UrlTimeout";

    public const string WebsiteIsSubdomain = "WebsiteIsSubdomain";

    /// <summary>
    /// Array of all URL-related error codes that indicate a genuinely broken URL
    /// needing AI repair (404, timeout, unreachable, etc.).
    /// UrlRequiresAuth is included because a protected URL may still be the wrong URL
    /// for this provider, and needs AI validation (e.g., is this the correct login page?).
    /// WebsiteIsSubdomain is included because a page on the provider's own subdomain is a wrong
    /// 'website' value, and where that provider's public homepage actually is is exactly what
    /// Config/trees/website.json researches — the validator only establishes that this page is not it.
    /// LoginUrlNotApplicableForSelfHosted is included because the loginUrl tree answers a
    /// self-hosted provider with its not-applicable verdict, which is the repair.
    /// SubscriptionPricingUrlNotApplicableForSelfHosted is included because the subscriptionPricingUrl
    /// tree answers a self-hosted provider with its not-applicable verdict, which is the repair.
    /// ApiPricingUrlNotApplicableForSelfHosted is included because the apiPricingUrl tree answers a
    /// self-hosted provider with its not-applicable verdict, which is the repair.
    /// LoginUrlNotLoginPage is included because finding the real authentication entry point
    /// is exactly what the loginUrl tree's page classification is for.
    /// </summary>
    public static readonly string[] UrlErrorCodes =
        {
            UrlNotFound, UrlNotReachable, UrlTimeout, UrlError, UrlRequiresAuth,
            PricingContentInvalid, PricingUrlRedirected, MissingSubscriptionPricingUrl,
            SubscriptionPricingUrlNotApplicableForSelfHosted, ApiPricingUrlNotApplicableForSelfHosted,
            ServiceRetired, LoginUrlSameAsWebsite, BaseUrlNotApiEndpoint,
            LoginUrlNotApplicableForSelfHosted, LoginUrlNotLoginPage, WebsiteIsSubdomain
        };

    /// <summary>
    /// URLs where the issue is purely informational — the validator already knows
    /// the correct value and wrote it into the issue message.
    /// BotProtected sites are correct URLs that can't be verified — skip AI fix.
    /// </summary>
    public static readonly string[] UrlInfoCodes = { BotProtected };

    /// <summary>
    /// Array of all structural (non-URL) error codes.
    /// </summary>
    public static readonly string[] StructuralErrorCodes =
        {
            MissingRequired, EmptyField, InvalidProtocol, InvalidUrl, MissingDiscoveryEndpoint,
            DeserializationError, DeserializationNull, DuplicateId, MinModelCountInvalid,
            MissingSubscriptionPricingUrl
        };
}
