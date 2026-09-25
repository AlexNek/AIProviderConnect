using ScraperTool.Models;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Default <see cref="IPricingPageVerifier"/>: fetches pricing pages via
/// <see cref="IWebContentFetcher"/> and analyses them with <see cref="IContentAnalyzer"/>.
/// </summary>
public sealed class PricingPageVerifier : IPricingPageVerifier
{
    /// <summary>
    /// Phrases that unambiguously indicate a service has been retired or discontinued.
    /// At least two distinct phrases must appear to trigger a positive detection.
    /// </summary>
    private static readonly string[] RetirementSignalPhrases =
    [
        "has been retired",
        "has been discontinued",
        "has been sunset",
        "no longer available",
        "service has ended",
        "end of life",
        "no longer supported",
        "service is no longer",
        "been fully retired",
        "shutting down"
    ];

    private readonly IWebContentFetcher _fetcher;
    private readonly IContentAnalyzer _contentAnalyzer;

    public PricingPageVerifier(IWebContentFetcher fetcher, IContentAnalyzer contentAnalyzer)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _contentAnalyzer = contentAnalyzer ?? throw new ArgumentNullException(nameof(contentAnalyzer));
    }

    /// <inheritdoc />
    public async Task<(EPricingContentVerdict Verdict, string Reason)> VerifyPricingUrlAsync(
        Uri uri,
        string url,
        string fileName,
        string fieldName,
        IValidationIssueSink sink,
        CancellationToken ct = default)
    {
        // Sanitized Markdown, not the PlainText default: page text is read through
        // body.textContent, which keeps every inline <script> body, and a React/Next payload is
        // full of "$1", "$2" handle references — enough to satisfy a currency pattern on a page
        // that displays no price at all. Strict sanitization drops script/style/nav/footer/header,
        // so the navigation "Pricing" link stops counting as evidence too.
        var result = await _fetcher.FetchAsAsync(
            url,
            EContentFormat.Markdown,
            ct: ct);

        if (!result.Success)
        {
            var errorMsg = result.ErrorMessage ?? "unknown error";
            if (errorMsg.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                sink.Append(
                    fileName,
                    fieldName,
                    url,
                    "UrlTimeout",
                    $"Field '{fieldName}' timed out: {url}");
            }
            else if (errorMsg.Contains("403") || errorMsg.Contains("401"))
            {
                var code = errorMsg.Contains("403") ? 403 : 401;
                sink.Append(
                    fileName,
                    fieldName,
                    url,
                    "UrlRequiresAuth",
                    $"Field '{fieldName}' requires authentication (HTTP {code}) — skipped: {url}");
            }
            else if (errorMsg.Contains("404"))
            {
                sink.Append(
                    fileName,
                    fieldName,
                    url,
                    "UrlNotFound",
                    $"Field '{fieldName}' returned 404 Not Found: {url}");
            }
            else
            {
                sink.Append(
                    fileName,
                    fieldName,
                    url,
                    "UrlError",
                    $"Field '{fieldName}' request failed: {url}");
            }

            return (EPricingContentVerdict.NotEvaluated, errorMsg);
        }

        sink.Progress(fileName, fieldName, url, ValidationStage.CheckingPricingContent);

        // Detect client-side redirects: if the browser landed on a different URL,
        // the page JS-redirected — the original URL doesn't show the expected content.
        // Trivial trailing-slash differences are ignored (/pricing ≡ /pricing/)
        // because most servers normalise URLs that way without changing content.
        if (!string.IsNullOrWhiteSpace(result.FinalUrl)
            && !string.Equals(
                result.FinalUrl.TrimEnd('/'),
                url.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase))
        {
            sink.Append(
                new ValidationIssue(
                    fileName,
                    ValidationIssueCodes.PricingUrlRedirected,
                    $"Field '{fieldName}' URL redirected from '{url}' to '{result.FinalUrl}' — original URL does not show expected content")
                { Field = fieldName, CurrentValue = url, RedirectTargetUrl = result.FinalUrl });
            return (EPricingContentVerdict.NotEvaluated, $"the page redirected to {result.FinalUrl}");
        }

        // Detect retirement/deprecation notices: a page that announces the service has been
        // retired or discontinued is not a pricing page — it has no pricing content by definition.
        // Emitting ServiceRetired routes the field through the AI fix pipeline which suggests "-".
        if (TryDetectRetirement(result.Content, out var signalSummary))
        {
            sink.Append(
                new ValidationIssue(
                    fileName,
                    ValidationIssueCodes.ServiceRetired,
                    $"Field '{fieldName}' page indicates service retirement ({signalSummary}): {url}")
                { Field = fieldName, CurrentValue = url });
            return (EPricingContentVerdict.NotEvaluated, $"page indicates service retirement ({signalSummary})");
        }

        // Use field-specific content validation:
        // - apiPricingUrl: an amount the page displays (per-token, per-1M, $)
        // - subscriptionPricingUrl: priced plans (tiers, monthly or per-seat amounts)
        var (verdict, reason) = fieldName == ProviderJsonFields.SubscriptionPricingUrl
            ? await _contentAnalyzer.AnalyzeSubscriptionPricingContentAsync(result.Content)
            : await _contentAnalyzer.AnalyzeApiPricingContentAsync(result.Content);

        if (verdict == EPricingContentVerdict.NoPricing)
        {
            var expectedContent = fieldName == ProviderJsonFields.SubscriptionPricingUrl
                ? "subscription/plan content (tiers, monthly pricing, etc.)"
                : "API pricing content (per-token, per-1M, cost, etc.)";
            sink.Append(
                fileName,
                fieldName,
                url,
                ValidationIssueCodes.PricingContentInvalid,
                $"Field '{fieldName}' returned HTTP 200 but page contains no {expectedContent} — {reason}: {url}");
        }

        return (verdict, reason);
    }

    /// <summary>
    /// Checks whether the page content contains enough retirement/deprecation phrases to
    /// conclude the service is no longer active. Requires at least two distinct signal phrases
    /// to reduce false positives, matching the threshold used by <c>ServiceRetirementProbe</c>.
    /// </summary>
    private static bool TryDetectRetirement(string content, out string signalSummary)
    {
        signalSummary = string.Empty;

        if (string.IsNullOrWhiteSpace(content))
            return false;

        var contentLower = content.ToLowerInvariant();
        var matchedSignals = RetirementSignalPhrases
            .Where(phrase => contentLower.Contains(phrase))
            .ToList();

        if (matchedSignals.Count < 2)
            return false;

        signalSummary = string.Join(", ", matchedSignals.Select(s => $"\"{s}\""));
        return true;
    }
}
