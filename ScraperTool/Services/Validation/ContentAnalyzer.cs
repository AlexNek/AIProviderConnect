using System.Text.RegularExpressions;

namespace ScraperTool.Services.Validation;

/// <summary>
/// Default implementation of <see cref="IContentAnalyzer"/>.
/// </summary>
public sealed class ContentAnalyzer : IContentAnalyzer
{
    private static readonly Regex[] NotFoundSignals =
        [
            new(
                @"(?i)page\s+(not\s+found|you\s+are\s+looking\s+for|could\s+not\s+be\s+found|does\s+not\s+exist|(may\s+)?have\s+been\s+(removed|moved|renamed|deleted))"),
            new(@"(?i)<title>[^<]*(404|not\s+found)[^<]*</title>"),
            new(@"(?i)(we\s+)?can'?t\s+seem\s+to\s+find"),
            new(@"(?i)something's?\s+missing"),
            new(
                @"(?i)this\s+page\s+(no\s+longer\s+exists|is\s+(unavailable|no\s+longer\s+available))"),
        ];

    /// <summary>
    /// A price the page itself displays. Words are not on this list: navigation labels and prose
    /// mention "pricing", "cost" and "model" on every page of an AI vendor, so a count of them
    /// certifies anything. An amount is the cheapest evidence that a pricing page is one.
    /// </summary>
    private static readonly Regex[] MonetaryAmountSignals =
        [
            new(@"[$\u20ac\u00a3\u00a5]\s*\d+(?:[.,]\d+)?", RegexOptions.IgnoreCase),
            new(@"\d+(?:[.,]\d+)?\s*(USD|EUR|GBP|CNY|RMB|JPY|INR|SGD|KRW)\b", RegexOptions.IgnoreCase),
            new(@"(per|/)\s*1\s*[MK]\b", RegexOptions.IgnoreCase),
            new(@"per\s*(million|thousand)\s*(tokens|characters|credits)", RegexOptions.IgnoreCase),
            new(@"\bper\s*token\b", RegexOptions.IgnoreCase),
        ];

    /// <summary>
    /// How much readable page text is needed before an absent price means something. Pricing tables
    /// that a page fetches after the render window was spent leave a body too short to hold one, and
    /// such a body cannot support a rejection either.
    /// </summary>
    private const int MinReadablePricingBodyChars = 3_000;

    private static readonly Regex[] SubscriptionSignals =
        [
            new(
                @"\b(plan|plans|tier|tiers|subscription|pro|enterprise)\b",
                RegexOptions.IgnoreCase),
            new(
                @"\b(monthly|annually|billed|per\s*month|per\s*year|per\s*seat)\b",
                RegexOptions.IgnoreCase),
            new(@"\b(team|business|premium|plus|ultimate|max)\b", RegexOptions.IgnoreCase),
            new(
                @"\b(free\s+tier|free\s+plan|hobby|starter|growth|scale)\b",
                RegexOptions.IgnoreCase),
        ];

    private static readonly Regex[] LoginSignals =
        [
            new(@"(?i)>\s*(log\s*in|sign\s*in)\s*<"),
            new(@"(?i)href\s*=\s*[""'][^""']*/(login|signin|sign-in)(?:\?|#|&|""|'|/|$)"),
            new(@"(?i)href\s*=\s*[""'][^""']*/auth(?:/|\?|#|""|'|$)"),
            new(@"(?i)(button|input)[^>]*(log\s*in|sign\s*in|login|signin)"),
        ];

    /// <summary>
    /// Proof that the page itself takes credentials, which a link to a sign-in page is not.
    /// </summary>
    private static readonly Regex[] CredentialFormSignals =
        [
            new(@"(?i)<input[^>]*type\s*=\s*[""']?password"),
            new(@"(?i)autocomplete\s*=\s*[""']current-password"),
        ];

    /// <summary>
    /// Address tokens that name an authentication endpoint, matched against the host labels,
    /// path segments and query values of the URL the redirects settled on.
    /// </summary>
    private static readonly string[] AuthUrlTokens =
        [
            "login", "signin", "sign-in", "log-in", "auth", "oauth", "authorize", "authenticate",
            "authentication", "sso", "identifier"
        ];

    public Task<bool> HasNotFoundContentAsync(string html)
    {
        var matchCount = NotFoundSignals.Count(rx => rx.IsMatch(html));
        return Task.FromResult(matchCount >= 2);
    }

    /// <summary>
    /// A page displays a price, so the stored address is the pricing page it claims to be.
    /// </summary>
    public Task<(EPricingContentVerdict Verdict, string Reason)> AnalyzeApiPricingContentAsync(
        string content)
    {
        var amount = FindMonetaryAmount(content);
        if (amount is not null)
            return Task.FromResult(
                (EPricingContentVerdict.HasPricing, $"amount displayed on the page ('{amount}')"));

        return Task.FromResult(
            JudgeAbsentPricing(content, "currency amount, per-token or per-1M rate"));
    }

    /// <summary>
    /// Tier vocabulary on its own is a marketing page, and an amount on its own can be a per-token
    /// rate list — the field is about recurring plans, so it needs both.
    /// </summary>
    public Task<(EPricingContentVerdict Verdict, string Reason)>
        AnalyzeSubscriptionPricingContentAsync(string content)
    {
        var amount = FindMonetaryAmount(content);
        var tiers = SubscriptionSignals.Count(rx => rx.IsMatch(content ?? string.Empty));

        if (amount is not null && tiers >= 1)
            return Task.FromResult(
                (EPricingContentVerdict.HasPricing,
                    $"priced plans displayed on the page ('{amount}', {tiers} tier signal(s))"));

        return Task.FromResult(
            JudgeAbsentPricing(content, "plan price (a tier, monthly or per-seat amount)"));
    }

    /// <summary>
    /// Splits "the page shows no price" from "the page was never read": a body shorter than a
    /// rendered table says nothing either way, and claiming it did is the defect this branch stops.
    /// </summary>
    private static (EPricingContentVerdict Verdict, string Reason) JudgeAbsentPricing(
        string content,
        string expectedEvidence)
    {
        var readable = content?.Trim().Length ?? 0;
        if (readable < MinReadablePricingBodyChars)
            return (
                EPricingContentVerdict.NotEvaluated,
                $"only {readable} characters of page text were readable — too little to hold a price");

        return (
            EPricingContentVerdict.NoPricing,
            $"no {expectedEvidence} appears anywhere in the page text");
    }

    /// <summary>
    /// Returns the first price the content displays, or null when it shows none.
    /// </summary>
    private static string? FindMonetaryAmount(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        foreach (var signal in MonetaryAmountSignals)
        {
            var match = signal.Match(content);
            if (match.Success)
                return match.Value.Trim();
        }

        return null;
    }

    public Task<bool> HasLoginContentAsync(string html)
    {
        var matchCount = LoginSignals.Count(rx => rx.IsMatch(html));
        return Task.FromResult(matchCount >= 2);
    }

    public Task<(ELoginUrlVerdict Verdict, string Reason)> AnalyzeLoginUrlAsync(
        string html,
        string finalUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
            return Task.FromResult((ELoginUrlVerdict.NotEvaluated, "the page returned no readable body"));

        if (CredentialFormSignals.Any(rx => rx.IsMatch(html)))
            return Task.FromResult((ELoginUrlVerdict.Confirmed, "credential form on the page"));

        var authToken = FindAuthUrlToken(finalUrl);
        if (authToken is not null)
            return Task.FromResult(
                (ELoginUrlVerdict.Confirmed, $"address names an authentication endpoint ('{authToken}')"));

        var affordances = LoginSignals.Count(rx => rx.IsMatch(html));
        if (affordances == 0)
            return Task.FromResult(
                (ELoginUrlVerdict.NotLoginPage,
                    "no credential form, no authentication endpoint in the address, no sign-in link"));

        // A page that links to signing in says nothing about whether it is where signing in
        // happens — consoles and code hosts all carry the link. Judging it needs the page's own
        // content read against the field purpose, which is the loginUrl tree's classification step.
        return Task.FromResult(
            (ELoginUrlVerdict.NotEvaluated,
                $"the page only links to a sign-in surface ({affordances} link(s))"));
    }

    /// <summary>
    /// Returns the first host label, path segment or query value that names an authentication
    /// endpoint, or null when the address carries no such token.
    /// </summary>
    private static string? FindAuthUrlToken(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        foreach (var token in Regex.Split(url.ToLowerInvariant(), @"[^a-z0-9-]+"))
        {
            if (token.Length == 0)
                continue;

            // Suffix as well as exact: "servicelogin", "unified-login" and "signin" all name the
            // same endpoint under the spellings providers actually use.
            if (AuthUrlTokens.Any(t => token == t || token.EndsWith(t, StringComparison.Ordinal)))
                return token;
        }

        return null;
    }
}
