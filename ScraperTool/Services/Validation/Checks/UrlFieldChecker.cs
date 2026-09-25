using System.Text.Json;

using ScraperTool.Models;
using ScraperTool.Services.Validation;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Default <see cref="IUrlFieldChecker"/>: runs the full per-field check — gates,
/// pricing branch, reachability branch with redirect caps, content probes,
/// field-specific judgments, HTTP status classification, and exception handlers.
/// </summary>
public sealed class UrlFieldChecker : IUrlFieldChecker
{
    private const int MaxLoginRedirects = 5;
    private const int MaxWebPageRedirects = 2;
    private static readonly TimeSpan UrlTimeout = TimeSpan.FromSeconds(12);

    private readonly IUrlReachabilityChecker _reachabilityChecker;
    private readonly IPageContentProbe _pageContentProbe;
    private readonly IPricingPageVerifier _pricingPageVerifier;
    private readonly IWebsiteOwnershipJudge _websiteOwnershipJudge;
    private readonly IApiEndpointProbe _apiEndpointProbe;
    private readonly IManualBrowserVerifier _manualVerifier;

    public UrlFieldChecker(
        IUrlReachabilityChecker reachabilityChecker,
        IPageContentProbe pageContentProbe,
        IPricingPageVerifier pricingPageVerifier,
        IWebsiteOwnershipJudge websiteOwnershipJudge,
        IApiEndpointProbe apiEndpointProbe,
        IManualBrowserVerifier manualVerifier)
    {
        _reachabilityChecker = reachabilityChecker ?? throw new ArgumentNullException(nameof(reachabilityChecker));
        _pageContentProbe = pageContentProbe ?? throw new ArgumentNullException(nameof(pageContentProbe));
        _pricingPageVerifier = pricingPageVerifier ?? throw new ArgumentNullException(nameof(pricingPageVerifier));
        _websiteOwnershipJudge = websiteOwnershipJudge ?? throw new ArgumentNullException(nameof(websiteOwnershipJudge));
        _apiEndpointProbe = apiEndpointProbe ?? throw new ArgumentNullException(nameof(apiEndpointProbe));
        _manualVerifier = manualVerifier ?? throw new ArgumentNullException(nameof(manualVerifier));
    }

    /// <inheritdoc />
    public async Task CheckFieldAsync(FieldCheckContext context)
    {
        var fileName = context.FileName;
        var field = context.Field;
        var url = context.Url;
        var uri = context.Uri;
        var root = context.Root;
        var sink = context.Sink;
        var ct = context.CancellationToken;
        var applicability = context.Applicability;

        // Checking it would report a finding straight over the not-applicable finding raised above.
        if (field == ProviderJsonFields.LoginUrl && applicability.LoginUrlIsNotApplicable) return;
        if (field == ProviderJsonFields.SubscriptionPricingUrl && applicability.SubscriptionPricingUrlIsNotApplicable) return;
        if (field == ProviderJsonFields.ApiPricingUrl && applicability.ApiPricingUrlIsNotApplicable) return;

        // useLocalProviders flag semantics:
        // - when true: allow local/private host reachability checks
        // - when false: silently skip local/private host reachability checks
        if (!context.UseLocalProviders && UrlDomainRules.IsPrivateUrl(uri))
            return;

        sink.Report(fileName, field, url, ValidationStage.CheckingUrl);

        try
        {
            if (field == ProviderJsonFields.ApiPricingUrl)
            {
                await CheckPricingFieldAsync(context, ProviderJsonFields.ApiPricingUrl,
                    verdict => $"API pricing page displays prices ({verdict.Reason})",
                    reason => $"apiPricingUrl could not be confirmed as a pricing page ({reason}) — stored value kept");
            }
            else if (field == ProviderJsonFields.SubscriptionPricingUrl)
            {
                await CheckPricingFieldAsync(context, ProviderJsonFields.SubscriptionPricingUrl,
                    verdict => $"subscription pricing page displays priced plans ({verdict.Reason})",
                    reason => $"subscriptionPricingUrl could not be confirmed as a priced plan page ({reason}) — stored value kept");
            }
            else
            {
                await CheckReachabilityFieldAsync(context);
            }
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            sink.FailWith(
                fileName, field, url,
                "UrlTimeout",
                $"Field '{field}' request timed out after {UrlTimeout.TotalSeconds}s: {url}",
                $"timed out after {UrlTimeout.TotalSeconds}s");
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                       and not TaskCanceledException)
        {
            Serilog.Log.Warning(ex, "URL request failed for {Url}", url);
            sink.FailWith(
                fileName, field, url,
                "UrlError",
                $"Field '{field}' request failed: {url}",
                "request failed");
        }
    }

    private async Task CheckPricingFieldAsync(
        FieldCheckContext context,
        string fieldName,
        Func<(EPricingContentVerdict Verdict, string Reason), string> passMessage,
        Func<string, string> fallbackMessage)
    {
        var before = context.Sink.Mark();
        var (verdict, reason) = await _pricingPageVerifier.VerifyPricingUrlAsync(
            context.Uri, context.Url, context.FileName, fieldName, context.Sink, context.CancellationToken);

        if (context.Sink.HasIssuesSince(before))
        {
            context.Sink.FailAppended(context.FileName, context.Field, context.Url);
        }
        else
        {
            var msg = verdict == EPricingContentVerdict.HasPricing
                ? passMessage((verdict, reason))
                : fallbackMessage(reason);
            context.Sink.Pass(context.FileName, context.Field, context.Url, msg);
        }
    }

    private async Task CheckReachabilityFieldAsync(FieldCheckContext context)
    {
        var status = await _reachabilityChecker.CheckAsync(context.Uri, context.CancellationToken);
        var httpStatus = status.HttpStatus ?? 0;

        if (status.Reachable)
        {
            await HandleReachableResponseAsync(context, status, httpStatus);
        }
        else if (httpStatus is 401 or 403)
        {
            await HandleAuthRequiredAsync(context, status, httpStatus);
        }
        else if (httpStatus == 429)
        {
            context.Sink.Pass(context.FileName, context.Field, context.Url,
                "HTTP 429 (rate-limited — URL exists)");
        }
        else if (httpStatus == 404)
        {
            await HandleNotFoundAsync(context, httpStatus);
        }
        else
        {
            context.Sink.FailWith(context.FileName, context.Field, context.Url,
                "UrlNotReachable",
                $"Field '{context.Field}' returned HTTP {httpStatus}: {context.Url}",
                $"HTTP {httpStatus}");
        }
    }

    private async Task HandleReachableResponseAsync(
        FieldCheckContext context,
        UrlCheckResult status,
        int httpStatus)
    {
        var fileName = context.FileName;
        var field = context.Field;
        var url = context.Url;
        var uri = context.Uri;
        var sink = context.Sink;
        var ct = context.CancellationToken;
        var loginUrlIsNotApplicable = context.Applicability.LoginUrlIsNotApplicable;

        if (field == ProviderJsonFields.Website
            || field == ProviderJsonFields.LoginUrl
            || field == ProviderJsonFields.DocumentationUrl)
        {
            // Login URLs are expected to redirect through auth flows
            // (e.g. Google ServiceLogin → signin/identifier → consent).
            // Allow more redirects for login pages than for regular web pages.
            var maxRedirects = field == ProviderJsonFields.LoginUrl
                                   ? MaxLoginRedirects
                                   : MaxWebPageRedirects;

            if (status.RedirectCount > maxRedirects)
            {
                sink.FailWith(fileName, field, url,
                    "UrlNotFound",
                    $"Field '{field}' went through {status.RedirectCount} redirects to '{status.FinalUrl}': {url}",
                    $"{status.RedirectCount} redirects — final: {status.FinalUrl}");
                return;
            }

            var before = sink.Mark();
            var readResult = await _pageContentProbe.ReadBodyForErrorPageAsync(
                url, fileName, field, sink, ct);
            context.PageHtml = readResult.Body;
            if (sink.HasIssuesSince(before))
            {
                sink.FailAppended(fileName, field, url);
                return;
            }

            // A 'website' that names a page on a subdomain of a domain the provider itself runs
            // is a section of that site — not the public homepage the field asks for.
            if (field == ProviderJsonFields.Website)
            {
                if (await _websiteOwnershipJudge.TrySettleWebsiteRootAsync(
                        uri, context.Root, fileName, field, url, sink, ct)) return;

                // Verify loginUrl when it equals the website URL.
                if (await TryCheckLoginUrlEqualityAsync(context, loginUrlIsNotApplicable)) return;
            }

            // loginUrl: the field is judged on whether it offers an authentication surface.
            // The probe already settled the login verdict in the same call (pass-through fold).
            if (field == ProviderJsonFields.LoginUrl)
            {
                var loginVerdict = readResult.LoginVerdict;
                var loginReason = readResult.LoginReason;

                if (loginVerdict == ELoginUrlVerdict.NotLoginPage)
                {
                    sink.FailWith(fileName, field, url,
                        ValidationIssueCodes.LoginUrlNotLoginPage,
                        $"Field 'loginUrl' is not an authentication surface ({loginReason}): {url}",
                        loginReason);
                    return;
                }

                sink.Pass(fileName, field, url,
                    loginVerdict == ELoginUrlVerdict.Confirmed
                        ? $"login surface confirmed ({loginReason})"
                        : $"loginUrl could not be confirmed as a sign-in surface ({loginReason}) — stored value kept");
                return;
            }
        }

        // baseUrl: verify it's an actual API endpoint, not a web page.
        if (field == ProviderJsonFields.BaseUrl && httpStatus >= 200 && httpStatus < 300)
        {
            var (verdict, apiReason) = await _apiEndpointProbe.ProbeIsApiEndpointAsync(url, ct);

            if (verdict == EApiProbeVerdict.NotApi)
            {
                sink.FailWith(fileName, field, url,
                    ValidationIssueCodes.BaseUrlNotApiEndpoint,
                    $"Field 'baseUrl' {apiReason} — not an API endpoint: {url}",
                    $"baseUrl {apiReason} — not an API endpoint");
                return;
            }

            if (verdict == EApiProbeVerdict.NotEvaluated)
            {
                sink.Pass(fileName, field, url,
                    $"baseUrl could not be evaluated ({apiReason}) — stored value kept");
                return;
            }
        }

        var msg = httpStatus >= 200 && httpStatus < 300
                      ? "URL is reachable"
                      : "Server reachable (JSON error response)";
        sink.Pass(fileName, field, url, msg);
    }

    private async Task<bool> TryCheckLoginUrlEqualityAsync(
        FieldCheckContext context,
        bool loginUrlIsNotApplicable)
    {
        if (loginUrlIsNotApplicable
            || !context.Root.TryGetProperty(ProviderJsonFields.LoginUrl, out var loginProp)
            || loginProp.ValueKind != JsonValueKind.String)
            return false;

        var loginUrl = loginProp.GetString()!;
        if (string.IsNullOrWhiteSpace(loginUrl)
            || loginUrl == ProviderJsonFields.NotApplicable
            || !string.Equals(loginUrl, context.Url, StringComparison.OrdinalIgnoreCase))
            return false;

        return await _pageContentProbe.TrySettleLoginUrlEqualityAsync(
            loginUrl, context.FileName, context.Sink, context.CancellationToken);
    }

    private async Task HandleAuthRequiredAsync(
        FieldCheckContext context,
        UrlCheckResult status,
        int httpStatus)
    {
        if (status.ProtectionType is not null)
        {
            try
            {
                var result = await _manualVerifier.TryVerifyAsync(
                    context.Url, context.Field, context.FileName, context.CancellationToken);

                if (result == EManualVerificationResult.Verified)
                {
                    // Manual verification is authoritative — skip headless content probes
                    // that would fail against the same bot protection the user just solved.
                    context.Sink.Pass(context.FileName, context.Field, context.Url,
                        "Manually verified — page content confirmed");
                    return;
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Manual browser verification failed for {Url}", context.Url);
            }

            // StillProtected, UserCancelled, or exception — fall back to BotProtected pass.
            var msg = $"HTTP {httpStatus} — site uses {status.ProtectionType} bot protection";
            context.Sink.Append(context.FileName, context.Field, context.Url,
                ValidationIssueCodes.BotProtected,
                $"Field '{context.Field}' returned HTTP {httpStatus} ({status.ProtectionType} protection detected) — URL likely correct but can't be verified: {context.Url}");
            context.Sink.Pass(context.FileName, context.Field, context.Url, msg);
        }
        else if (context.Field == ProviderJsonFields.Website)
        {
            var msg = $"HTTP {httpStatus} — public website must be accessible without authentication";
            context.Sink.FailWith(context.FileName, context.Field, context.Url,
                "UrlNotReachable",
                $"Field 'website' returned HTTP {httpStatus} — public websites must be accessible without authentication: {context.Url}",
                msg);
        }
        else
        {
            var msg = $"requires authentication (HTTP {httpStatus})";
            context.Sink.FailWith(context.FileName, context.Field, context.Url,
                "UrlRequiresAuth",
                $"Field '{context.Field}' requires authentication (HTTP {httpStatus}) — skipped: {context.Url}",
                msg);
        }
    }

    private async Task HandleNotFoundAsync(FieldCheckContext context, int httpStatus)
    {
        if (context.Field == ProviderJsonFields.BaseUrl)
        {
            if (await _apiEndpointProbe.TryModelsEndpointAsync(
                    context.Url, context.Root, context.CancellationToken))
            {
                context.Sink.Pass(context.FileName, context.Field, context.Url,
                    $"HTTP {httpStatus} (models endpoint reachable)");
                return;
            }
        }

        context.Sink.FailWith(context.FileName, context.Field, context.Url,
            "UrlNotFound",
            $"Field '{context.Field}' returned 404 Not Found: {context.Url}",
            "404 Not Found");
    }
}
