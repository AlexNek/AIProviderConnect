using System.Globalization;
using System.Net;
using System.Text;

using FluentAssertions;

using Moq;

using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.Services.Validation;
using ScraperTool.Services.Validation.Checks;

using WebTools.NET.Abstractions;

namespace ScraperTool.Tests.Validation;

/// <summary>
/// Behavior freeze for <see cref="ProviderDefinitionValidator"/> taken before refactor 20 starts
/// moving its code. Each test walks one manifest through the validator and compares a complete
/// transcript — every progress report in the order it was reported, every sidecar write, every
/// outbound request in the order it was made, and the issues that came back — against a golden that
/// was captured from the code as it stood before the extraction. The transcripts say nothing about
/// the target design: they exist so that a moved block can be proven to still do exactly this.
/// </summary>
public class ProviderDefinitionValidatorCharacterizationTests
{
    private const string ManifestFileName = "fixture.json";

    private const string NotStored = "";

    private const string SelfHostedManifest = """
        {
          "id": "fixture",
          "displayName": "Fixture",
          "protocol": "OpenAICompatible",
          "category": "SelfHosted",
          "website": "http://localhost:8080/ui",
          "loginUrl": "https://account.example.com/login",
          "apiPricingUrl": "-",
          "subscriptionPricingUrl": "https://plans.example.com/subscribe",
          "documentationUrl": "not a uri"
        }
        """;

    private const string SubdomainWebsiteManifest = """
        {
          "id": "fixture",
          "displayName": "Example Labs",
          "protocol": "OpenAICompatible",
          "website": "https://console.example.com",
          "loginUrl": "https://console.example.com/login",
          "apiPricingUrl": "https://console.example.com/pricing",
          "subscriptionPricingUrl": "https://console.example.com/plans",
          "documentationUrl": "https://console.example.com/docs",
          "baseUrl": "https://api.example.com/v1"
        }
        """;

    private const string LoginUrlEqualsWebsiteManifest = """
        {
          "id": "fixture",
          "displayName": "Fixture",
          "protocol": "OpenAICompatible",
          "website": "https://example.com",
          "loginUrl": "https://example.com",
          "apiPricingUrl": "-",
          "subscriptionPricingUrl": "-",
          "documentationUrl": "https://example.com/docs",
          "baseUrl": "http://localhost:1234/v1"
        }
        """;

    private const string PricingManifest = """
        {
          "id": "fixture",
          "displayName": "Fixture",
          "protocol": "OpenAICompatible",
          "website": "https://example.com",
          "loginUrl": "-",
          "apiPricingUrl": "https://example.com/pricing",
          "subscriptionPricingUrl": "https://example.com/plans",
          "documentationUrl": "https://example.com/docs",
          "baseUrl": "https://api.example.com/v1"
        }
        """;

    private const string MissingSubscriptionManifest = """
        {
          "id": "fixture",
          "displayName": "Fixture",
          "protocol": "OpenAICompatible",
          "website": "https://example.com",
          "loginUrl": "-",
          "apiPricingUrl": "-",
          "documentationUrl": "https://status.example.com/rate-limits",
          "baseUrl": "https://api.example.com/v1",
          "modelsEndpoint": "models"
        }
        """;

    private const string ProtectedAndFailingManifest = """
        {
          "id": "fixture",
          "displayName": "Fixture",
          "protocol": "OpenAICompatible",
          "website": "https://example.com",
          "loginUrl": "https://account.example.net/login",
          "apiPricingUrl": "https://example.com/pricing",
          "subscriptionPricingUrl": "https://example.com/plans",
          "documentationUrl": "https://docs.example.org/api",
          "baseUrl": "https://api.example.com/v1"
        }
        """;

    private const string PageLevelFailuresManifest = """
        {
          "id": "fixture",
          "displayName": "Fixture",
          "protocol": "OpenAICompatible",
          "website": "https://example.com",
          "loginUrl": "https://signin.example.net",
          "apiPricingUrl": "https://example.com/pricing",
          "subscriptionPricingUrl": "https://example.com/plans",
          "documentationUrl": "https://old.example.org/docs",
          "baseUrl": "https://api.example.com/v1"
        }
        """;

    private const string PrivateBaseUrlManifest = """
        {
          "id": "fixture",
          "displayName": "Fixture",
          "protocol": "OpenAICompatible",
          "website": "https://example.com",
          "loginUrl": "-",
          "apiPricingUrl": "-",
          "subscriptionPricingUrl": "-",
          "documentationUrl": "-",
          "baseUrl": "http://localhost:1234/v1"
        }
        """;

    private const string SubdomainWithoutOtherFieldsManifest = """
        {
          "id": "fixture",
          "displayName": "Example Labs",
          "protocol": "OpenAICompatible",
          "website": "https://console.example.com",
          "loginUrl": "-",
          "apiPricingUrl": "-",
          "subscriptionPricingUrl": "-",
          "documentationUrl": "-",
          "baseUrl": "http://localhost:1234/v1"
        }
        """;

    private const string SubdomainWithLoginManifest = """
        {
          "id": "fixture",
          "displayName": "Example Labs",
          "protocol": "OpenAICompatible",
          "website": "https://console.example.com",
          "loginUrl": "https://console.example.com/login",
          "apiPricingUrl": "-",
          "subscriptionPricingUrl": "-",
          "documentationUrl": "-",
          "baseUrl": "http://localhost:1234/v1"
        }
        """;

    private const string TransportFailureManifest = """
        {
          "id": "fixture",
          "displayName": "Fixture",
          "protocol": "OpenAICompatible",
          "website": "https://example.com",
          "loginUrl": "-",
          "apiPricingUrl": "-",
          "subscriptionPricingUrl": "-",
          "documentationUrl": "https://docs.example.org/api",
          "baseUrl": "http://localhost:1234/v1"
        }
        """;

    // No subscriptionPricingUrl at all, so the post-loop completeness check has something to say and
    // the sidecar path can be frozen with the finding it produces.
    private const string SubscriptionNotStoredManifest = """
        {
          "id": "fixture",
          "displayName": "Fixture",
          "protocol": "OpenAICompatible",
          "website": "https://example.com",
          "loginUrl": "-",
          "apiPricingUrl": "-",
          "documentationUrl": "-",
          "baseUrl": "http://localhost:1234/v1"
        }
        """;

    [Fact]
    public async Task SelfHostedProvider_FilesNotApplicableFindingsWithoutProbingAnything()
    {
        var fixture = new Fixture(SelfHostedManifest);

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS
            loginUrl|failed|1|https://account.example.com/login|self-hosted provider has no web login — set to -|LoginUrlNotApplicableForSelfHosted/loginUrl
            subscriptionPricingUrl|failed|2|https://plans.example.com/subscribe|self-hosted provider has no subscription plans — set to -|SubscriptionPricingUrlNotApplicableForSelfHosted/subscriptionPricingUrl
            apiPricingUrl|passed|2|-|N/A (self-hosted)|-
            CALLS
            read:none
            delete
            save:count=2,by=auto,force=True
            REQUESTS
            markdown:http://localhost:8080/ui
            RETURNED
            LoginUrlNotApplicableForSelfHosted|loginUrl|https://account.example.com/login
            SubscriptionPricingUrlNotApplicableForSelfHosted|subscriptionPricingUrl|https://plans.example.com/subscribe
            """);
    }

    [Fact]
    public async Task WebsiteOnProviderSubdomain_FilesSubdomainFindingBeforeLoginComparison()
    {
        var fixture = new Fixture(SubdomainWebsiteManifest);
        fixture.HtmlPages["https://example.com"] = new StubPage("Welcome to Example");

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS
            website|checking-url|0|https://console.example.com||-
            website|checking-content|0|https://console.example.com||-
            website|failed|1|https://console.example.com|subdomain of the provider's own site — the root domain 'example.com' names the provider ('Example')|WebsiteIsSubdomain/website
            loginUrl|checking-url|1|https://console.example.com/login||-
            loginUrl|checking-content|0|https://console.example.com/login||-
            loginUrl|passed|1|https://console.example.com/login|login surface confirmed (credential form on the page)|-
            apiPricingUrl|checking-url|1|https://console.example.com/pricing||-
            apiPricingUrl|checking-pricing|0|https://console.example.com/pricing||-
            apiPricingUrl|passed|1|https://console.example.com/pricing|API pricing page displays prices (amount displayed ('$9'))|-
            subscriptionPricingUrl|checking-url|1|https://console.example.com/plans||-
            subscriptionPricingUrl|checking-pricing|0|https://console.example.com/plans||-
            subscriptionPricingUrl|passed|1|https://console.example.com/plans|subscription pricing page displays priced plans (priced plans displayed ('$9', 1 tier))|-
            documentationUrl|checking-url|1|https://console.example.com/docs||-
            documentationUrl|checking-content|0|https://console.example.com/docs||-
            documentationUrl|passed|1|https://console.example.com/docs|URL is reachable|-
            baseUrl|checking-url|1|https://api.example.com/v1||-
            baseUrl|passed|1|https://api.example.com/v1|baseUrl could not be evaluated (answers 404 on its root) — stored value kept|-
            CALLS
            read:none
            delete
            save:count=1,by=auto,force=True
            REQUESTS
            reachability:https://console.example.com/
            fetch:https://console.example.com
            root-reachability:https://example.com
            fetch:https://example.com
            reachability:https://console.example.com/login
            fetch:https://console.example.com/login
            markdown:https://console.example.com/pricing
            markdown:https://console.example.com/plans
            reachability:https://console.example.com/docs
            fetch:https://console.example.com/docs
            reachability:https://api.example.com/v1
            probe:GET https://api.example.com/v1
            markdown:https://console.example.com
            RETURNED
            WebsiteIsSubdomain|website|https://console.example.com
            """);
    }

    [Fact]
    public async Task WebsiteThatAlsoServesAsLoginUrl_FilesSameAsWebsiteFinding()
    {
        var fixture = new Fixture(LoginUrlEqualsWebsiteManifest);
        fixture.HtmlPages["https://example.com"] = new StubPage("Product marketing page.");

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS
            website|checking-url|0|https://example.com||-
            website|checking-content|0|https://example.com||-
            loginUrl|failed|1|https://example.com|loginUrl equals website — no login/sign-in elements found on page|LoginUrlSameAsWebsite/loginUrl
            loginUrl|checking-url|1|https://example.com||-
            loginUrl|checking-content|0|https://example.com||-
            loginUrl|passed|1|https://example.com|login surface confirmed (credential form on the page)|-
            apiPricingUrl|passed|1|-|N/A (self-hosted)|-
            subscriptionPricingUrl|passed|1|-|N/A (self-hosted)|-
            documentationUrl|checking-url|1|https://example.com/docs||-
            documentationUrl|checking-content|0|https://example.com/docs||-
            documentationUrl|passed|1|https://example.com/docs|URL is reachable|-
            CALLS
            read:none
            delete
            save:count=1,by=auto,force=True
            REQUESTS
            reachability:https://example.com/
            fetch:https://example.com
            fetch:https://example.com
            reachability:https://example.com/
            fetch:https://example.com
            reachability:https://example.com/docs
            fetch:https://example.com/docs
            markdown:https://example.com
            RETURNED
            LoginUrlSameAsWebsite|loginUrl|https://example.com
            """);
    }

    [Fact]
    public async Task WebsiteThatOffersSignIn_PassesTheSameAsWebsiteCheck()
    {
        var fixture = new Fixture(LoginUrlEqualsWebsiteManifest);
        fixture.HtmlPages["https://example.com"] = new StubPage("Sign in to continue.");

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS
            website|checking-url|0|https://example.com||-
            website|checking-content|0|https://example.com||-
            loginUrl|passed|0|https://example.com|loginUrl equals website — page has login elements|-
            website|passed|0|https://example.com|URL is reachable|-
            loginUrl|checking-url|0|https://example.com||-
            loginUrl|checking-content|0|https://example.com||-
            loginUrl|passed|0|https://example.com|login surface confirmed (credential form on the page)|-
            apiPricingUrl|passed|0|-|N/A (self-hosted)|-
            subscriptionPricingUrl|passed|0|-|N/A (self-hosted)|-
            documentationUrl|checking-url|0|https://example.com/docs||-
            documentationUrl|checking-content|0|https://example.com/docs||-
            documentationUrl|passed|0|https://example.com/docs|URL is reachable|-
            CALLS
            read:none
            delete
            save:count=0,by=auto,force=True
            REQUESTS
            reachability:https://example.com/
            fetch:https://example.com
            fetch:https://example.com
            reachability:https://example.com/
            fetch:https://example.com
            reachability:https://example.com/docs
            fetch:https://example.com/docs
            markdown:https://example.com
            RETURNED
            """);
    }

    [Fact]
    public async Task PricingPages_ReportTheirOwnVerdicts()
    {
        var fixture = new Fixture(PricingManifest);
        fixture.MarkdownPages["https://example.com/pricing"] = new StubPage(
            "Models and rates.",
            FinalUrl: "https://example.com/pricing/");
        fixture.ApiPricing = (EPricingContentVerdict.NoPricing, "no amount displayed");
        fixture.ProbeReplies["https://api.example.com/v1"] = new ProbeReply(
            HttpStatusCode.OK,
            "application/json",
            """{"data":[]}""");

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS
            website|checking-url|0|https://example.com||-
            website|checking-content|0|https://example.com||-
            website|passed|0|https://example.com|URL is reachable|-
            loginUrl|passed|0|-|N/A (self-hosted)|-
            apiPricingUrl|checking-url|0|https://example.com/pricing||-
            apiPricingUrl|checking-pricing|0|https://example.com/pricing||-
            apiPricingUrl|failed|1|https://example.com/pricing|Field 'apiPricingUrl' returned HTTP 200 but page contains no API pricing content (per-token, per-1M, cost, etc.) — no amount displayed: https://example.com/pricing|PricingContentInvalid/apiPricingUrl
            subscriptionPricingUrl|checking-url|1|https://example.com/plans||-
            subscriptionPricingUrl|checking-pricing|0|https://example.com/plans||-
            subscriptionPricingUrl|passed|1|https://example.com/plans|subscription pricing page displays priced plans (priced plans displayed ('$9', 1 tier))|-
            documentationUrl|checking-url|1|https://example.com/docs||-
            documentationUrl|checking-content|0|https://example.com/docs||-
            documentationUrl|passed|1|https://example.com/docs|URL is reachable|-
            baseUrl|checking-url|1|https://api.example.com/v1||-
            baseUrl|passed|1|https://api.example.com/v1|URL is reachable|-
            CALLS
            read:none
            delete
            save:count=1,by=auto,force=True
            REQUESTS
            reachability:https://example.com/
            fetch:https://example.com
            markdown:https://example.com/pricing
            markdown:https://example.com/plans
            reachability:https://example.com/docs
            fetch:https://example.com/docs
            reachability:https://api.example.com/v1
            probe:GET https://api.example.com/v1
            markdown:https://example.com
            RETURNED
            PricingContentInvalid|apiPricingUrl|https://example.com/pricing
            """);
    }

    [Fact]
    public async Task BaseUrlNotFoundWithReachableModelsEndpoint_PassesAndReportsMissingSubscriptionPricing()
    {
        var fixture = new Fixture(MissingSubscriptionManifest);
        fixture.Reachability["https://status.example.com/rate-limits"] =
            new WebTools.NET.Models.UrlCheckResult(false, 429, "rate limited");
        fixture.Reachability["https://api.example.com/v1"] =
            new WebTools.NET.Models.UrlCheckResult(false, 404, "not found");
        fixture.Reachability["https://api.example.com/v1/models"] =
            new WebTools.NET.Models.UrlCheckResult(true, 200, null);

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS
            website|checking-url|0|https://example.com||-
            website|checking-content|0|https://example.com||-
            website|passed|0|https://example.com|URL is reachable|-
            loginUrl|passed|0|-|N/A (self-hosted)|-
            apiPricingUrl|passed|0|-|N/A (self-hosted)|-
            documentationUrl|checking-url|0|https://status.example.com/rate-limits||-
            documentationUrl|passed|0|https://status.example.com/rate-limits|HTTP 429 (rate-limited — URL exists)|-
            baseUrl|checking-url|0|https://api.example.com/v1||-
            baseUrl|passed|0|https://api.example.com/v1|HTTP 404 (models endpoint reachable)|-
            subscriptionPricingUrl|failed|1||subscriptionPricingUrl not set|MissingSubscriptionPricingUrl/subscriptionPricingUrl
            CALLS
            read:none
            delete
            save:count=1,by=auto,force=True
            REQUESTS
            reachability:https://example.com/
            fetch:https://example.com
            reachability:https://status.example.com/rate-limits
            reachability:https://api.example.com/v1
            reachability:https://api.example.com/v1/models
            markdown:https://example.com
            RETURNED
            MissingSubscriptionPricingUrl|subscriptionPricingUrl|
            """);
    }

    [Fact]
    public async Task ProtectedAndFailingAddresses_FileTheirOwnFindings()
    {
        var fixture = new Fixture(ProtectedAndFailingManifest);
        fixture.Reachability["https://example.com"] = new WebTools.NET.Models.UrlCheckResult(
            false, 403, "forbidden", ProtectionType: "Cloudflare");
        fixture.Reachability["https://account.example.net/login"] = new WebTools.NET.Models.UrlCheckResult(
            false, 401, "unauthorized");
        fixture.Reachability["https://docs.example.org/api"] = new WebTools.NET.Models.UrlCheckResult(
            false, 500, "server error");
        fixture.MarkdownPages["https://example.com/pricing"] = new StubPage(
            Success: false,
            Error: "HTTP 404 Not Found");
        fixture.MarkdownPages["https://example.com/plans"] = new StubPage(
            Success: false,
            Error: "The operation timed out.");
        fixture.ProbeReplies["https://api.example.com/v1"] = new ProbeReply(
            HttpStatusCode.OK,
            "text/html",
            "<html><body>pricing</body></html>");

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS
            website|checking-url|0|https://example.com||-
            website|passed|1|https://example.com|HTTP 403 — site uses Cloudflare bot protection|-
            loginUrl|checking-url|1|https://account.example.net/login||-
            loginUrl|failed|2|https://account.example.net/login|requires authentication (HTTP 401)|UrlRequiresAuth/loginUrl
            apiPricingUrl|checking-url|2|https://example.com/pricing||-
            apiPricingUrl|failed|3|https://example.com/pricing|Field 'apiPricingUrl' returned 404 Not Found: https://example.com/pricing|UrlNotFound/apiPricingUrl
            subscriptionPricingUrl|checking-url|3|https://example.com/plans||-
            subscriptionPricingUrl|failed|4|https://example.com/plans|Field 'subscriptionPricingUrl' timed out: https://example.com/plans|UrlTimeout/subscriptionPricingUrl
            documentationUrl|checking-url|4|https://docs.example.org/api||-
            documentationUrl|failed|5|https://docs.example.org/api|HTTP 500|UrlNotReachable/documentationUrl
            baseUrl|checking-url|5|https://api.example.com/v1||-
            baseUrl|failed|6|https://api.example.com/v1|baseUrl returns an HTML page — not an API endpoint|BaseUrlNotApiEndpoint/baseUrl
            CALLS
            read:none
            delete
            save:count=6,by=auto,force=True
            REQUESTS
            reachability:https://example.com/
            reachability:https://account.example.net/login
            markdown:https://example.com/pricing
            markdown:https://example.com/plans
            reachability:https://docs.example.org/api
            reachability:https://api.example.com/v1
            probe:GET https://api.example.com/v1
            markdown:https://example.com
            RETURNED
            BotProtected|website|https://example.com
            UrlRequiresAuth|loginUrl|https://account.example.net/login
            UrlNotFound|apiPricingUrl|https://example.com/pricing
            UrlTimeout|subscriptionPricingUrl|https://example.com/plans
            UrlNotReachable|documentationUrl|https://docs.example.org/api
            BaseUrlNotApiEndpoint|baseUrl|https://api.example.com/v1
            """);
    }

    [Fact]
    public async Task PageLevelFailures_FileFindingsForEachBrokenAddress()
    {
        var fixture = new Fixture(PageLevelFailuresManifest);
        fixture.HtmlPages["https://example.com"] = new StubPage("<div>notfound</div>");
        fixture.LoginUrl = (ELoginUrlVerdict.NotLoginPage, "no credential form on the page");
        fixture.MarkdownPages["https://example.com/pricing"] = new StubPage(
            "Plans.",
            FinalUrl: "https://www.example.com/plans");
        fixture.SubscriptionPricing = (EPricingContentVerdict.NotEvaluated, "the page could not be read");
        fixture.Reachability["https://old.example.org/docs"] = new WebTools.NET.Models.UrlCheckResult(
            true, 200, null, RedirectCount: 5);
        fixture.Reachability["https://api.example.com/v1"] =
            new WebTools.NET.Models.UrlCheckResult(false, 404, "not found");

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS
            website|checking-url|0|https://example.com||-
            website|checking-content|0|https://example.com||-
            website|failed|1|https://example.com|Field 'website' returned HTTP 200 but page shows error/not-found content: https://example.com|UrlNotFound/website
            loginUrl|checking-url|1|https://signin.example.net||-
            loginUrl|checking-content|0|https://signin.example.net||-
            loginUrl|failed|2|https://signin.example.net|no credential form on the page|LoginUrlNotLoginPage/loginUrl
            apiPricingUrl|checking-url|2|https://example.com/pricing||-
            apiPricingUrl|checking-pricing|0|https://example.com/pricing||-
            apiPricingUrl|failed|3|https://example.com/pricing|Field 'apiPricingUrl' URL redirected from 'https://example.com/pricing' to 'https://www.example.com/plans' — original URL does not show expected content|PricingUrlRedirected/apiPricingUrl
            subscriptionPricingUrl|checking-url|3|https://example.com/plans||-
            subscriptionPricingUrl|checking-pricing|0|https://example.com/plans||-
            subscriptionPricingUrl|passed|3|https://example.com/plans|subscriptionPricingUrl could not be confirmed as a priced plan page (the page could not be read) — stored value kept|-
            documentationUrl|checking-url|3|https://old.example.org/docs||-
            documentationUrl|failed|4|https://old.example.org/docs|5 redirects — final: |UrlNotFound/documentationUrl
            baseUrl|checking-url|4|https://api.example.com/v1||-
            baseUrl|failed|5|https://api.example.com/v1|404 Not Found|UrlNotFound/baseUrl
            CALLS
            read:none
            delete
            save:count=5,by=auto,force=True
            REQUESTS
            reachability:https://example.com/
            fetch:https://example.com
            reachability:https://signin.example.net/
            fetch:https://signin.example.net
            markdown:https://example.com/pricing
            markdown:https://example.com/plans
            reachability:https://old.example.org/docs
            reachability:https://api.example.com/v1
            markdown:https://example.com
            RETURNED
            UrlNotFound|website|https://example.com
            LoginUrlNotLoginPage|loginUrl|https://signin.example.net
            PricingUrlRedirected|apiPricingUrl|https://example.com/pricing
            UrlNotFound|documentationUrl|https://old.example.org/docs
            UrlNotFound|baseUrl|https://api.example.com/v1
            """);
    }

    [Fact]
    public async Task UnreachableWebsiteRoot_ReportsNothingAboutTheRoot()
    {
        var fixture = new Fixture(SubdomainWithoutOtherFieldsManifest);
        fixture.RootReachability["https://example.com"] = false;

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS
            website|checking-url|0|https://console.example.com||-
            website|checking-content|0|https://console.example.com||-
            website|passed|0|https://console.example.com|URL is reachable|-
            loginUrl|passed|0|-|N/A (self-hosted)|-
            apiPricingUrl|passed|0|-|N/A (self-hosted)|-
            subscriptionPricingUrl|passed|0|-|N/A (self-hosted)|-
            documentationUrl|passed|0|-|N/A (self-hosted)|-
            CALLS
            read:none
            delete
            save:count=0,by=auto,force=True
            REQUESTS
            reachability:https://console.example.com/
            fetch:https://console.example.com
            root-reachability:https://example.com
            markdown:https://console.example.com
            RETURNED
            """);
    }

    [Fact]
    public async Task ForeignWebsiteRoot_KeepsTheStoredSubdomain()
    {
        var fixture = new Fixture(SubdomainWithLoginManifest);
        fixture.HtmlPages["https://example.com"] = new StubPage("Somebody else entirely.");

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS
            website|checking-url|0|https://console.example.com||-
            website|checking-content|0|https://console.example.com||-
            website|passed|0|https://console.example.com|root domain 'example.com' does not name this provider — the subdomain is the service's own site|-
            website|passed|0|https://console.example.com|URL is reachable|-
            loginUrl|checking-url|0|https://console.example.com/login||-
            loginUrl|checking-content|0|https://console.example.com/login||-
            loginUrl|passed|0|https://console.example.com/login|login surface confirmed (credential form on the page)|-
            apiPricingUrl|passed|0|-|N/A (self-hosted)|-
            subscriptionPricingUrl|passed|0|-|N/A (self-hosted)|-
            documentationUrl|passed|0|-|N/A (self-hosted)|-
            CALLS
            read:none
            delete
            save:count=0,by=auto,force=True
            REQUESTS
            reachability:https://console.example.com/
            fetch:https://console.example.com
            root-reachability:https://example.com
            fetch:https://example.com
            reachability:https://console.example.com/login
            fetch:https://console.example.com/login
            markdown:https://console.example.com
            RETURNED
            """);
    }

    [Fact]
    public async Task TransportFailures_FileTimeoutAndErrorFindings()
    {
        var fixture = new Fixture(TransportFailureManifest);
        fixture.ReachabilityFailures["https://example.com"] = new TaskCanceledException("timeout");
        fixture.ReachabilityFailures["https://docs.example.org/api"] =
            new HttpRequestException("Internal server error");

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS
            website|checking-url|0|https://example.com||-
            website|failed|1|https://example.com|timed out after 12s|UrlTimeout/website
            loginUrl|passed|1|-|N/A (self-hosted)|-
            apiPricingUrl|passed|1|-|N/A (self-hosted)|-
            subscriptionPricingUrl|passed|1|-|N/A (self-hosted)|-
            documentationUrl|checking-url|1|https://docs.example.org/api||-
            documentationUrl|failed|2|https://docs.example.org/api|request failed|UrlError/documentationUrl
            CALLS
            read:none
            delete
            save:count=2,by=auto,force=True
            REQUESTS
            reachability:https://example.com/
            reachability:https://docs.example.org/api
            markdown:https://example.com
            RETURNED
            UrlTimeout|website|https://example.com
            UrlError|documentationUrl|https://docs.example.org/api
            """);
    }

    [Fact]
    public async Task RetiredService_FlagsEveryUrlField()
    {
        var fixture = new Fixture(PrivateBaseUrlManifest);
        fixture.MarkdownPages["https://example.com"] = new StubPage(
            "This service has been retired and the models are no longer available.");

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS
            website|checking-url|0|https://example.com||-
            website|checking-content|0|https://example.com||-
            website|passed|0|https://example.com|URL is reachable|-
            loginUrl|passed|0|-|N/A (self-hosted)|-
            apiPricingUrl|passed|0|-|N/A (self-hosted)|-
            subscriptionPricingUrl|passed|0|-|N/A (self-hosted)|-
            documentationUrl|passed|0|-|N/A (self-hosted)|-
            website|failed|0|https://example.com|Service retired (signals: "has been retired", "no longer available")|-
            website|failed|6|https://example.com|Service retired — 6 field(s) flagged for AI fix|-
            CALLS
            read:none
            delete
            save:count=6,by=auto,force=True
            REQUESTS
            reachability:https://example.com/
            fetch:https://example.com
            markdown:https://example.com
            RETURNED
            ServiceRetired|website|https://example.com
            ServiceRetired|loginUrl|-
            ServiceRetired|apiPricingUrl|-
            ServiceRetired|subscriptionPricingUrl|-
            ServiceRetired|documentationUrl|-
            ServiceRetired|baseUrl|http://localhost:1234/v1
            """);
    }

    [Fact]
    public async Task ManuallyValidatedSidecar_SkipsTheWorkAndPersistsNothing()
    {
        var fixture = new Fixture(SubscriptionNotStoredManifest)
        {
            Sidecar = new ValidationMetadata
            {
                ProviderId = "fixture",
                Level = ValidationLevel.ManuallyValidated,
                ValidatedBy = "manual",
                LastValidatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS
            sidecar|passed|0|<temp>\fixture.json|Skipped (manually validated)|-
            subscriptionPricingUrl|failed|1||subscriptionPricingUrl not set|MissingSubscriptionPricingUrl/subscriptionPricingUrl
            CALLS
            read:level=ManuallyValidated,by=manual
            REQUESTS
            RETURNED
            MissingSubscriptionPricingUrl|subscriptionPricingUrl|
            """);
    }

    [Fact]
    public async Task ExpiredManualSidecar_RevalidatesAndPersists()
    {
        var fixture = new Fixture(SubscriptionNotStoredManifest)
        {
            RevalidationDays = 3,
            Sidecar = new ValidationMetadata
            {
                ProviderId = "fixture",
                Level = ValidationLevel.ManuallyValidated,
                ValidatedBy = "auto",
                LastValidatedAt = DateTime.UtcNow - TimeSpan.FromDays(10) - TimeSpan.FromHours(1)
            }
        };

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS
            sidecar|checking-url|0|<temp>\fixture.json|Manual validation expired (10d > 3d) — re-validating|-
            website|checking-url|0|https://example.com||-
            website|checking-content|0|https://example.com||-
            website|passed|0|https://example.com|URL is reachable|-
            loginUrl|passed|0|-|N/A (self-hosted)|-
            apiPricingUrl|passed|0|-|N/A (self-hosted)|-
            documentationUrl|passed|0|-|N/A (self-hosted)|-
            subscriptionPricingUrl|failed|1||subscriptionPricingUrl not set|MissingSubscriptionPricingUrl/subscriptionPricingUrl
            CALLS
            read:level=ManuallyValidated,by=auto
            delete
            save:count=1,by=auto,force=True
            REQUESTS
            reachability:https://example.com/
            fetch:https://example.com
            markdown:https://example.com
            RETURNED
            MissingSubscriptionPricingUrl|subscriptionPricingUrl|
            """);
    }

    private static async Task AssertTranscriptAsync(Fixture fixture, string golden)
    {
        var actual = await fixture.RunAsync();

        // Line endings come from the test file for the golden and from Environment.NewLine for the
        // transcript, so neither is part of what is being frozen.
        actual.Should().Be(NormalizeLineEndings(golden), "the validator transcript is frozen");
    }

    private static string NormalizeLineEndings(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>
    /// One page exactly as the fetcher hands it over: what it says, where the browser ended up, and
    /// whether it could be read at all.
    /// </summary>
    private sealed record StubPage(
        string Content = "A page.",
        string? FinalUrl = null,
        bool Success = true,
        string? Error = null);

    /// <summary>
    /// One reply of the API probe, addressed by the URL it was asked about.
    /// </summary>
    private sealed record ProbeReply(HttpStatusCode Status, string MediaType, string Body);

    /// <summary>
    /// Answers every probe with the reply the fixture registered for its address, and records the
    /// request so that the transcript shows how often the validator reached the transport. Nothing
    /// here may leave the process, so a fixture that forgot to register a reply gets a plain 404
    /// instead of a live request.
    /// </summary>
    private sealed class StubProbeHandler(Fixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(fixture.AnswerProbe(request.Method.Method, request.RequestUri!.ToString()));
    }

    /// <summary>
    /// Arranges one manifest against fakes that answer every address from a table, runs
    /// <see cref="ProviderDefinitionValidator.ValidateFileAsync"/>, and prints everything the
    /// validator did as one ordered transcript.
    /// </summary>
    private sealed class Fixture(string manifest)
    {
        private const string NotFoundMarker = "notfound";

        private const string LoginMarker = "Sign in";

        private readonly List<string> _calls = [];
        private readonly List<string> _progress = [];
        private readonly List<string> _requests = [];

        public Dictionary<string, StubPage> HtmlPages { get; } = [];

        public Dictionary<string, StubPage> MarkdownPages { get; } = [];

        public Dictionary<string, WebTools.NET.Models.UrlCheckResult> Reachability { get; } = [];

        public Dictionary<string, bool> RootReachability { get; } = [];

        public Dictionary<string, Exception> ReachabilityFailures { get; } = [];

        public Dictionary<string, ProbeReply> ProbeReplies { get; } = [];

        public ValidationMetadata? Sidecar { get; init; }

        public int RevalidationDays { get; init; }

        public bool PersistValidation { get; init; } = true;

        public (EPricingContentVerdict Verdict, string Reason) ApiPricing { get; set; } =
            (EPricingContentVerdict.HasPricing, "amount displayed ('$9')");

        public (EPricingContentVerdict Verdict, string Reason) SubscriptionPricing { get; set; } =
            (EPricingContentVerdict.HasPricing, "priced plans displayed ('$9', 1 tier)");

        public (ELoginUrlVerdict Verdict, string Reason) LoginUrl { get; set; } =
            (ELoginUrlVerdict.Confirmed, "credential form on the page");

        public async Task<string> RunAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "refactor20-freeze", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, ManifestFileName);
            File.WriteAllText(path, manifest);

            try
            {
                var fetcher = BuildFetcher();
                var analyzer = BuildAnalyzer();
                var urlChecker = BuildUrlChecker();
                var httpClientFactory = new Mock<IHttpClientFactory>();
                httpClientFactory.Setup(f => f.CreateClient(HttpConstants.ScraperHttpClientName))
                    .Returns(new HttpClient(new StubProbeHandler(this), disposeHandler: false));

                var fieldChecker = ValidatorGraphBuilder.BuildFieldChecker(
                    urlChecker,
                    fetcher,
                    analyzer,
                    httpClientFactory.Object);

                var validator = new ProviderDefinitionValidator(
                    BuildSchemaValidator(),
                    BuildDuplicateIdChecker(),
                    BuildMetadataService(),
                    new ProviderManifestReader(),
                    new SelfHostedApplicabilityEvaluator(),
                    new SubscriptionConfiguredChecker(),
                    new ServiceRetirementProbe(fetcher),
                    fieldChecker);

                var progress = new RecordingProgress(_progress);

                var issues = await validator.ValidateFileAsync(
                    path,
                    revalidationDays: RevalidationDays,
                    progress: progress,
                    persistValidation: PersistValidation,
                    ct: CancellationToken.None);

                // The throwaway directory carries a fresh Guid and this machine's temp path, so it is
                // the one part of the transcript that is not today's behaviour.
                return Render(issues).Replace(directory, "<temp>").Replace("\r\n", "\n", StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        public HttpResponseMessage AnswerProbe(string method, string url)
        {
            _requests.Add($"probe:{method} {url}");

            var reply = Served(ProbeReplies, url) ?? new ProbeReply(
                HttpStatusCode.NotFound,
                "text/html",
                "<html><body>nothing</body></html>");

            return new HttpResponseMessage(reply.Status)
            {
                Content = new StringContent(reply.Body, Encoding.UTF8, reply.MediaType)
            };
        }

        private static T? Served<T>(Dictionary<string, T> pages, string url)
            => pages.TryGetValue(NormalizeUrl(url), out var served) ? served : default;

        private static string NormalizeUrl(string url) => url.TrimEnd('/');

        private static string Format(ValidationMetadata metadata)
            => $"level={metadata.Level},by={metadata.ValidatedBy}";

        private static string FormatOrDefault(ValidationMetadata? metadata)
            => metadata is null ? "none" : Format(metadata);

        private string Render(List<ValidationIssue> issues)
        {
            var lines = new List<string> { "PROGRESS" };

            lines.AddRange(_progress);
            lines.Add("CALLS");
            lines.AddRange(_calls);
            lines.Add("REQUESTS");
            lines.AddRange(_requests);
            lines.Add("RETURNED");
            lines.AddRange(issues.Select(issue
                => $"{issue.Code}|{issue.Field ?? NotStored}|{issue.CurrentValue ?? NotStored}"));

            return string.Join('\n', lines);
        }

        private IWebContentFetcher BuildFetcher()
        {
            var mock = new Mock<IWebContentFetcher>();

            mock.Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string url, int? _, CancellationToken _) => Serve(HtmlPages, url, "fetch"));
            mock.Setup(s => s.FetchAsAsync(
                    It.IsAny<string>(),
                    It.IsAny<WebTools.NET.Models.EContentFormat>(),
                    It.IsAny<int?>(),
                    It.IsAny<WebTools.NET.Models.ESanitizeLevel>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((
                    string url,
                    WebTools.NET.Models.EContentFormat _,
                    int? __,
                    WebTools.NET.Models.ESanitizeLevel ___,
                    CancellationToken ____) => Serve(MarkdownPages, url, "markdown"));
            mock.Setup(s => s.CheckReachabilityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string url, CancellationToken _) =>
                {
                    _requests.Add($"root-reachability:{url}");

                    // The root is assumed reachable unless the fixture says otherwise, so that a test
                    // about the subdomain verdict only names the one answer it cares about.
                    var reachable = RootReachability.TryGetValue(NormalizeUrl(url), out var configured)
                        ? configured
                        : true;

                    return new WebTools.NET.Models.UrlCheckResult(reachable, 200, null);
                });

            return mock.Object;
        }

        private WebTools.NET.Models.WebContent Serve(
            Dictionary<string, StubPage> pages,
            string url,
            string kind)
        {
            _requests.Add($"{kind}:{url}");

            var page = Served(pages, url) ?? new StubPage();

            return new WebTools.NET.Models.WebContent(
                page.Success,
                page.Success ? page.Content : string.Empty,
                page.Success ? null : page.Error,
                page.FinalUrl ?? url);
        }

        private IProviderSchemaValidator BuildSchemaValidator()
        {
            var mock = new Mock<IProviderSchemaValidator>();
            mock.Setup(s => s.ValidateSchema(It.IsAny<string>(), It.IsAny<string>())).Returns([]);
            return mock.Object;
        }

        private IUrlReachabilityChecker BuildUrlChecker()
        {
            var mock = new Mock<IUrlReachabilityChecker>();
            mock.Setup(s => s.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Uri uri, CancellationToken _) =>
                {
                    _requests.Add($"reachability:{uri}");

                    var key = NormalizeUrl(uri.ToString());
                    if (ReachabilityFailures.TryGetValue(key, out var failure))
                        throw failure;

                    return Served(Reachability, uri.ToString())
                           ?? new WebTools.NET.Models.UrlCheckResult(true, 200, null);
                });
            return mock.Object;
        }

        private IContentAnalyzer BuildAnalyzer()
        {
            var mock = new Mock<IContentAnalyzer>();

            // The two content questions are answered from the page text, so one fixture can give a
            // different answer to each field without configuring the analyzer per URL.
            mock.Setup(s => s.HasNotFoundContentAsync(It.IsAny<string>()))
                .ReturnsAsync((string content)
                    => content.Contains(NotFoundMarker, StringComparison.OrdinalIgnoreCase));
            mock.Setup(s => s.HasLoginContentAsync(It.IsAny<string>()))
                .ReturnsAsync((string content) => content.Contains(LoginMarker, StringComparison.Ordinal));
            mock.Setup(s => s.AnalyzeApiPricingContentAsync(It.IsAny<string>()))
                .ReturnsAsync(() => ApiPricing);
            mock.Setup(s => s.AnalyzeSubscriptionPricingContentAsync(It.IsAny<string>()))
                .ReturnsAsync(() => SubscriptionPricing);
            mock.Setup(s => s.AnalyzeLoginUrlAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(() => LoginUrl);

            return mock.Object;
        }

        private IDuplicateIdChecker BuildDuplicateIdChecker()
        {
            var mock = new Mock<IDuplicateIdChecker>();
            mock.Setup(s => s.CheckDuplicateIds(It.IsAny<string>())).Returns([]);
            return mock.Object;
        }

        private IValidationMetadataService BuildMetadataService()
        {
            var mock = new Mock<IValidationMetadataService>();

            mock.Setup(s => s.Read(It.IsAny<string>()))
                .Returns(() =>
                {
                    _calls.Add($"read:{FormatOrDefault(Sidecar)}");
                    return Sidecar;
                });
            mock.Setup(s => s.Delete(It.IsAny<string>()))
                .Callback(() => _calls.Add("delete"));
            mock.Setup(s => s.SaveFromValidationAsync(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<ValidationIssue>>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<bool>()))
                .Callback((string _, IReadOnlyList<ValidationIssue> saved, string validatedBy, string __, bool force)
                    => _calls.Add($"save:count={saved.Count},by={validatedBy},force={force}"))
                .Returns(Task.CompletedTask);

            return mock.Object;
        }
    }

    /// <summary>
    /// Collects progress reports synchronously and renders each one as the tuple the freeze is taken
    /// against: field, stage, issue count, URL, message, and the issue handed along with the report.
    /// </summary>
    private sealed class RecordingProgress(List<string> progress) : IProgress<ValidationProgress>
    {
        private static readonly Dictionary<string, string> StageNames = new()
        {
            [nameof(ValidationStage.CheckingUrl)] = "checking-url",
            [nameof(ValidationStage.CheckingContent)] = "checking-content",
            [nameof(ValidationStage.CheckingPricingContent)] = "checking-pricing",
            [nameof(ValidationStage.CheckPassed)] = "passed",
            [nameof(ValidationStage.CheckFailed)] = "failed"
        };

        public void Report(ValidationProgress value)
            => progress.Add(
                $"{value.Field}|{StageName(value.Stage)}|{value.IssuesCount}|{value.Url}|{value.ResultMessage ?? NotStored}|{AttachedIssue(value.Issue)}");

        private static string AttachedIssue(ValidationIssue? issue)
            => issue is null ? "-" : $"{issue.Code}/{issue.Field ?? NotStored}";

        private static string StageName(ValidationStage stage)
            => StageNames.TryGetValue(Enum.GetName(stage)!, out var name) ? name : stage.ToString();
    }
}
