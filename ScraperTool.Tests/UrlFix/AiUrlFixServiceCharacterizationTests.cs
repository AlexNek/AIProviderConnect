using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using FluentAssertions;

using Moq;

using ScraperTool.Data;
using ScraperTool.Data.Entities;
using ScraperTool.Data.Repositories;
using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.Services.UrlResearch;
using ScraperTool.Services.UrlResearch.DecisionTree;
using ScraperTool.Services.Validation.Checks;

using WebTools.NET.Abstractions;

namespace ScraperTool.Tests.UrlFix;

/// <summary>
/// Phase 0 behavior freeze for <see cref="AiUrlFixService"/>. Each fact pins the complete transcript
/// of one run — ordered progress lines, research calls, persisted batches, token rows, the resulting
/// issue states and the run summary — so that the Refactor 20 extraction can be proven to preserve
/// today's behavior, quirks included.
/// </summary>
public partial class AiUrlFixServiceCharacterizationTests
{
    [GeneratedRegex(@"\((?:\d+ms|\d+m \d+s)\)")]
    private static partial Regex ElapsedRegex();

    [Fact]
    public async Task AiSetupMissingApiKey_ReportsOnlyApiKeyGuidance()
    {
        var fixture = new Fixture();
        fixture.AiSetup(apiKey: false);
        fixture.Issue("deepinfra.json", ProviderJsonFields.SubscriptionPricingUrl);

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 1
            -     ✖ AI setup incomplete: missing API key. Open AI Setup and configure the API key
            RESEARCH 0
            DB 0
            SAVES 0
            TOKENS 0
            ISSUES 1
            - file=deepinfra.json field=subscriptionPricingUrl status=None suggested=- reason=- severity=- noValue=False attempted=unset
            RESULT
            - suggestions=0 failed=1 prompt=0 completion=0 cost=0 label='' model= available=False
            SUGGESTIONS 0
            """);
    }

    [Fact]
    public async Task AiSetupMissingProviderSelection_ReportsOnlyProviderGuidance()
    {
        var fixture = new Fixture();
        fixture.AiSetup(providerSelection: false);
        fixture.Issue("deepinfra.json", ProviderJsonFields.SubscriptionPricingUrl);

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 1
            -     ✖ AI setup incomplete: missing provider selection. Open AI Setup and select a provider
            RESEARCH 0
            DB 0
            SAVES 0
            TOKENS 0
            ISSUES 1
            - file=deepinfra.json field=subscriptionPricingUrl status=None suggested=- reason=- severity=- noValue=False attempted=unset
            RESULT
            - suggestions=0 failed=1 prompt=0 completion=0 cost=0 label='' model= available=False
            SUGGESTIONS 0
            """);
    }

    [Fact]
    public async Task AiSetupMissingFallbackModelName_ReportsModelGuidance()
    {
        var fixture = new Fixture();
        fixture.AiSetup(modelConfigured: false, fallbackModel: null);
        fixture.Issue("deepinfra.json", ProviderJsonFields.SubscriptionPricingUrl);

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 1
            -     ✖ AI setup incomplete: missing fallback model name. Open Settings and select a model
            RESEARCH 0
            DB 0
            SAVES 0
            TOKENS 0
            ISSUES 1
            - file=deepinfra.json field=subscriptionPricingUrl status=None suggested=- reason=- severity=- noValue=False attempted=unset
            RESULT
            - suggestions=0 failed=1 prompt=0 completion=0 cost=0 label='' model= available=False
            SUGGESTIONS 0
            """);
    }

    [Fact]
    public async Task AiSetupMissingEverything_ReportsEveryGuidance()
    {
        var fixture = new Fixture();
        fixture.AiSetup(
            apiKey: false,
            providerSelection: false,
            modelConfigured: false,
            primaryModel: null,
            fallbackModel: null);
        fixture.Issue("deepinfra.json", ProviderJsonFields.SubscriptionPricingUrl);

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 1
            -     ✖ AI setup incomplete: missing API key and provider selection and primary and fallback model names. Open AI Setup and configure the API key and select a provider. Open Settings and select models
            RESEARCH 0
            DB 0
            SAVES 0
            TOKENS 0
            ISSUES 1
            - file=deepinfra.json field=subscriptionPricingUrl status=None suggested=- reason=- severity=- noValue=False attempted=unset
            RESULT
            - suggestions=0 failed=1 prompt=0 completion=0 cost=0 label='' model= available=False
            SUGGESTIONS 0
            """);
    }

    [Fact]
    public async Task UnknownFieldAndMissingProvider_SkipBeforeResearch()
    {
        var fixture = new Fixture();
        fixture.Issue("orphan.json", null, message: "Field is not recognised by the schema.");
        fixture.Issue("ghost.json", ProviderJsonFields.Website);

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 8
            - Processing 2 issue(s) in 1 batch(es)...
            -   Batch 1/1 (2 URLs) — starting research...
            -   Batch 1: orphan.json, ghost.json (website)
            -     Researching orphan.json ()...
            -     ✖ orphan.json: unknown field in issue — skipping: Field is not recognised by the schema.
            -     Researching ghost.json (website)...
            -     ✖ ghost.json: provider definition not found — skipping
            -   ✓ Batch 1: 0 suggestion(s) found, 2 failed (<elapsed>)
            RESEARCH 0
            DB 1
            - call=1 file=orphan.json code=UrlError field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=ghost.json code=UrlError field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            SAVES 2
            TOKENS 1
            - model=test-model provider=test-provider op=AiUrlFix prompt=0 completion=0 cost=0 source=Unknown created=set
            ISSUES 2
            - file=orphan.json field=- status=Pending suggested=- reason=- severity=- noValue=False attempted=set
            - file=ghost.json field=website status=Pending suggested=- reason=- severity=- noValue=False attempted=set
            RESULT
            - suggestions=0 failed=0 prompt=0 completion=0 cost=0 label='' model=test-model available=True
            SUGGESTIONS 0
            """);
    }

    [Fact]
    public async Task SemanticallyCorrectPricingUrl_IsDismissedWithoutResearch()
    {
        var fixture = new Fixture();
        fixture.Provider("deepinfra");
        fixture.Issue(
            "deepinfra.json",
            ProviderJsonFields.SubscriptionPricingUrl,
            ValidationIssueCodes.UrlTimeout,
            current: "https://deepinfra.example.com/pricing");

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 6
            - Processing 1 issue(s) in 1 batch(es)...
            -   Batch 1/1 (1 URLs) — starting research...
            -   Batch 1: deepinfra.json (subscriptionPricingUrl)
            -     Researching deepinfra.json (subscriptionPricingUrl)...
            -     ✓ deepinfra.json: current subscriptionPricingUrl is semantically correct — environmental issue only, no change needed
            -   ✓ Batch 1: 0 suggestion(s) found, 1 no valid value (<elapsed>)
            RESEARCH 0
            DB 2
            - call=1 file=deepinfra.json code=UrlTimeout field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=2 file=deepinfra.json code=UrlTimeout field=subscriptionPricingUrl current=https://deepinfra.example.com/pricing suggested=- reason=Current URL is semantically correct; error is environmental severity=urlFix status=Dismissed saved=set
            SAVES 3
            TOKENS 1
            - model=test-model provider=test-provider op=AiUrlFix prompt=0 completion=0 cost=0 source=Unknown created=set
            ISSUES 1
            - file=deepinfra.json field=subscriptionPricingUrl status=Dismissed suggested=- reason=Current URL is semantically correct; error is environmental severity=urlFix noValue=False attempted=set
            RESULT
            - suggestions=0 failed=0 prompt=0 completion=0 cost=0 label='' model=test-model available=True
            SUGGESTIONS 0
            """);
    }

    [Fact]
    public async Task NoValidValueForUrlField_SuggestsDashAndAnswersLaterFinding()
    {
        var fixture = new Fixture();
        fixture.Provider("deepinfra");
        fixture.Issue(
            "deepinfra.json",
            ProviderJsonFields.SubscriptionPricingUrl,
            ValidationIssueCodes.MissingSubscriptionPricingUrl);
        fixture.Issue(
            "deepinfra.json",
            ProviderJsonFields.SubscriptionPricingUrl,
            ValidationIssueCodes.UrlNotFound,
            message: "Subscription pricing page is documented under a second address.");
        fixture.Answer(
            "deepinfra.json",
            ProviderJsonFields.SubscriptionPricingUrl,
            Result(null, "Provider only offers pay-as-you-go pricing", prompt: 3, completion: 4));

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 8
            - Processing 2 issue(s) in 1 batch(es)...
            -   Batch 1/1 (2 URLs) — starting research...
            -   Batch 1: deepinfra.json (subscriptionPricingUrl), deepinfra.json (subscriptionPricingUrl)
            -     Researching deepinfra.json (subscriptionPricingUrl)...
            -     — deepinfra.json: no valid value — suggesting '-' (Provider only offers pay-as-you-go pricing)
            -     Researching deepinfra.json (subscriptionPricingUrl)...
            -     ✓ deepinfra.json: subscriptionPricingUrl was answered earlier in this run — one suggestion per field: -
            -   ✓ Batch 1: 1 suggestion(s) found (<elapsed>)
            RESEARCH 1
            - file=deepinfra.json provider=deepinfra field=subscriptionPricingUrl current='' region=us website=https://deepinfra.example.com redirect=- model=test-model session=S1
            DB 2
            - call=1 file=deepinfra.json code=MissingSubscriptionPricingUrl field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=deepinfra.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=2 file=deepinfra.json code=MissingSubscriptionPricingUrl field=subscriptionPricingUrl current='' suggested=- reason=Provider only offers pay-as-you-go pricing severity=urlFix status=Pending saved=set
            - call=2 file=deepinfra.json code=UrlNotFound field=subscriptionPricingUrl current='' suggested=- reason=Provider only offers pay-as-you-go pricing severity=urlFix status=Pending saved=set
            SAVES 3
            TOKENS 1
            - model=test-model provider=test-provider op=AiUrlFix prompt=3 completion=4 cost=0 source=Unknown created=set
            ISSUES 2
            - file=deepinfra.json field=subscriptionPricingUrl status=Pending suggested=- reason=Provider only offers pay-as-you-go pricing severity=urlFix noValue=False attempted=set
            - file=deepinfra.json field=subscriptionPricingUrl status=Pending suggested=- reason=Provider only offers pay-as-you-go pricing severity=urlFix noValue=False attempted=set
            RESULT
            - suggestions=1 failed=0 prompt=3 completion=4 cost=0 label='' model=test-model available=True
            SUGGESTIONS 1
            - provider=deepinfra display=deepinfra.json field=subscriptionPricingUrl suggested=- reason=Provider only offers pay-as-you-go pricing severity=urlFix current=''
            """);
    }

    [Fact]
    public async Task NoValidValueForNumericField_FailsAndRecordsResearchConclusion()
    {
        var fixture = new Fixture();
        fixture.Provider("deepinfra");
        fixture.Issue(
            "deepinfra.json",
            ProviderJsonFields.MinModelCount,
            ValidationIssueCodes.MinModelCountInvalid,
            current: "0");
        fixture.Answer(
            "deepinfra.json",
            ProviderJsonFields.MinModelCount,
            Result(null, "No documented model count"));

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 6
            - Processing 1 issue(s) in 1 batch(es)...
            -   Batch 1/1 (1 URLs) — starting research...
            -   Batch 1: deepinfra.json (minModelCount)
            -     Researching deepinfra.json (minModelCount)...
            -     — deepinfra.json: could not determine numeric value (No documented model count)
            -   ✓ Batch 1: 0 suggestion(s) found, 1 failed (<elapsed>)
            RESEARCH 1
            - file=deepinfra.json provider=deepinfra field=minModelCount current=0 region=us website=https://deepinfra.example.com redirect=- model=test-model session=S1
            DB 2
            - call=1 file=deepinfra.json code=MinModelCountInvalid field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=2 file=deepinfra.json code=MinModelCountInvalid field=minModelCount current=0 suggested=- reason=No documented model count severity=urlFix status=Failed saved=set
            SAVES 3
            TOKENS 1
            - model=test-model provider=test-provider op=AiUrlFix prompt=0 completion=0 cost=0 source=Unknown created=set
            ISSUES 1
            - file=deepinfra.json field=minModelCount status=Failed suggested=- reason=No documented model count severity=urlFix noValue=True attempted=set
            RESULT
            - suggestions=0 failed=1 prompt=0 completion=0 cost=0 label='' model=test-model available=True
            SUGGESTIONS 0
            """);
    }

    [Fact]
    public async Task ResearchFailuresAndCancellations_AreRecordedPerIssue()
    {
        var fixture = new Fixture();
        fixture.Provider("alpha");
        fixture.Provider("beta");
        fixture.Provider("gamma");
        fixture.Provider("delta");
        fixture.Issue("alpha.json", ProviderJsonFields.Website, ValidationIssueCodes.UrlNotReachable);
        fixture.Issue("beta.json", ProviderJsonFields.Website, ValidationIssueCodes.UrlError);
        fixture.Issue("gamma.json", ProviderJsonFields.Website, ValidationIssueCodes.UrlNotFound);
        fixture.Issue("delta.json", ProviderJsonFields.Website, ValidationIssueCodes.UrlTimeout);
        fixture.Answer(
            "alpha.json",
            ProviderJsonFields.Website,
            Result("https://alpha.example.com", "provider answered 500", success: false));
        fixture.Throws("beta.json", ProviderJsonFields.Website, () => new InvalidOperationException("boom"));
        fixture.Throws(
            "gamma.json",
            ProviderJsonFields.Website,
            () => new OperationCanceledException("token expired"));
        fixture.Throws("delta.json", ProviderJsonFields.Website, () => new OperationCanceledException());

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 12
            - Processing 4 issue(s) in 1 batch(es)...
            -   Batch 1/1 (4 URLs) — starting research...
            -   Batch 1: alpha.json (website), beta.json (website), gamma.json (website), delta.json (website)
            -     Researching alpha.json (website)...
            -     ✖ alpha.json: no valid suggestion (provider answered 500)
            -     Researching beta.json (website)...
            -     ✖ beta.json: research failed — InvalidOperationException: boom
            -     Researching gamma.json (website)...
            -     ✖ gamma.json: token expired
            -     Researching delta.json (website)...
            -     ✖ delta.json: The operation was canceled.
            -   ✓ Batch 1: 0 suggestion(s) found, 4 failed (<elapsed>)
            RESEARCH 4
            - file=alpha.json provider=alpha field=website current='' region=us website=https://alpha.example.com redirect=- model=test-model session=S1
            - file=beta.json provider=beta field=website current='' region=us website=https://beta.example.com redirect=- model=test-model session=S2
            - file=gamma.json provider=gamma field=website current='' region=us website=https://gamma.example.com redirect=- model=test-model session=S3
            - file=delta.json provider=delta field=website current='' region=us website=https://delta.example.com redirect=- model=test-model session=S4
            DB 2
            - call=1 file=alpha.json code=UrlNotReachable field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=beta.json code=UrlError field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=gamma.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=delta.json code=UrlTimeout field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=2 file=alpha.json code=UrlNotReachable field=website current='' suggested=- reason=provider answered 500 severity=urlFix status=Failed saved=set
            SAVES 3
            TOKENS 1
            - model=test-model provider=test-provider op=AiUrlFix prompt=0 completion=0 cost=0 source=Unknown created=set
            ISSUES 4
            - file=alpha.json field=website status=Failed suggested=- reason=provider answered 500 severity=urlFix noValue=False attempted=set
            - file=beta.json field=website status=Pending suggested=- reason=- severity=- noValue=False attempted=set
            - file=gamma.json field=website status=Pending suggested=- reason=- severity=- noValue=False attempted=set
            - file=delta.json field=website status=Pending suggested=- reason=- severity=- noValue=False attempted=set
            RESULT
            - suggestions=0 failed=1 prompt=0 completion=0 cost=0 label='' model=test-model available=True
            SUGGESTIONS 0
            """);
    }

    [Fact]
    public async Task VerifiedSuggestion_IsReportedAndRecordedAsPending()
    {
        var fixture = new Fixture();
        fixture.Provider("deepinfra");
        fixture.Issue(
            "deepinfra.json",
            ProviderJsonFields.ApiPricingUrl,
            ValidationIssueCodes.UrlNotFound,
            current: "https://deepinfra.example.com/price");
        fixture.Answer(
            "deepinfra.json",
            ProviderJsonFields.ApiPricingUrl,
            Result("https://www.deepinfra.example.com/pricing", "official pricing page", prompt: 5, completion: 6));

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 6
            - Processing 1 issue(s) in 1 batch(es)...
            -   Batch 1/1 (1 URLs) — starting research...
            -   Batch 1: deepinfra.json (apiPricingUrl)
            -     Researching deepinfra.json (apiPricingUrl)...
            -     ✓ deepinfra.json: https://www.deepinfra.example.com/pricing (suggested)
            -   ✓ Batch 1: 1 suggestion(s) found (<elapsed>)
            RESEARCH 1
            - file=deepinfra.json provider=deepinfra field=apiPricingUrl current=https://deepinfra.example.com/price region=us website=https://deepinfra.example.com redirect=- model=test-model session=S1
            DB 2
            - call=1 file=deepinfra.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=2 file=deepinfra.json code=UrlNotFound field=apiPricingUrl current=https://deepinfra.example.com/price suggested=https://www.deepinfra.example.com/pricing reason=official pricing page severity=urlFix status=Pending saved=set
            SAVES 3
            TOKENS 1
            - model=test-model provider=test-provider op=AiUrlFix prompt=5 completion=6 cost=0 source=Unknown created=set
            ISSUES 1
            - file=deepinfra.json field=apiPricingUrl status=Pending suggested=https://www.deepinfra.example.com/pricing reason=official pricing page severity=urlFix noValue=False attempted=set
            RESULT
            - suggestions=1 failed=0 prompt=5 completion=6 cost=0 label='' model=test-model available=True
            SUGGESTIONS 1
            - provider=deepinfra display=deepinfra.json field=apiPricingUrl suggested=https://www.deepinfra.example.com/pricing reason=official pricing page severity=urlFix current=https://deepinfra.example.com/price
            """);
    }

    [Fact]
    public async Task SuggestionOnUnrelatedDomain_IsRejectedAsFailed()
    {
        var fixture = new Fixture();
        fixture.Provider("deepinfra");
        fixture.Issue(
            "deepinfra.json",
            ProviderJsonFields.ApiPricingUrl,
            ValidationIssueCodes.UrlNotFound,
            current: "https://deepinfra.example.com/price");
        fixture.Answer(
            "deepinfra.json",
            ProviderJsonFields.ApiPricingUrl,
            Result("https://random.example.org/pricing", "competitor pricing comparison"));

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 7
            - Processing 1 issue(s) in 1 batch(es)...
            -   Batch 1/1 (1 URLs) — starting research...
            -   Batch 1: deepinfra.json (apiPricingUrl)
            -     Researching deepinfra.json (apiPricingUrl)...
            -     ✖ deepinfra.json: suggested domain has no relationship to provider 'deepinfra' — rejecting: https://random.example.org/pricing
            -     ✖ deepinfra.json: suggestion 'https://random.example.org/pricing' not reachable — stored as failed
            -   ✓ Batch 1: 0 suggestion(s) found (<elapsed>)
            RESEARCH 1
            - file=deepinfra.json provider=deepinfra field=apiPricingUrl current=https://deepinfra.example.com/price region=us website=https://deepinfra.example.com redirect=- model=test-model session=S1
            DB 2
            - call=1 file=deepinfra.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=2 file=deepinfra.json code=UrlNotFound field=apiPricingUrl current=https://deepinfra.example.com/price suggested=https://random.example.org/pricing reason=competitor pricing comparison severity=urlFix status=Failed saved=set
            SAVES 3
            TOKENS 1
            - model=test-model provider=test-provider op=AiUrlFix prompt=0 completion=0 cost=0 source=Unknown created=set
            ISSUES 1
            - file=deepinfra.json field=apiPricingUrl status=Failed suggested=https://random.example.org/pricing reason=competitor pricing comparison severity=urlFix noValue=False attempted=set
            RESULT
            - suggestions=0 failed=1 prompt=0 completion=0 cost=0 label='' model=test-model available=True
            SUGGESTIONS 0
            """);
    }

    [Fact]
    public async Task SuggestionWithContentPathForPricingField_IsRejectedAsFailed()
    {
        var fixture = new Fixture();
        fixture.Provider("deepinfra");
        fixture.Issue(
            "deepinfra.json",
            ProviderJsonFields.ApiPricingUrl,
            ValidationIssueCodes.PricingContentInvalid,
            current: "https://deepinfra.example.com/price");
        fixture.Answer(
            "deepinfra.json",
            ProviderJsonFields.ApiPricingUrl,
            Result("https://deepinfra.example.com/blog/pricing", "announced pricing in a post"));

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 7
            - Processing 1 issue(s) in 1 batch(es)...
            -   Batch 1/1 (1 URLs) — starting research...
            -   Batch 1: deepinfra.json (apiPricingUrl)
            -     Researching deepinfra.json (apiPricingUrl)...
            -     ✖ deepinfra.json: suggested URL path is structurally incompatible with 'apiPricingUrl' — rejecting: https://deepinfra.example.com/blog/pricing
            -     ✖ deepinfra.json: suggestion 'https://deepinfra.example.com/blog/pricing' not reachable — stored as failed
            -   ✓ Batch 1: 0 suggestion(s) found (<elapsed>)
            RESEARCH 1
            - file=deepinfra.json provider=deepinfra field=apiPricingUrl current=https://deepinfra.example.com/price region=us website=https://deepinfra.example.com redirect=- model=test-model session=S1
            DB 2
            - call=1 file=deepinfra.json code=PricingContentInvalid field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=2 file=deepinfra.json code=PricingContentInvalid field=apiPricingUrl current=https://deepinfra.example.com/price suggested=https://deepinfra.example.com/blog/pricing reason=announced pricing in a post severity=urlFix status=Failed saved=set
            SAVES 3
            TOKENS 1
            - model=test-model provider=test-provider op=AiUrlFix prompt=0 completion=0 cost=0 source=Unknown created=set
            ISSUES 1
            - file=deepinfra.json field=apiPricingUrl status=Failed suggested=https://deepinfra.example.com/blog/pricing reason=announced pricing in a post severity=urlFix noValue=False attempted=set
            RESULT
            - suggestions=0 failed=1 prompt=0 completion=0 cost=0 label='' model=test-model available=True
            SUGGESTIONS 0
            """);
    }

    [Fact]
    public async Task BaseUrlSuggestions_AreProbedForApiResponses()
    {
        var fixture = new Fixture();
        fixture.Provider("probe1");
        fixture.Provider("probe2");
        fixture.Provider("probe3");
        fixture.Issue(
            "probe1.json",
            ProviderJsonFields.BaseUrl,
            ValidationIssueCodes.BaseUrlNotApiEndpoint,
            current: "https://probe1.example.com");
        fixture.Issue(
            "probe2.json",
            ProviderJsonFields.BaseUrl,
            ValidationIssueCodes.BaseUrlNotApiEndpoint,
            current: "https://probe2.example.com");
        fixture.Issue(
            "probe3.json",
            ProviderJsonFields.BaseUrl,
            ValidationIssueCodes.UrlNotFound,
            current: "https://probe3.example.com");
        fixture.Answer(
            "probe1.json",
            ProviderJsonFields.BaseUrl,
            Result("https://api.probe1.example.com/v1", "documented endpoint"));
        fixture.Answer(
            "probe2.json",
            ProviderJsonFields.BaseUrl,
            Result("https://api.probe2.example.com/v1", "documented endpoint"));
        fixture.Answer(
            "probe3.json",
            ProviderJsonFields.BaseUrl,
            Result("https://api.probe3.example.com/v1", "documented endpoint"));
        fixture.Probe("https://api.probe1.example.com/v1", "text/html");
        fixture.Probe("https://api.probe2.example.com/v1", "application/json");
        fixture.Probe("https://api.probe3.example.com/v1", "text/html", status: HttpStatusCode.NotFound);

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 11
            - Processing 3 issue(s) in 1 batch(es)...
            -   Batch 1/1 (3 URLs) — starting research...
            -   Batch 1: probe1.json (baseUrl), probe2.json (baseUrl), probe3.json (baseUrl)
            -     Researching probe1.json (baseUrl)...
            -     ✖ probe1.json: suggested baseUrl does not serve API responses — rejecting: https://api.probe1.example.com/v1
            -     ✖ probe1.json: suggestion 'https://api.probe1.example.com/v1' not reachable — stored as failed
            -     Researching probe2.json (baseUrl)...
            -     ✓ probe2.json: https://api.probe2.example.com/v1 (suggested)
            -     Researching probe3.json (baseUrl)...
            -     ✓ probe3.json: https://api.probe3.example.com/v1 (suggested)
            -   ✓ Batch 1: 2 suggestion(s) found (<elapsed>)
            RESEARCH 3
            - file=probe1.json provider=probe1 field=baseUrl current=https://probe1.example.com region=us website=https://probe1.example.com redirect=- model=test-model session=S1
            - file=probe2.json provider=probe2 field=baseUrl current=https://probe2.example.com region=us website=https://probe2.example.com redirect=- model=test-model session=S2
            - file=probe3.json provider=probe3 field=baseUrl current=https://probe3.example.com region=us website=https://probe3.example.com redirect=- model=test-model session=S3
            DB 2
            - call=1 file=probe1.json code=BaseUrlNotApiEndpoint field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=probe2.json code=BaseUrlNotApiEndpoint field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=probe3.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=2 file=probe1.json code=BaseUrlNotApiEndpoint field=baseUrl current=https://probe1.example.com suggested=https://api.probe1.example.com/v1 reason=documented endpoint severity=urlFix status=Failed saved=set
            - call=2 file=probe2.json code=BaseUrlNotApiEndpoint field=baseUrl current=https://probe2.example.com suggested=https://api.probe2.example.com/v1 reason=documented endpoint severity=urlFix status=Pending saved=set
            - call=2 file=probe3.json code=UrlNotFound field=baseUrl current=https://probe3.example.com suggested=https://api.probe3.example.com/v1 reason=documented endpoint severity=urlFix status=Pending saved=set
            SAVES 3
            TOKENS 1
            - model=test-model provider=test-provider op=AiUrlFix prompt=0 completion=0 cost=0 source=Unknown created=set
            ISSUES 3
            - file=probe1.json field=baseUrl status=Failed suggested=https://api.probe1.example.com/v1 reason=documented endpoint severity=urlFix noValue=False attempted=set
            - file=probe2.json field=baseUrl status=Pending suggested=https://api.probe2.example.com/v1 reason=documented endpoint severity=urlFix noValue=False attempted=set
            - file=probe3.json field=baseUrl status=Pending suggested=https://api.probe3.example.com/v1 reason=documented endpoint severity=urlFix noValue=False attempted=set
            RESULT
            - suggestions=2 failed=1 prompt=0 completion=0 cost=0 label='' model=test-model available=True
            SUGGESTIONS 2
            - provider=probe2 display=probe2.json field=baseUrl suggested=https://api.probe2.example.com/v1 reason=documented endpoint severity=urlFix current=https://probe2.example.com
            - provider=probe3 display=probe3.json field=baseUrl suggested=https://api.probe3.example.com/v1 reason=documented endpoint severity=urlFix current=https://probe3.example.com
            """);
    }

    [Fact]
    public async Task CurrentUrlConfirmedValid_DismissesAndKeepsRegionalSuggestions()
    {
        var fixture = new Fixture();
        fixture.Provider("deepinfra");
        fixture.Issue(
            "deepinfra.json",
            ProviderJsonFields.Website,
            ValidationIssueCodes.UrlRequiresAuth,
            current: "https://deepinfra.example.com/");
        fixture.Answer(
            "deepinfra.json",
            ProviderJsonFields.Website,
            Result(
                "https://deepinfra.example.com",
                "current address is the public homepage",
                suggestions:
                [
                    Regional("""{"us":"https://us.example.com/v1","eu":"https://eu.example.com/v1"}"""),
                    Regional(null),
                    Regional("https://api.example.com/v1"),
                    Regional("""["https://api.example.com/v1","https://eu.example.com/v1"]""")
                ]));

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 7
            - Processing 1 issue(s) in 1 batch(es)...
            -   Batch 1/1 (1 URLs) — starting research...
            -   Batch 1: deepinfra.json (website)
            -     Researching deepinfra.json (website)...
            -     ✓ deepinfra.json: current URL confirmed valid — no change needed
            -     ✓ deepinfra.json: 5 regional endpoint suggestion(s)
            -   ✓ Batch 1: 4 suggestion(s) found, 1 no valid value (<elapsed>)
            RESEARCH 1
            - file=deepinfra.json provider=deepinfra field=website current=https://deepinfra.example.com/ region=us website=https://deepinfra.example.com redirect=- model=test-model session=S1
            DB 2
            - call=1 file=deepinfra.json code=UrlRequiresAuth field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=2 file=deepinfra.json code=UrlRequiresAuth field=website current=https://deepinfra.example.com/ suggested=- reason=current address is the public homepage severity=urlFix status=Dismissed saved=set
            SAVES 3
            TOKENS 1
            - model=test-model provider=test-provider op=AiUrlFix prompt=0 completion=0 cost=0 source=Unknown created=set
            ISSUES 1
            - file=deepinfra.json field=website status=Dismissed suggested=- reason=current address is the public homepage severity=urlFix noValue=False attempted=set
            RESULT
            - suggestions=4 failed=0 prompt=0 completion=0 cost=0 label='' model=test-model available=True
            SUGGESTIONS 4
            - provider=deepinfra display=deepinfra.json field=regionalEndpoints suggested={"us":"https://us.example.com/v1","eu":"https://eu.example.com/v1"} reason=Regional endpoints documented alongside the answer severity=urlFix current=-
            - provider=deepinfra display=deepinfra.json field=regionalEndpoints suggested=- reason=Regional endpoints documented alongside the answer severity=urlFix current=-
            - provider=deepinfra display=deepinfra.json field=regionalEndpoints suggested=https://api.example.com/v1 reason=Regional endpoints documented alongside the answer severity=urlFix current=-
            - provider=deepinfra display=deepinfra.json field=regionalEndpoints suggested=["https://api.example.com/v1","https://eu.example.com/v1"] reason=Regional endpoints documented alongside the answer severity=urlFix current=-
            """);
    }

    [Fact]
    public async Task AllBatchesFailing_StopsAfterTwoBatches()
    {
        var fixture = new Fixture();
        for (var index = 1; index <= 11; index++)
        {
            var fileName = $"p{index:x2}.json";
            fixture.Provider($"p{index:x2}");
            fixture.Issue(
                fileName,
                ProviderJsonFields.Website,
                ValidationIssueCodes.UrlNotFound,
                current: "https://old.example.com");
            fixture.Answer(
                fileName,
                ProviderJsonFields.Website,
                Result("https://p.example.com", "provider unreachable", success: false));
        }

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 28
            - Processing 11 issue(s) in 3 batch(es)...
            -   Batch 1/3 (5 URLs) — starting research...
            -   Batch 1: p01.json (website), p02.json (website), p03.json (website), p04.json (website), p05.json (website)
            -     Researching p01.json (website)...
            -     ✖ p01.json: no valid suggestion (provider unreachable)
            -     Researching p02.json (website)...
            -     ✖ p02.json: no valid suggestion (provider unreachable)
            -     Researching p03.json (website)...
            -     ✖ p03.json: no valid suggestion (provider unreachable)
            -     Researching p04.json (website)...
            -     ✖ p04.json: no valid suggestion (provider unreachable)
            -     Researching p05.json (website)...
            -     ✖ p05.json: no valid suggestion (provider unreachable)
            -   ✓ Batch 1: 0 suggestion(s) found, 5 failed (<elapsed>)
            -   Batch 2/3 (5 URLs) — starting research...
            -   Batch 2: p06.json (website), p07.json (website), p08.json (website), p09.json (website), p0a.json (website)
            -     Researching p06.json (website)...
            -     ✖ p06.json: no valid suggestion (provider unreachable)
            -     Researching p07.json (website)...
            -     ✖ p07.json: no valid suggestion (provider unreachable)
            -     Researching p08.json (website)...
            -     ✖ p08.json: no valid suggestion (provider unreachable)
            -     Researching p09.json (website)...
            -     ✖ p09.json: no valid suggestion (provider unreachable)
            -     Researching p0a.json (website)...
            -     ✖ p0a.json: no valid suggestion (provider unreachable)
            -   ✓ Batch 2: 0 suggestion(s) found, 5 failed (<elapsed>)
            -   ⚠ Stopping early: all items in the last 2 batch(es) failed — likely a configuration or connectivity issue. Fix the problem and retry failed items.
            RESEARCH 10
            - file=p01.json provider=p01 field=website current=https://old.example.com region=us website=https://p01.example.com redirect=- model=test-model session=S1
            - file=p02.json provider=p02 field=website current=https://old.example.com region=us website=https://p02.example.com redirect=- model=test-model session=S2
            - file=p03.json provider=p03 field=website current=https://old.example.com region=us website=https://p03.example.com redirect=- model=test-model session=S3
            - file=p04.json provider=p04 field=website current=https://old.example.com region=us website=https://p04.example.com redirect=- model=test-model session=S4
            - file=p05.json provider=p05 field=website current=https://old.example.com region=us website=https://p05.example.com redirect=- model=test-model session=S5
            - file=p06.json provider=p06 field=website current=https://old.example.com region=us website=https://p06.example.com redirect=- model=test-model session=S6
            - file=p07.json provider=p07 field=website current=https://old.example.com region=us website=https://p07.example.com redirect=- model=test-model session=S7
            - file=p08.json provider=p08 field=website current=https://old.example.com region=us website=https://p08.example.com redirect=- model=test-model session=S8
            - file=p09.json provider=p09 field=website current=https://old.example.com region=us website=https://p09.example.com redirect=- model=test-model session=S9
            - file=p0a.json provider=p0a field=website current=https://old.example.com region=us website=https://p0a.example.com redirect=- model=test-model session=S10
            DB 4
            - call=1 file=p01.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=p02.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=p03.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=p04.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=p05.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=2 file=p01.json code=UrlNotFound field=website current=https://old.example.com suggested=- reason=provider unreachable severity=urlFix status=Failed saved=set
            - call=2 file=p02.json code=UrlNotFound field=website current=https://old.example.com suggested=- reason=provider unreachable severity=urlFix status=Failed saved=set
            - call=2 file=p03.json code=UrlNotFound field=website current=https://old.example.com suggested=- reason=provider unreachable severity=urlFix status=Failed saved=set
            - call=2 file=p04.json code=UrlNotFound field=website current=https://old.example.com suggested=- reason=provider unreachable severity=urlFix status=Failed saved=set
            - call=2 file=p05.json code=UrlNotFound field=website current=https://old.example.com suggested=- reason=provider unreachable severity=urlFix status=Failed saved=set
            - call=3 file=p06.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=3 file=p07.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=3 file=p08.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=3 file=p09.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=3 file=p0a.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=4 file=p06.json code=UrlNotFound field=website current=https://old.example.com suggested=- reason=provider unreachable severity=urlFix status=Failed saved=set
            - call=4 file=p07.json code=UrlNotFound field=website current=https://old.example.com suggested=- reason=provider unreachable severity=urlFix status=Failed saved=set
            - call=4 file=p08.json code=UrlNotFound field=website current=https://old.example.com suggested=- reason=provider unreachable severity=urlFix status=Failed saved=set
            - call=4 file=p09.json code=UrlNotFound field=website current=https://old.example.com suggested=- reason=provider unreachable severity=urlFix status=Failed saved=set
            - call=4 file=p0a.json code=UrlNotFound field=website current=https://old.example.com suggested=- reason=provider unreachable severity=urlFix status=Failed saved=set
            SAVES 5
            TOKENS 1
            - model=test-model provider=test-provider op=AiUrlFix prompt=0 completion=0 cost=0 source=Unknown created=set
            ISSUES 11
            - file=p01.json field=website status=Failed suggested=- reason=provider unreachable severity=urlFix noValue=False attempted=set
            - file=p02.json field=website status=Failed suggested=- reason=provider unreachable severity=urlFix noValue=False attempted=set
            - file=p03.json field=website status=Failed suggested=- reason=provider unreachable severity=urlFix noValue=False attempted=set
            - file=p04.json field=website status=Failed suggested=- reason=provider unreachable severity=urlFix noValue=False attempted=set
            - file=p05.json field=website status=Failed suggested=- reason=provider unreachable severity=urlFix noValue=False attempted=set
            - file=p06.json field=website status=Failed suggested=- reason=provider unreachable severity=urlFix noValue=False attempted=set
            - file=p07.json field=website status=Failed suggested=- reason=provider unreachable severity=urlFix noValue=False attempted=set
            - file=p08.json field=website status=Failed suggested=- reason=provider unreachable severity=urlFix noValue=False attempted=set
            - file=p09.json field=website status=Failed suggested=- reason=provider unreachable severity=urlFix noValue=False attempted=set
            - file=p0a.json field=website status=Failed suggested=- reason=provider unreachable severity=urlFix noValue=False attempted=set
            - file=p0b.json field=website status=None suggested=- reason=- severity=- noValue=False attempted=unset
            RESULT
            - suggestions=0 failed=10 prompt=0 completion=0 cost=0 label='' model=test-model available=True
            SUGGESTIONS 0
            """);
    }

    [Fact]
    public async Task SecondBatch_ReusesAnswersAndSessionsFromTheFirst()
    {
        var fixture = new Fixture();
        for (var index = 1; index <= 5; index++)
        {
            var fileName = $"b{index:x2}.json";
            fixture.Provider($"b{index:x2}");
            fixture.Issue(
                fileName,
                ProviderJsonFields.SubscriptionPricingUrl,
                ValidationIssueCodes.UrlNotFound,
                current: $"https://b{index:x2}.example.com/price",
                redirect: index == 1 ? "https://b01.example.com/old-pricing" : null);
        }

        fixture.Issue(
            "b01.json",
            ProviderJsonFields.SubscriptionPricingUrl,
            ValidationIssueCodes.PricingUrlRedirected,
            current: "https://b01.example.com/price",
            message: "Pricing page moved to the redirect target.");
        fixture.Answer(
            "b01.json",
            ProviderJsonFields.SubscriptionPricingUrl,
            Result("https://b01.example.com/pricing", "canonical pricing page", prompt: 2, completion: 3));
        fixture.Answer(
            "b02.json",
            ProviderJsonFields.SubscriptionPricingUrl,
            Result(null, "Subscription plans are not offered", prompt: 4, completion: 5));
        for (var index = 3; index <= 5; index++)
        {
            fixture.Answer(
                $"b{index:x2}.json",
                ProviderJsonFields.SubscriptionPricingUrl,
                Result("https://b.example.com/pricing", "search returned nothing", success: false));
        }

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 19
            - Processing 6 issue(s) in 2 batch(es)...
            -   Batch 1/2 (5 URLs) — starting research...
            -   Batch 1: b01.json (subscriptionPricingUrl), b02.json (subscriptionPricingUrl), b03.json (subscriptionPricingUrl), b04.json (subscriptionPricingUrl), b05.json (subscriptionPricingUrl)
            -     Researching b01.json (subscriptionPricingUrl)...
            -     ✓ b01.json: https://b01.example.com/pricing (suggested)
            -     Researching b02.json (subscriptionPricingUrl)...
            -     — b02.json: no valid value — suggesting '-' (Subscription plans are not offered)
            -     Researching b03.json (subscriptionPricingUrl)...
            -     ✖ b03.json: no valid suggestion (search returned nothing)
            -     Researching b04.json (subscriptionPricingUrl)...
            -     ✖ b04.json: no valid suggestion (search returned nothing)
            -     Researching b05.json (subscriptionPricingUrl)...
            -     ✖ b05.json: no valid suggestion (search returned nothing)
            -   ✓ Batch 1: 2 suggestion(s) found, 3 failed (<elapsed>)
            -   Batch 2/2 (1 URLs) — starting research...
            -   Batch 2: b01.json (subscriptionPricingUrl)
            -     Researching b01.json (subscriptionPricingUrl)...
            -     ✓ b01.json: subscriptionPricingUrl was answered earlier in this run — one suggestion per field: https://b01.example.com/pricing
            -   ✓ Batch 2: 0 suggestion(s) found (<elapsed>)
            RESEARCH 5
            - file=b01.json provider=b01 field=subscriptionPricingUrl current=https://b01.example.com/price region=us website=https://b01.example.com redirect=https://b01.example.com/old-pricing model=test-model session=S1
            - file=b02.json provider=b02 field=subscriptionPricingUrl current=https://b02.example.com/price region=us website=https://b02.example.com redirect=- model=test-model session=S2
            - file=b03.json provider=b03 field=subscriptionPricingUrl current=https://b03.example.com/price region=us website=https://b03.example.com redirect=- model=test-model session=S3
            - file=b04.json provider=b04 field=subscriptionPricingUrl current=https://b04.example.com/price region=us website=https://b04.example.com redirect=- model=test-model session=S4
            - file=b05.json provider=b05 field=subscriptionPricingUrl current=https://b05.example.com/price region=us website=https://b05.example.com redirect=- model=test-model session=S5
            DB 4
            - call=1 file=b01.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=b02.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=b03.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=b04.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=1 file=b05.json code=UrlNotFound field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=2 file=b01.json code=UrlNotFound field=subscriptionPricingUrl current=https://b01.example.com/price suggested=https://b01.example.com/pricing reason=canonical pricing page severity=urlFix status=Pending saved=set
            - call=2 file=b02.json code=UrlNotFound field=subscriptionPricingUrl current=https://b02.example.com/price suggested=- reason=Subscription plans are not offered severity=urlFix status=Pending saved=set
            - call=2 file=b03.json code=UrlNotFound field=subscriptionPricingUrl current=https://b03.example.com/price suggested=- reason=search returned nothing severity=urlFix status=Failed saved=set
            - call=2 file=b04.json code=UrlNotFound field=subscriptionPricingUrl current=https://b04.example.com/price suggested=- reason=search returned nothing severity=urlFix status=Failed saved=set
            - call=2 file=b05.json code=UrlNotFound field=subscriptionPricingUrl current=https://b05.example.com/price suggested=- reason=search returned nothing severity=urlFix status=Failed saved=set
            - call=3 file=b01.json code=PricingUrlRedirected field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=4 file=b01.json code=PricingUrlRedirected field=subscriptionPricingUrl current=https://b01.example.com/price suggested=https://b01.example.com/pricing reason=canonical pricing page severity=urlFix status=Pending saved=set
            SAVES 6
            TOKENS 2
            - model=test-model provider=test-provider op=AiUrlFix prompt=6 completion=8 cost=0 source=Unknown created=set
            - model=test-model provider=test-provider op=AiUrlFix prompt=0 completion=0 cost=0 source=Unknown created=set
            ISSUES 6
            - file=b01.json field=subscriptionPricingUrl status=Pending suggested=https://b01.example.com/pricing reason=canonical pricing page severity=urlFix noValue=False attempted=set
            - file=b02.json field=subscriptionPricingUrl status=Pending suggested=- reason=Subscription plans are not offered severity=urlFix noValue=False attempted=set
            - file=b03.json field=subscriptionPricingUrl status=Failed suggested=- reason=search returned nothing severity=urlFix noValue=False attempted=set
            - file=b04.json field=subscriptionPricingUrl status=Failed suggested=- reason=search returned nothing severity=urlFix noValue=False attempted=set
            - file=b05.json field=subscriptionPricingUrl status=Failed suggested=- reason=search returned nothing severity=urlFix noValue=False attempted=set
            - file=b01.json field=subscriptionPricingUrl status=Pending suggested=https://b01.example.com/pricing reason=canonical pricing page severity=urlFix noValue=False attempted=set
            RESULT
            - suggestions=2 failed=3 prompt=6 completion=8 cost=0 label='' model=test-model available=True
            SUGGESTIONS 2
            - provider=b01 display=b01.json field=subscriptionPricingUrl suggested=https://b01.example.com/pricing reason=canonical pricing page severity=urlFix current=https://b01.example.com/price
            - provider=b02 display=b02.json field=subscriptionPricingUrl suggested=- reason=Subscription plans are not offered severity=urlFix current=https://b02.example.com/price
            """);
    }

    [Fact]
    public async Task NotApplicableSentinel_IsAcceptedWithoutDomainChecks()
    {
        var fixture = new Fixture();
        fixture.Provider("deepinfra");
        fixture.Issue(
            "deepinfra.json",
            ProviderJsonFields.LoginUrl,
            ValidationIssueCodes.LoginUrlNotLoginPage,
            current: "https://console.example.com/login");
        fixture.Answer(
            "deepinfra.json",
            ProviderJsonFields.LoginUrl,
            Result(ProviderJsonFields.NotApplicable, "Self-hosted console authenticates at the website"));

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 6
            - Processing 1 issue(s) in 1 batch(es)...
            -   Batch 1/1 (1 URLs) — starting research...
            -   Batch 1: deepinfra.json (loginUrl)
            -     Researching deepinfra.json (loginUrl)...
            -     ✓ deepinfra.json: - (suggested)
            -   ✓ Batch 1: 1 suggestion(s) found (<elapsed>)
            RESEARCH 1
            - file=deepinfra.json provider=deepinfra field=loginUrl current=https://console.example.com/login region=us website=https://deepinfra.example.com redirect=- model=test-model session=S1
            DB 2
            - call=1 file=deepinfra.json code=LoginUrlNotLoginPage field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=2 file=deepinfra.json code=LoginUrlNotLoginPage field=loginUrl current=https://console.example.com/login suggested=- reason=Self-hosted console authenticates at the website severity=urlFix status=Pending saved=set
            SAVES 3
            TOKENS 1
            - model=test-model provider=test-provider op=AiUrlFix prompt=0 completion=0 cost=0 source=Unknown created=set
            ISSUES 1
            - file=deepinfra.json field=loginUrl status=Pending suggested=- reason=Self-hosted console authenticates at the website severity=urlFix noValue=False attempted=set
            RESULT
            - suggestions=1 failed=0 prompt=0 completion=0 cost=0 label='' model=test-model available=True
            SUGGESTIONS 1
            - provider=deepinfra display=deepinfra.json field=loginUrl suggested=- reason=Self-hosted console authenticates at the website severity=urlFix current=https://console.example.com/login
            """);
    }

    [Fact]
    public async Task NumericSuggestion_IsAcceptedWithoutVerification()
    {
        var fixture = new Fixture();
        fixture.Provider("deepinfra");
        fixture.Issue(
            "deepinfra.json",
            ProviderJsonFields.MinModelCount,
            ValidationIssueCodes.MinModelCountInvalid,
            current: "0");
        fixture.Answer(
            "deepinfra.json",
            ProviderJsonFields.MinModelCount,
            Result("12", "Catalog lists twelve models", prompt: 7, completion: 8));

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 6
            - Processing 1 issue(s) in 1 batch(es)...
            -   Batch 1/1 (1 URLs) — starting research...
            -   Batch 1: deepinfra.json (minModelCount)
            -     Researching deepinfra.json (minModelCount)...
            -     ✓ deepinfra.json: 12 (suggested)
            -   ✓ Batch 1: 1 suggestion(s) found (<elapsed>)
            RESEARCH 1
            - file=deepinfra.json provider=deepinfra field=minModelCount current=0 region=us website=https://deepinfra.example.com redirect=- model=test-model session=S1
            DB 2
            - call=1 file=deepinfra.json code=MinModelCountInvalid field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=2 file=deepinfra.json code=MinModelCountInvalid field=minModelCount current=0 suggested=12 reason=Catalog lists twelve models severity=urlFix status=Pending saved=set
            SAVES 3
            TOKENS 1
            - model=test-model provider=test-provider op=AiUrlFix prompt=7 completion=8 cost=0 source=Unknown created=set
            ISSUES 1
            - file=deepinfra.json field=minModelCount status=Pending suggested=12 reason=Catalog lists twelve models severity=urlFix noValue=False attempted=set
            RESULT
            - suggestions=1 failed=0 prompt=7 completion=8 cost=0 label='' model=test-model available=True
            SUGGESTIONS 1
            - provider=deepinfra display=deepinfra.json field=minModelCount suggested=12 reason=Catalog lists twelve models severity=urlFix current=0
            """);
    }

    [Fact]
    public async Task ServiceRetired_ReportsErrorAndStopsWithoutResearch()
    {
        // Arrange — a ServiceRetired issue should be reported as an error (not a suggestion),
        // increment the failed count, and stop processing for that provider without any research.
        var fixture = new Fixture();
        fixture.Provider("github-models");
        fixture.Issue(
            "github-models.json",
            ProviderJsonFields.ApiPricingUrl,
            ValidationIssueCodes.ServiceRetired,
            current: "https://docs.github.com/en/github-models");

        await AssertTranscriptAsync(
            fixture,
            """
            PROGRESS 6
            - Processing 1 issue(s) in 1 batch(es)...
            -   Batch 1/1 (1 URLs) — starting research...
            -   Batch 1: github-models.json (apiPricingUrl)
            -     Researching github-models.json (apiPricingUrl)...
            -     ✖ github-models.json: service retired — apiPricingUrl cannot be repaired
            -   ✓ Batch 1: 0 suggestion(s) found, 1 failed (<elapsed>)
            RESEARCH 0
            DB 2
            - call=1 file=github-models.json code=ServiceRetired field=- current=- suggested=- reason=- severity=- status=Pending saved=set
            - call=2 file=github-models.json code=ServiceRetired field=apiPricingUrl current=https://docs.github.com/en/github-models suggested=- reason=Service retired/deprecated severity=urlFix status=Failed saved=set
            SAVES 3
            TOKENS 1
            - model=test-model provider=test-provider op=AiUrlFix prompt=0 completion=0 cost=0 source=Unknown created=set
            ISSUES 1
            - file=github-models.json field=apiPricingUrl status=Failed suggested=- reason=Service retired/deprecated severity=urlFix noValue=False attempted=set
            RESULT
            - suggestions=0 failed=1 prompt=0 completion=0 cost=0 label='' model=test-model available=True
            SUGGESTIONS 0
            """);
    }

    private static AiSuggestion Regional(string? suggestedValue)
        => new()
        {
            ProviderId = "deepinfra",
            DisplayName = "deepinfra.json",
            Field = ProviderJsonFields.RegionalEndpoints,
            CurrentValue = null,
            SuggestedValue = suggestedValue,
            Reason = "Regional endpoints documented alongside the answer",
            Severity = "urlFix"
        };

    private static UrlResearchResult Result(
        string? suggestedValue,
        string reason,
        bool success = true,
        int prompt = 0,
        int completion = 0,
        List<AiSuggestion>? suggestions = null)
        => new(suggestedValue, reason, success, prompt, completion, [], suggestions ?? []);

    private static async Task AssertTranscriptAsync(Fixture fixture, string golden)
    {
        var actual = await fixture.RunAsync();
        actual.Should().Be(NormalizeLineEndings(golden), "the URL-fix transcript is frozen");
    }

    private static string NormalizeLineEndings(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>
    /// Assembles the eleven dependencies <see cref="AiUrlFixService"/> needs today, scripts the
    /// research answer per (file, field) pair, and renders the whole run as one transcript.
    /// </summary>
    private sealed class Fixture
    {
        private readonly Mock<IAiFixConfiguration> _config = new();
        private readonly Mock<IValidationIssueRepository> _issueRepo = new();
        private readonly Mock<ITokenUsageRepository> _tokenRepo = new();
        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly Mock<IProviderCatalog> _catalog = new();
        private readonly Mock<IUrlResearchService> _research = new();
        private readonly Mock<IGeoRegionProvider> _regionProvider = new();
        private readonly Mock<IWebContentFetcher> _fetcher = new();
        private readonly Mock<IPricingPageVerifier> _pricingVerifier = new();
        private readonly Dictionary<string, Func<ResearchContext, UrlResearchResult>> _answers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ProbeResponse> _probes = new(StringComparer.Ordinal);
        private readonly Dictionary<ResearchSession, string> _sessionIds = [];
        private readonly List<ValidationIssue> _issues = [];
        private readonly List<ProviderDefinition> _providers = [];
        private readonly List<string> _progress = [];
        private readonly List<ResearchContext> _researchCalls = [];
        private readonly List<List<ValidationIssueEntry>> _dbBatches = [];
        private readonly List<TokenUsageEntry> _tokenRows = [];
        private int _saves;
        private int _sessionCounter;

        public Fixture() => AiSetup();

        public Fixture AiSetup(
            bool apiKey = true,
            bool providerSelection = true,
            bool modelConfigured = true,
            string? primaryModel = "test-model",
            string? fallbackModel = "fallback-model",
            string? selectedProviderId = "test-provider")
        {
            _config.SetupGet(c => c.HasApiKey).Returns(apiKey);
            _config.SetupGet(c => c.HasProviderSelection).Returns(providerSelection);
            _config.SetupGet(c => c.IsModelConfigured).Returns(modelConfigured);
            _config.SetupGet(c => c.PrimaryModel).Returns(primaryModel);
            _config.SetupGet(c => c.FallbackModel).Returns(fallbackModel);
            _config.SetupGet(c => c.SelectedProviderId).Returns(selectedProviderId);
            return this;
        }

        public Fixture Issue(
            string fileName,
            string? field,
            string code = ValidationIssueCodes.UrlError,
            string? current = null,
            string? message = null,
            string? redirect = null)
        {
            _issues.Add(
                new ValidationIssue(fileName, code, message ?? $"Field '{field}' could not be verified.")
                {
                    Field = field,
                    CurrentValue = current,
                    RedirectTargetUrl = redirect
                });
            return this;
        }

        public Fixture Provider(string id)
        {
            _providers.Add(
                new ProviderDefinition
                {
                    Id = id,
                    DisplayName = id,
                    BaseUrl = $"https://api.{id}.example.com/v1",
                    Protocol = EProviderProtocol.OpenAICompatible
                });
            return this;
        }

        public Fixture Answer(string fileName, string? field, UrlResearchResult result)
            => AnswerWhen(fileName, field, _ => result);

        public Fixture AnswerWhen(string fileName, string? field, Func<ResearchContext, UrlResearchResult> produce)
        {
            _answers[Key(fileName, field)] = produce;
            return this;
        }

        public Fixture Throws(string fileName, string? field, Func<Exception> produce)
            => AnswerWhen(fileName, field, _ => throw produce());

        public Fixture Probe(string url, string contentType, HttpStatusCode status = HttpStatusCode.OK)
        {
            _probes[url] = new ProbeResponse(contentType, status);
            return this;
        }

        public async Task<string> RunAsync()
        {
            var service = CreateService();
            var result = await service.RunAsync(
                _issues,
                "test-model",
                new RecordingProgress(_progress),
                CancellationToken.None);
            return Render(service, result);
        }

        private static string Key(string fileName, string? field) => $"{fileName}|{field}";

        private static string Text(string? value)
            => value is null ? "-" : value.Length == 0 ? "''" : value;

        private static string StatusName(int status)
            => Enum.IsDefined((IssueSuggestionStatus)status)
                   ? ((IssueSuggestionStatus)status).ToString()
                   : $"undefined({status})";

        private AiUrlFixService CreateService()
        {
            _catalog.SetupGet(c => c.All).Returns(() => _providers);
            _catalog.Setup(c => c.Get(It.IsAny<string>())).Returns<string>(Find);
            _catalog.Setup(c => c.GetResearchMetadata(It.IsAny<string>()))
                .Returns<string>(id => Find(id) is null
                                          ? null
                                          : new ProviderResearchMetadata { Website = $"https://{id}.example.com" });

            _regionProvider.Setup(r => r.DetectRegionAsync(It.IsAny<CancellationToken>())).ReturnsAsync("us");

            _research.Setup(r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()))
                .Returns<ResearchContext, CancellationToken>((context, _) =>
                {
                    _researchCalls.Add(context);
                    var key = Key(context.Issue.FileName, context.Field);
                    return _answers.TryGetValue(key, out var answer)
                               ? Task.FromResult(answer(context))
                               : Task.FromResult(new UrlResearchResult(
                                     null,
                                     $"UNSCRIPTED RESEARCH ANSWER {key}",
                                     false,
                                     0,
                                     0,
                                     [],
                                     []));
                });

            _issueRepo.Setup(r => r.UpdateSuggestionsAsync(It.IsAny<IReadOnlyList<ValidationIssueEntry>>()))
                .Callback<IReadOnlyList<ValidationIssueEntry>>(entries => _dbBatches.Add([.. entries]))
                .Returns(Task.CompletedTask);

            _tokenRepo.Setup(t => t.AddAsync(It.IsAny<TokenUsageEntry>()))
                .Callback<TokenUsageEntry>(entry => _tokenRows.Add(entry))
                .Returns(Task.CompletedTask);

            _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Callback(() => _saves++)
                .ReturnsAsync(0);

            // Default: pricing verifier accepts any URL (characterization tests trust the research agent)
            _pricingVerifier.Setup(v => v.VerifyPricingUrlAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<ScraperTool.Services.Validation.IValidationIssueSink>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((EPricingContentVerdict.HasPricing, "pricing content found"));

            return new AiUrlFixService(
                _config.Object,
                new ModelPriceResolver(new AppSettings()),
                _issueRepo.Object,
                _tokenRepo.Object,
                _uow.Object,
                _catalog.Object,
                _research.Object,
                _regionProvider.Object,
                _fetcher.Object,
                new ProviderResearchCache(),
                new HttpClient(new StubProbeHandler(_probes), disposeHandler: false),
                _pricingVerifier.Object);
        }

        private ProviderDefinition? Find(string providerId)
            => _providers.Find(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));

        private string Render(AiUrlFixService service, AiUrlFixRunResult result)
        {
            var lines = new List<string>();

            lines.Add($"PROGRESS {_progress.Count}");
            foreach (var message in _progress)
                lines.Add($"- {ElapsedRegex().Replace(message, "(<elapsed>)")}");

            lines.Add($"RESEARCH {_researchCalls.Count}");
            foreach (var context in _researchCalls)
            {
                lines.Add(
                    $"- file={context.Issue.FileName} provider={context.ProviderId} field={Text(context.Field)} "
                    + $"current={Text(context.CurrentValue)} region={context.Region} website={Text(context.Website)} "
                    + $"redirect={Text(context.RedirectTargetUrl)} model={context.ModelName} "
                    + $"session={SessionId(context)}");
            }

            lines.Add($"DB {_dbBatches.Count}");
            for (var call = 0; call < _dbBatches.Count; call++)
            {
                foreach (var entry in _dbBatches[call])
                {
                    lines.Add(
                        $"- call={call + 1} file={entry.FileName} code={entry.Code} field={Text(entry.Field)} "
                        + $"current={Text(entry.CurrentValue)} suggested={Text(entry.SuggestedValue)} "
                        + $"reason={Text(entry.SuggestionReason)} severity={Text(entry.SuggestionSeverity)} "
                        + $"status={entry.SuggestionStatus} saved={(entry.SavedAt == default ? "unset" : "set")}");
                }
            }

            lines.Add($"SAVES {_saves}");

            lines.Add($"TOKENS {_tokenRows.Count}");
            foreach (var row in _tokenRows)
            {
                lines.Add(
                    $"- model={row.ModelName} provider={row.ProviderName} op={row.Operation} "
                    + $"prompt={row.PromptTokens} completion={row.CompletionTokens} "
                    + $"cost={row.Cost.ToString(CultureInfo.InvariantCulture)} source={Text(row.CostSource)} "
                    + $"created={(row.CreatedAt == default ? "unset" : "set")}");
            }

            lines.Add($"ISSUES {_issues.Count}");
            foreach (var issue in _issues)
            {
                lines.Add(
                    $"- file={issue.FileName} field={Text(issue.Field)} status={StatusName(issue.SuggestionStatus)} "
                    + $"suggested={Text(issue.SuggestedValue)} reason={Text(issue.SuggestionReason)} "
                    + $"severity={Text(issue.SuggestionSeverity)} noValue={issue.ResearchCompletedWithoutValue} "
                    + $"attempted={(issue.AiAttemptedAt is null ? "unset" : "set")}");
            }

            lines.Add("RESULT");
            lines.Add(
                $"- suggestions={result.TotalSuggestions} failed={result.FailedCount} "
                + $"prompt={result.TotalPromptTokens} completion={result.TotalCompletionTokens} "
                + $"cost={result.TotalCost.ToString(CultureInfo.InvariantCulture)} label='{result.CostSourceLabel}' "
                + $"model={result.UsedModelName} available={service.IsAvailable}");

            lines.Add($"SUGGESTIONS {result.Suggestions.Count}");
            foreach (var suggestion in result.Suggestions)
            {
                lines.Add(
                    $"- provider={suggestion.ProviderId} display={suggestion.DisplayName} field={suggestion.Field} "
                    + $"suggested={Text(suggestion.SuggestedValue)} reason={Text(suggestion.Reason)} "
                    + $"severity={suggestion.Severity} current={Text(suggestion.CurrentValue)}");
            }

            return string.Join('\n', lines);
        }

        private string SessionId(ResearchContext context)
        {
            if (context.Session is not { } session)
                return "none";

            if (!_sessionIds.TryGetValue(session, out var id))
            {
                id = $"S{++_sessionCounter}";
                _sessionIds[session] = id;
            }

            return id;
        }
    }

    private sealed record ProbeResponse(string ContentType, HttpStatusCode Status);

    private sealed class RecordingProgress(List<string> sink) : IProgress<AiUrlFixProgress>
    {
        public void Report(AiUrlFixProgress value) => sink.Add(value.Message);
    }

    private sealed class StubProbeHandler(Dictionary<string, ProbeResponse> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            var scripted = responses.TryGetValue(url, out var response) ? response : null;
            var message = new HttpResponseMessage(scripted?.Status ?? HttpStatusCode.NotImplemented)
            {
                Content = new ByteArrayContent("<body/>"u8.ToArray())
            };
            message.Content.Headers.ContentType =
                new MediaTypeHeaderValue(scripted?.ContentType ?? "text/plain");
            return Task.FromResult(message);
        }
    }
}
