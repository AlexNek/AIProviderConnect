namespace ScraperTool.Services.Validation;

/// <summary>
/// Analyzes fetched web page content for specific signals.
/// </summary>
public interface IContentAnalyzer
{
    /// <summary>
    /// Checks whether the HTML contains not-found / error page signals.
    /// </summary>
    Task<bool> HasNotFoundContentAsync(string html);

    /// <summary>
    /// Judges whether <paramref name="content"/> is the API pricing page it is stored as — a page
    /// that <em>displays</em> at least one price — rather than a page that only talks about pricing.
    /// </summary>
    Task<(EPricingContentVerdict Verdict, string Reason)> AnalyzeApiPricingContentAsync(string content);

    /// <summary>
    /// Judges whether <paramref name="content"/> shows subscription plans with prices (monthly or
    /// annual tiers, per-seat billing), as opposed to per-token API rates or tier vocabulary alone.
    /// </summary>
    Task<(EPricingContentVerdict Verdict, string Reason)> AnalyzeSubscriptionPricingContentAsync(
        string content);

    /// <summary>
    /// Checks whether the HTML contains login/sign-in signals
    /// (login buttons, sign-in links, auth forms, etc.).
    /// </summary>
    Task<bool> HasLoginContentAsync(string html);

    /// <summary>
    /// Judges whether <paramref name="html"/> is the authentication surface <paramref name="finalUrl"/>
    /// claims to be — a page that takes credentials or an address that names an authentication
    /// endpoint — rather than a page that merely links to one.
    /// </summary>
    Task<(ELoginUrlVerdict Verdict, string Reason)> AnalyzeLoginUrlAsync(string html, string finalUrl);
}
