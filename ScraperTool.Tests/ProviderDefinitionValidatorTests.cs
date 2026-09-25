using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using FluentAssertions;

using Moq;

using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.Services.Validation;
using ScraperTool.Services.Validation.Checks;
using ScraperTool.Tests.Validation;

using WebTools.NET.Abstractions;

namespace ScraperTool.Tests;

public class ProviderDefinitionValidatorTests
{
    [Fact]
    public async Task ValidateFileAsync_SanitizesExceptionMessages()
    {
        var fetcherMock = CreateFetcherMock();

        // The url reachability checker throws — this is what ValidateFileAsync actually uses
        var urlCheckerMock = new Mock<IUrlReachabilityChecker>();
        urlCheckerMock.Setup(s => s.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Internal server error"));

        var validator = CreateValidator(fetcherMock, urlCheckerMock: urlCheckerMock);

        var json = SerializeManifest(
            new AIProviderConnect.Models.ProviderDefinition
                {
                    Id = "test",
                    DisplayName = "Test",
                    Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                    BaseUrl = "https://api.example.com/v1"
                },
            new AIProviderConnect.Models.ProviderResearchMetadata
                {
                    Website = "https://example.com",
                    LoginUrl = "https://example.com/login",
                    ApiPricingUrl = "https://example.com/pricing",
                    DocumentationUrl = "https://example.com/docs",
                    MinModelCount = 1
                });

        var tempFile = Path.Combine(Path.GetTempPath(), "validator-tests-sanitize.json");
        File.WriteAllText(tempFile, json);

        try
        {
            var issues = await validator.ValidateFileAsync(tempFile);
            issues.Should().Contain(i => i.Code == "UrlError");
            issues.Should().AllSatisfy(i => i.Message.Should().NotContain("Internal server error"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ValidateFileSync_ReturnsIssues_ForMissingFields()
    {
        // ValidateFileSync delegates to _schemaValidator.ValidateSchema.
        // An empty {} JSON is missing required fields — the schema validator should report them.
        var schemaValidatorMock = new Mock<IProviderSchemaValidator>();
        schemaValidatorMock.Setup(s => s.ValidateSchema(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(
                (string json, string fileName) =>
                {
                    // Simulate what a real schema validator returns for empty JSON
                    if (json.Trim() == "{}")
                    {
                        return new List<ValidationIssue>
                        {
                            new(fileName, "MissingRequired", "Field 'id' is required."),
                            new(fileName, "MissingRequired", "Field 'displayName' is required."),
                            new(fileName, "MissingRequired", "Field 'baseUrl' is required.")
                        };
                    }
                    return [];
                });

        var validator = CreateValidator(schemaValidatorMock: schemaValidatorMock);

        var tempFile = Path.Combine(Path.GetTempPath(), "validator-tests-bad.json");
        File.WriteAllText(tempFile, "{}");

        try
        {
            var issues = validator.ValidateFileSync(tempFile);
            issues.Should().NotBeEmpty();
            issues.Should().Contain(i => i.Code == "MissingRequired");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ValidateFileSync_ReturnsNoIssues_ForValidProvider()
    {
        var validator = CreateValidator();

        var json = SerializeManifest(
            new AIProviderConnect.Models.ProviderDefinition
                {
                    Id = "test",
                    DisplayName = "Test",
                    Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                    BaseUrl = "https://api.example.com/v1"
                },
            new AIProviderConnect.Models.ProviderResearchMetadata
                {
                    Website = "https://example.com",
                    LoginUrl = "https://example.com/login",
                    ApiPricingUrl = "https://example.com/pricing",
                    DocumentationUrl = "https://example.com/docs",
                    MinModelCount = 1
                });

        var tempFile = Path.Combine(Path.GetTempPath(), "validator-tests-good.json");
        File.WriteAllText(tempFile, json);

        try
        {
            var issues = validator.ValidateFileSync(tempFile);
            issues.Should().BeEmpty();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void SchemaValidator_MinModelCountZero_WithoutDynamicCatalog_FlagsInvalid()
    {
        var validator = new ScraperTool.Services.Validation.ProviderSchemaValidator();

        var json = SerializeManifest(
            new AIProviderConnect.Models.ProviderDefinition
                {
                    Id = "test",
                    DisplayName = "Test",
                    Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                    BaseUrl = "https://test.example.com/v1"
                },
            new AIProviderConnect.Models.ProviderResearchMetadata
                {
                    Website = "https://test.example.com",
                    LoginUrl = "https://test.example.com/login",
                    ApiPricingUrl = "https://test.example.com/pricing",
                    DocumentationUrl = "https://test.example.com/docs",
                    MinModelCount = 0
                });

        var issues = validator.ValidateSchema(json, "test.json");
        issues.Should().Contain(i => i.Code == ValidationIssueCodes.MinModelCountInvalid);
    }

    [Fact]
    public void SchemaValidator_MinModelCountZero_WithDynamicCatalog_NoIssue()
    {
        var validator = new ScraperTool.Services.Validation.ProviderSchemaValidator();

        var json = SerializeManifest(
            new AIProviderConnect.Models.ProviderDefinition
                {
                    Id = "test",
                    DisplayName = "Test",
                    Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                    BaseUrl = "https://test.example.com/v1"
                },
            new AIProviderConnect.Models.ProviderResearchMetadata
                {
                    Website = "https://test.example.com",
                    LoginUrl = "https://test.example.com/login",
                    ApiPricingUrl = "https://test.example.com/pricing",
                    DocumentationUrl = "https://test.example.com/docs",
                    MinModelCount = 0,
                    IsDynamicModelCatalog = true
                });

        var issues = validator.ValidateSchema(json, "test.json");
        issues.Should().NotContain(i => i.Code == ValidationIssueCodes.MinModelCountInvalid);
    }

    [Fact]
    public void SchemaValidator_SubscriptionPricingUrlDash_ForNonSelfHostedProvider_NoIssue()
    {
        // A subscription is optional: a pay-as-you-go or free provider (any category) may
        // legitimately have no plans page, so '-' must not be flagged as an invalid URL.
        var validator = new ScraperTool.Services.Validation.ProviderSchemaValidator();

        var json = SerializeManifest(
            new AIProviderConnect.Models.ProviderDefinition
                {
                    Id = "test-provider",
                    DisplayName = "Test",
                    Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                    BaseUrl = "https://test.example.com/v1",
                    Category = "ResearchPlatform"
                },
            new AIProviderConnect.Models.ProviderResearchMetadata
                {
                    Website = "https://test.example.com",
                    LoginUrl = "https://test.example.com/login",
                    ApiPricingUrl = "https://test.example.com/pricing",
                    SubscriptionPricingUrl = ProviderJsonFields.NotApplicable,
                    DocumentationUrl = "https://test.example.com/docs",
                    MinModelCount = 2
                });

        var issues = validator.ValidateSchema(json, "test.json");
        issues.Should().NotContain(
            i => i.Code == ValidationIssueCodes.InvalidUrl
                 && i.Message.Contains("subscriptionPricingUrl"));
    }

    [Fact]
    public void SchemaValidator_ApiPricingUrlDash_ForNonSelfHostedProvider_FlagsInvalid()
    {
        // Guard the boundary: apiPricingUrl '-' stays a self-hosted-only exemption, so a
        // non-self-hosted provider must still be flagged.
        var validator = new ScraperTool.Services.Validation.ProviderSchemaValidator();

        var json = SerializeManifest(
            new AIProviderConnect.Models.ProviderDefinition
                {
                    Id = "test-provider",
                    DisplayName = "Test",
                    Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                    BaseUrl = "https://test.example.com/v1",
                    Category = "ResearchPlatform"
                },
            new AIProviderConnect.Models.ProviderResearchMetadata
                {
                    Website = "https://test.example.com",
                    LoginUrl = "https://test.example.com/login",
                    ApiPricingUrl = ProviderJsonFields.NotApplicable,
                    SubscriptionPricingUrl = "https://test.example.com/plans",
                    DocumentationUrl = "https://test.example.com/docs",
                    MinModelCount = 2
                });

        var issues = validator.ValidateSchema(json, "test.json");
        issues.Should().Contain(
            i => i.Code == ValidationIssueCodes.InvalidUrl
                 && i.Message.Contains("apiPricingUrl"));
    }

    [Fact]
    public async Task ValidateFileAsync_BaseUrlServesAnHtmlPage_FlagsItAsNotAnApiEndpoint()
    {
        // A pricing or documentation page stored as the base address answers with a web page.
        // Every downstream call through that base fails, so a 200 is not enough to certify it.
        var handler = new StubApiProbeHandler(HttpStatusCode.OK, "text/html", "<html><body>Pricing plans</body></html>");

        var issues = await ValidateProviderAsync("https://test.example.com/zen/go", handler);

        var issue = issues.Single(i => i.Code == ValidationIssueCodes.BaseUrlNotApiEndpoint);
        issue.Message.Should().Contain("returns an HTML page");
        issue.Field.Should().Be("baseUrl");
        handler.RequestedUrls.Should().Contain("https://test.example.com/zen/go");
    }

    [Fact]
    public async Task ValidateFileAsync_BaseUrlAnswersSuccessWithAnErrorPage_FlagsItAndQuotesTheBody()
    {
        // The soft 404 the reported run sailed through: the gateway served HTTP 200 with a plain
        // "Not Found" body for a base that composes to a documentation route. Nothing can be
        // counted or completed out of that body, and certifying the address as reachable told
        // the research run to stop looking for the real one.
        var handler = new StubApiProbeHandler(HttpStatusCode.OK, "text/plain", "Not Found");

        var issues = await ValidateProviderAsync("https://test.example.com/zen/go", handler);

        var issue = issues.Single(i => i.Code == ValidationIssueCodes.BaseUrlNotApiEndpoint);
        issue.Message.Should().Contain("\"Not Found\"");
        issue.Field.Should().Be("baseUrl");
    }

    [Fact]
    public async Task ValidateFileAsync_BaseUrlAnswersJson_IsNotFlagged()
    {
        var handler = new StubApiProbeHandler(HttpStatusCode.OK, "application/json", """{"error":"unauthorized"}""");

        var issues = await ValidateProviderAsync("https://api.test.example.com/v1", handler);

        issues.Should().NotContain(i => i.Code == ValidationIssueCodes.BaseUrlNotApiEndpoint);
    }

    [Fact]
    public async Task ValidateFileAsync_BaseUrlAnswersJsonWithoutAJsonContentType_IsNotFlagged()
    {
        // Plenty of real APIs answer with a bare "application/octet-stream" or none at all.
        // The body decides, and a body that reads as data is a base that works.
        var handler = new StubApiProbeHandler(HttpStatusCode.OK, "text/plain", """{"data":[{"id":"m1"}]}""");

        var issues = await ValidateProviderAsync("https://api.test.example.com/v1", handler);

        issues.Should().NotContain(i => i.Code == ValidationIssueCodes.BaseUrlNotApiEndpoint);
    }

    [Fact]
    public async Task ValidateFileAsync_BaseUrlRootRejectsTheRequest_IsNotFlagged()
    {
        // GETting an API root 404s for perfectly valid bases — the routes live below it. Measured
        // over the whole catalogue this is the common case, not the exception, so rejecting on the
        // root reply alone would throw out correct entries.
        var handler = new StubApiProbeHandler(
            HttpStatusCode.NotFound,
            "text/html",
            "<html><body>404</body></html>");

        var issues = await ValidateProviderAsync("https://api.test.example.com/v1", handler);

        issues.Should().NotContain(i => i.Code == ValidationIssueCodes.BaseUrlNotApiEndpoint);
    }

    [Fact]
    public async Task ValidateFileAsync_BaseUrlProbeSaysNothing_ReportsThatInsteadOfCertifyingTheValue()
    {
        // The abstention used to be reported as "URL is reachable", wording that claims a
        // verification which never happened. It raises no finding — nothing in the catalogue
        // reaches this verdict, so a finding would be machinery for a case that does not occur —
        // but the report has to say the address could not be evaluated.
        var handler = new StubApiProbeHandler(
            HttpStatusCode.NotFound,
            "text/html",
            "<html><body>404</body></html>");
        var progress = new RecordingProgress();

        await ValidateProviderAsync("https://api.test.example.com/v1", handler, progress);

        var baseUrlReports = progress.Reports.Where(p => p.Field == "baseUrl").ToList();

        baseUrlReports.Should().Contain(p =>
            p.Stage == ValidationStage.CheckPassed
            && (p.ResultMessage ?? string.Empty).Contains(
                "could not be evaluated (answers 404 on its root) — stored value kept"));
        baseUrlReports.Should().NotContain(p => p.ResultMessage == "URL is reachable");
    }

    [Fact]
    public async Task ValidateFileAsync_BaseUrlServesAnEmptyBody_IsNotFlagged()
    {
        // An empty body says nothing either way — flagging it would reject bases that answer a
        // request with no content.
        var handler = new StubApiProbeHandler(HttpStatusCode.OK, "text/plain", "");
        var progress = new RecordingProgress();

        var issues = await ValidateProviderAsync(
            "https://api.test.example.com/v1", handler, progress);

        issues.Should().NotContain(i => i.Code == ValidationIssueCodes.BaseUrlNotApiEndpoint);
        progress.Reports.Should().Contain(p =>
            p.Field == "baseUrl"
            && (p.ResultMessage ?? string.Empty).Contains("with no body"));
    }

    [Fact]
    public async Task ValidateFileAsync_BaseUrlAnswersAnAuthGatedPlainTextBody_IsRecognisedAsApi()
    {
        // The probe recognises plain-text auth-gate messages ("You must provide a valid API key")
        // as evidence of a real API endpoint that requires authentication, rather than flagging
        // the URL as "not an API". This prevents false positives for auth-gated bases like
        // Blablador that answer HTTP 200 with a plain-text key requirement instead of 401/403.
        var handler = new StubApiProbeHandler(
            HttpStatusCode.OK,
            "text/plain",
            "You must provide a valid API key. Obtain one from https://test.example.com");

        var issues = await ValidateProviderAsync("https://api.test.example.com/v1/", handler);

        issues.Should().NotContain(i => i.Code == ValidationIssueCodes.BaseUrlNotApiEndpoint);
    }

    [Fact]
    public async Task ValidateFileAsync_SelfHostedProviderCarriesLoginUrl_RequiresTheNotApplicableMarker()
    {
        // A sign-in page belonging to wherever the project is hosted is not a login for a
        // self-hosted provider — there is no hosted account to sign in to. Reachability could not
        // tell that, and reported the field as verified.
        var progress = new RecordingProgress();

        var issues = await ValidateAsync(
            new AIProviderConnect.Models.ProviderDefinition
                {
                    Id = "test",
                    DisplayName = "Test",
                    Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                    BaseUrl = "http://127.0.0.1:5000/v1/",
                    Category = "SelfHosted"
                },
            new AIProviderConnect.Models.ProviderResearchMetadata
                {
                    Website = "https://test.example.com",
                    LoginUrl = "https://github.test.example.com/login?return_to=https%3A%2F%2Ftest.example.com",
                    ApiPricingUrl = ProviderJsonFields.NotApplicable,
                    SubscriptionPricingUrl = ProviderJsonFields.NotApplicable,
                    DocumentationUrl = "https://test.example.com/docs",
                    IsDynamicModelCatalog = true
                },
            progress: progress);

        issues.Should().Contain(i =>
            i.Code == ValidationIssueCodes.LoginUrlNotApplicableForSelfHosted
            && i.Field == "loginUrl");

        // The field must not also be probed — that is what printed a success over the finding.
        progress.Reports.Should().NotContain(p =>
            p.Field == "loginUrl" && p.Stage == ValidationStage.CheckingUrl);
    }

    [Fact]
    public async Task ValidateFileAsync_SelfHostedProviderMarksLoginUrlNotApplicable_KeepsItValid()
    {
        var progress = new RecordingProgress();

        var issues = await ValidateAsync(
            new AIProviderConnect.Models.ProviderDefinition
                {
                    Id = "test",
                    DisplayName = "Test",
                    Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                    BaseUrl = "http://127.0.0.1:5000/v1/",
                    Category = "SelfHosted"
                },
            new AIProviderConnect.Models.ProviderResearchMetadata
                {
                    Website = "https://test.example.com",
                    LoginUrl = ProviderJsonFields.NotApplicable,
                    ApiPricingUrl = ProviderJsonFields.NotApplicable,
                    SubscriptionPricingUrl = ProviderJsonFields.NotApplicable,
                    DocumentationUrl = "https://test.example.com/docs",
                    IsDynamicModelCatalog = true
                },
            progress: progress);

        issues.Should().NotContain(i =>
            i.Code == ValidationIssueCodes.LoginUrlNotApplicableForSelfHosted);
        progress.Reports.Should().Contain(p =>
            p.Field == "loginUrl"
            && p.Stage == ValidationStage.CheckPassed
            && (p.ResultMessage ?? string.Empty).Contains("N/A (self-hosted)"));
    }

    [Fact]
    public async Task ValidateFileAsync_SelfHostedProviderStoresRealSubscriptionUrl_MarksItNotApplicable()
    {
        // A self-hosted provider is a local application with no plans to subscribe to, so a
        // subscriptionPricingUrl carrying an address is a structural defect — not a URL to probe.
        var progress = new RecordingProgress();

        var issues = await ValidateAsync(
            new AIProviderConnect.Models.ProviderDefinition
                {
                    Id = "test",
                    DisplayName = "Test",
                    Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                    BaseUrl = "http://127.0.0.1:5000/v1/",
                    Category = "SelfHosted"
                },
            new AIProviderConnect.Models.ProviderResearchMetadata
                {
                    Website = "https://test.example.com",
                    LoginUrl = ProviderJsonFields.NotApplicable,
                    ApiPricingUrl = ProviderJsonFields.NotApplicable,
                    SubscriptionPricingUrl = "https://test.example.com/plans",
                    DocumentationUrl = "https://test.example.com/docs",
                    IsDynamicModelCatalog = true
                },
            progress: progress);

        issues.Should().Contain(i =>
            i.Code == ValidationIssueCodes.SubscriptionPricingUrlNotApplicableForSelfHosted
            && i.Field == "subscriptionPricingUrl");

        // The field must not also be probed — that would print a priced-plan verdict over the finding.
        progress.Reports.Should().NotContain(p =>
            p.Field == "subscriptionPricingUrl" && p.Stage == ValidationStage.CheckingUrl);
    }

    [Fact]
    public async Task ValidateFileAsync_SelfHostedProviderMarksSubscriptionPricingUrlNotApplicable_KeepsItValid()
    {
        var progress = new RecordingProgress();

        var issues = await ValidateAsync(
            new AIProviderConnect.Models.ProviderDefinition
                {
                    Id = "test",
                    DisplayName = "Test",
                    Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                    BaseUrl = "http://127.0.0.1:5000/v1/",
                    Category = "SelfHosted"
                },
            new AIProviderConnect.Models.ProviderResearchMetadata
                {
                    Website = "https://test.example.com",
                    LoginUrl = ProviderJsonFields.NotApplicable,
                    ApiPricingUrl = ProviderJsonFields.NotApplicable,
                    SubscriptionPricingUrl = ProviderJsonFields.NotApplicable,
                    DocumentationUrl = "https://test.example.com/docs",
                    IsDynamicModelCatalog = true
                },
            progress: progress);

        issues.Should().NotContain(i =>
            i.Code == ValidationIssueCodes.SubscriptionPricingUrlNotApplicableForSelfHosted);
        progress.Reports.Should().Contain(p =>
            p.Field == "subscriptionPricingUrl"
            && p.Stage == ValidationStage.CheckPassed
            && (p.ResultMessage ?? string.Empty).Contains("N/A (self-hosted)"));
    }

    [Fact]
    public async Task ValidateFileAsync_SelfHostedProviderStoresRealApiPricingUrl_MarksItNotApplicable()
    {
        // A self-hosted provider serves a local model with no hosted API pricing, so an
        // apiPricingUrl carrying an address is a structural defect — not a URL to probe.
        var progress = new RecordingProgress();

        var issues = await ValidateAsync(
            new AIProviderConnect.Models.ProviderDefinition
                {
                    Id = "test",
                    DisplayName = "Test",
                    Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                    BaseUrl = "http://127.0.0.1:5000/v1/",
                    Category = "SelfHosted"
                },
            new AIProviderConnect.Models.ProviderResearchMetadata
                {
                    Website = "https://test.example.com",
                    LoginUrl = ProviderJsonFields.NotApplicable,
                    ApiPricingUrl = "https://test.example.com/api-pricing",
                    SubscriptionPricingUrl = ProviderJsonFields.NotApplicable,
                    DocumentationUrl = "https://test.example.com/docs",
                    IsDynamicModelCatalog = true
                },
            progress: progress);

        issues.Should().Contain(i =>
            i.Code == ValidationIssueCodes.ApiPricingUrlNotApplicableForSelfHosted
            && i.Field == "apiPricingUrl");

        // The field must not also be probed — that would print a pricing verdict over the finding.
        progress.Reports.Should().NotContain(p =>
            p.Field == "apiPricingUrl" && p.Stage == ValidationStage.CheckingUrl);
    }

    [Fact]
    public async Task ValidateFileAsync_SelfHostedProviderMarksApiPricingUrlNotApplicable_KeepsItValid()
    {
        var progress = new RecordingProgress();

        var issues = await ValidateAsync(
            new AIProviderConnect.Models.ProviderDefinition
                {
                    Id = "test",
                    DisplayName = "Test",
                    Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                    BaseUrl = "http://127.0.0.1:5000/v1/",
                    Category = "SelfHosted"
                },
            new AIProviderConnect.Models.ProviderResearchMetadata
                {
                    Website = "https://test.example.com",
                    LoginUrl = ProviderJsonFields.NotApplicable,
                    ApiPricingUrl = ProviderJsonFields.NotApplicable,
                    SubscriptionPricingUrl = ProviderJsonFields.NotApplicable,
                    DocumentationUrl = "https://test.example.com/docs",
                    IsDynamicModelCatalog = true
                },
            progress: progress);

        issues.Should().NotContain(i =>
            i.Code == ValidationIssueCodes.ApiPricingUrlNotApplicableForSelfHosted);
        progress.Reports.Should().Contain(p =>
            p.Field == "apiPricingUrl"
            && p.Stage == ValidationStage.CheckPassed
            && (p.ResultMessage ?? string.Empty).Contains("N/A (self-hosted)"));
    }

    [Fact]
    public async Task ValidateFileAsync_LoginUrlOffersNoAuthenticationSurface_IsFlaggedAsNotLoginPage()
    {
        var analyzerMock = CreateContentAnalyzerMock();
        analyzerMock.Setup(s => s.AnalyzeLoginUrlAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((
                ELoginUrlVerdict.NotLoginPage,
                "no credential form, no authentication endpoint in the address, no sign-in link"));

        var (definition, research) = CreateHostedDefinition("https://test.example.com/docs/getting-started");
        var issues = await ValidateAsync(
            definition,
            research,
            contentAnalyzerMock: analyzerMock);

        var issue = issues.Single(i => i.Code == ValidationIssueCodes.LoginUrlNotLoginPage);
        issue.Field.Should().Be("loginUrl");
        issue.Message.Should().Contain("not an authentication surface");
    }

    [Fact]
    public async Task ValidateFileAsync_LoginUrlCarriesCredentialForm_IsConfirmedWithoutReachabilityWording()
    {
        var analyzerMock = CreateContentAnalyzerMock();
        analyzerMock.Setup(s => s.AnalyzeLoginUrlAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((ELoginUrlVerdict.Confirmed, "credential form on the page"));
        var progress = new RecordingProgress();

        var (definition, research) = CreateHostedDefinition("https://test.example.com/login");
        var issues = await ValidateAsync(
            definition,
            research,
            contentAnalyzerMock: analyzerMock,
            progress: progress);

        issues.Should().NotContain(i => i.Code == ValidationIssueCodes.LoginUrlNotLoginPage);
        progress.Reports.Should().Contain(p =>
            p.Field == "loginUrl"
            && (p.ResultMessage ?? string.Empty).Contains("login surface confirmed (credential form on the page)"));
        progress.Reports.Should().NotContain(p =>
            p.Field == "loginUrl" && p.ResultMessage == "URL is reachable");
    }

    [Fact]
    public async Task ValidateFileAsync_LoginUrlCannotBeJudgedFromThePage_ReportsThatInsteadOfCertifying()
    {
        // The abstention must not borrow the wording of a verification: a page that only links to
        // signing in says nothing about whether it is where signing in happens.
        var analyzerMock = CreateContentAnalyzerMock();
        analyzerMock.Setup(s => s.AnalyzeLoginUrlAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((ELoginUrlVerdict.NotEvaluated, "the page only links to a sign-in surface (2 link(s))"));
        var progress = new RecordingProgress();

        var (definition, research) = CreateHostedDefinition("https://console.test.example.com/keys");
        var issues = await ValidateAsync(
            definition,
            research,
            contentAnalyzerMock: analyzerMock,
            progress: progress);

        issues.Should().NotContain(i => i.Code == ValidationIssueCodes.LoginUrlNotLoginPage);
        progress.Reports.Should().Contain(p =>
            p.Field == "loginUrl"
            && (p.ResultMessage ?? string.Empty).Contains("could not be confirmed as a sign-in surface")
            && (p.ResultMessage ?? string.Empty).Contains("stored value kept"));
    }

    [Fact]
    public async Task ValidateFileAsync_SelfHostedLoginUrlDuplicatesWebsite_RaisesOneFindingNotTwo()
    {
        // The reported run: a self-hosted entry carrying the same address in 'website' and
        // 'loginUrl'. The duplicate comparison has nothing to learn about a loginUrl already
        // resolved above as impossible, and raising a second finding for that one field sent the
        // loginUrl tree to research the same "-" verdict twice in the same batch.
        var progress = new RecordingProgress();

        var issues = await ValidateAsync(
            new AIProviderConnect.Models.ProviderDefinition
                {
                    Id = "test",
                    DisplayName = "Test",
                    Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                    BaseUrl = "http://127.0.0.1:8000/v1/",
                    Category = "SelfHosted"
                },
            new AIProviderConnect.Models.ProviderResearchMetadata
                {
                    Website = "https://test.example.com",
                    LoginUrl = "https://test.example.com",
                    ApiPricingUrl = ProviderJsonFields.NotApplicable,
                    SubscriptionPricingUrl = ProviderJsonFields.NotApplicable,
                    DocumentationUrl = "https://test.example.com/docs",
                    IsDynamicModelCatalog = true
                },
            progress: progress);

        issues.Count(i => i.Field == "loginUrl").Should().Be(1);
        issues.Should().Contain(i =>
            i.Code == ValidationIssueCodes.LoginUrlNotApplicableForSelfHosted);
        issues.Should().NotContain(i => i.Code == ValidationIssueCodes.LoginUrlSameAsWebsite);
    }

    [Fact]
    public async Task ValidateFileAsync_WebsiteIsAPageOnTheProvidersOwnDomain_FlagsItAsNotTheHomepage()
    {
        // The defect the reported run exposed: a documentation page stored as the public homepage.
        // The stored page is saturated with the provider's name and with the word "docs", so
        // judging it could only ever certify it. The root domain is what tells a section of the
        // provider's own site apart from a service that happens to live on somebody else's domain.
        var fetcherMock = CreatePageFetcherMock(
            new Dictionary<string, StubPage>
                {
                    ["https://docs.example.com/guide/"] =
                        new("Testrig Docs — everything about Testrig"),
                    ["https://example.com"] = new("Testrig — build on open models"),
                });
        var progress = new RecordingProgress();

        var (definition, research) = CreateSubdomainWebsiteDefinition("https://docs.example.com/guide/");
        var issues = await ValidateAsync(
            definition,
            research,
            fetcherMock: fetcherMock,
            progress: progress,
            httpHandler: NeutralApiProbeHandler());

        var issue = issues.Single(i => i.Code == ValidationIssueCodes.WebsiteIsSubdomain);
        issue.Field.Should().Be("website");
        issue.CurrentValue.Should().Be("https://docs.example.com/guide/");
        issue.Message.Should().Contain("https://example.com");

        // A finding that is not routed is how this value stayed wrong for a whole catalogue run:
        // only codes in UrlErrorCodes reach research, and the website tree is what answers where
        // the homepage is.
        ValidationIssueCodes.UrlErrorCodes.Should()
            .Contain(ValidationIssueCodes.WebsiteIsSubdomain);

        // The continue that hides this finding must not also hide a success line contradicting it.
        progress.Reports.Should().NotContain(p =>
            p.Field == "website" && p.ResultMessage == "URL is reachable");
    }

    [Fact]
    public async Task ValidateFileAsync_WebsiteSubdomainRootBelongsToAnotherOrganization_KeepsTheSubdomain()
    {
        // blablador.fz-juelich.de: the root answers with the institution that hosts the service, so
        // the subdomain is the only address that represents the provider. The old rule reached the
        // same answer by reading the subdomain page, which agrees with almost anything.
        var fetcherMock = CreatePageFetcherMock(
            new Dictionary<string, StubPage>
                {
                    ["https://service.example.org/"] = new("Testrig — chat with open models"),
                    ["https://example.org"] =
                        new("Example Organization — research in physics and climate"),
                });
        var progress = new RecordingProgress();

        var (definition, research) = CreateSubdomainWebsiteDefinition("https://service.example.org/");
        var issues = await ValidateAsync(
            definition,
            research,
            fetcherMock: fetcherMock,
            progress: progress,
            httpHandler: NeutralApiProbeHandler());

        issues.Should().NotContain(i => i.Code == ValidationIssueCodes.WebsiteIsSubdomain);
        progress.Reports.Should().Contain(p =>
            p.Field == "website"
            && p.Stage == ValidationStage.CheckPassed
            && (p.ResultMessage ?? string.Empty).Contains("does not name this provider"));
    }

    [Fact]
    public async Task ValidateFileAsync_WebsiteSubdomainRootHandsVisitorsOn_KeepsTheSubdomain()
    {
        // A root that redirects is not a destination of its own — and when the page it lands on is
        // the documentation subdomain being questioned, replacing the stored value with the root
        // would send the user to the very page the field is supposed to move away from.
        var fetcherMock = CreatePageFetcherMock(
            new Dictionary<string, StubPage>
                {
                    ["https://docs.example.com/guide/"] = new("Testrig Docs"),
                    ["https://example.com"] =
                        new("Testrig — build on open models", FinalUrl: "https://docs.example.com/home"),
                });
        var progress = new RecordingProgress();

        var (definition, research) = CreateSubdomainWebsiteDefinition("https://docs.example.com/guide/");
        var issues = await ValidateAsync(
            definition,
            research,
            fetcherMock: fetcherMock,
            progress: progress,
            httpHandler: NeutralApiProbeHandler());

        issues.Should().NotContain(i => i.Code == ValidationIssueCodes.WebsiteIsSubdomain);
        progress.Reports.Should().Contain(p =>
            p.Field == "website"
            && (p.ResultMessage ?? string.Empty).Contains(
                "hands its visitors to 'docs.example.com'"));
    }

    [Fact]
    public async Task ValidateFileAsync_WebsiteSubdomainRootCannotBeRead_ReportsThatInsteadOfCertifying()
    {
        var fetcherMock = CreatePageFetcherMock(
            new Dictionary<string, StubPage>
                {
                    ["https://docs.example.com/guide/"] = new("Testrig Docs"),
                    ["https://example.com"] =
                        new(Success: false, Error: "navigation timed out"),
                });
        var progress = new RecordingProgress();

        var (definition, research) = CreateSubdomainWebsiteDefinition("https://docs.example.com/guide/");
        var issues = await ValidateAsync(
            definition,
            research,
            fetcherMock: fetcherMock,
            progress: progress,
            httpHandler: NeutralApiProbeHandler());

        issues.Should().NotContain(i => i.Code == ValidationIssueCodes.WebsiteIsSubdomain);
        progress.Reports.Should().Contain(p =>
            p.Field == "website"
            && p.Stage == ValidationStage.CheckPassed
            && (p.ResultMessage ?? string.Empty).Contains("navigation timed out")
            && (p.ResultMessage ?? string.Empty).Contains("stored value kept"));
    }

    /// <summary>
    /// Serializes the runtime definition and its research metadata into the single flat manifest
    /// shape the on-disk provider JSON uses, which is what the validator reads.
    /// Delegates to <see cref="ProviderManifestSerializer.Flatten"/> for a single source of truth.
    /// </summary>
    private static string SerializeManifest(
        AIProviderConnect.Models.ProviderDefinition definition,
        AIProviderConnect.Models.ProviderResearchMetadata? research = null)
        => ProviderManifestSerializer.Flatten(definition, research).ToJsonString();

    /// <summary>
    /// Validates a throwaway provider definition supplied by the caller. The fetcher answers every
    /// page successfully, so the login-surface verdict is the only thing under test.
    /// </summary>
    private static async Task<List<ValidationIssue>> ValidateAsync(
        AIProviderConnect.Models.ProviderDefinition definition,
        AIProviderConnect.Models.ProviderResearchMetadata? research = null,
        Mock<IContentAnalyzer>? contentAnalyzerMock = null,
        IProgress<ValidationProgress>? progress = null,
        Mock<IWebContentFetcher>? fetcherMock = null,
        HttpMessageHandler? httpHandler = null)
    {
        var json = SerializeManifest(definition, research);

        var tempFile = Path.Combine(Path.GetTempPath(), $"validator-login-{Guid.NewGuid():N}.json");
        File.WriteAllText(tempFile, json);

        try
        {
            return await CreateValidator(
                    contentAnalyzerMock: contentAnalyzerMock,
                    fetcherMock: fetcherMock,
                    httpHandler: httpHandler)
                .ValidateFileAsync(tempFile, progress: progress);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// A hosted provider whose <c>website</c> sits on a subdomain, so the root-domain question is
    /// the one being asked. Every other field is on a host the root-domain check leaves alone.
    /// </summary>
    private static (AIProviderConnect.Models.ProviderDefinition Definition, AIProviderConnect.Models.ProviderResearchMetadata Research) CreateSubdomainWebsiteDefinition(
        string website) =>
        (
            new()
            {
                Id = "test",
                DisplayName = "Testrig",
                Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                BaseUrl = "https://api.example.com/v1"
            },
            new()
            {
                Website = website,
                LoginUrl = "https://console.example.com/login",
                ApiPricingUrl = "https://example.com/pricing",
                SubscriptionPricingUrl = ProviderJsonFields.NotApplicable,
                DocumentationUrl = "https://docs.example.com/api/",
                MinModelCount = 1
            });

    private static (AIProviderConnect.Models.ProviderDefinition Definition, AIProviderConnect.Models.ProviderResearchMetadata Research) CreateHostedDefinition(string loginUrl) =>
        (
            new()
            {
                Id = "test",
                DisplayName = "Test",
                Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                BaseUrl = "https://api.test.example.com/v1"
            },
            new()
            {
                Website = "https://test.example.com",
                LoginUrl = loginUrl,
                ApiPricingUrl = "https://test.example.com/pricing",
                SubscriptionPricingUrl = ProviderJsonFields.NotApplicable,
                DocumentationUrl = "https://test.example.com/docs",
                MinModelCount = 1
            });

    /// <summary>
    /// Validates a throwaway provider definition whose baseUrl points at <paramref name="baseUrl"/>,
    /// answering every probe the validator sends with <paramref name="handler"/>.
    /// </summary>
    private static async Task<List<ValidationIssue>> ValidateProviderAsync(
        string baseUrl,
        HttpMessageHandler handler,
        IProgress<ValidationProgress>? progress = null)
    {
        var json = SerializeManifest(
            new AIProviderConnect.Models.ProviderDefinition
                {
                    Id = "test",
                    DisplayName = "Test",
                    Protocol = AIProviderConnect.Models.EProviderProtocol.OpenAICompatible,
                    BaseUrl = baseUrl
                },
            new AIProviderConnect.Models.ProviderResearchMetadata
                {
                    Website = "https://test.example.com",
                    LoginUrl = "https://test.example.com/login",
                    ApiPricingUrl = "https://test.example.com/pricing",
                    DocumentationUrl = "https://test.example.com/docs",
                    SubscriptionPricingUrl = ProviderJsonFields.NotApplicable,
                    MinModelCount = 1
                });

        var tempFile = Path.Combine(Path.GetTempPath(), $"validator-baseurl-{Guid.NewGuid():N}.json");
        File.WriteAllText(tempFile, json);

        try
        {
            return await CreateValidator(httpHandler: handler)
                .ValidateFileAsync(tempFile, progress: progress);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Answers every request with one fixed reply, and records what was asked.
    /// </summary>
    private sealed class StubApiProbeHandler : HttpMessageHandler
    {
        private readonly (HttpStatusCode Status, string MediaType, string Body) _fallback;

        public StubApiProbeHandler(
            HttpStatusCode status,
            string mediaType,
            string body)
        {
            _fallback = (status, mediaType, body);
        }

        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);

            var (status, mediaType, body) = _fallback;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType)
            });
        }
    }

    /// <summary>
    /// Collects progress reports synchronously. <see cref="Progress{T}"/> posts to a
    /// synchronization context, so a report could still be in flight when the test asserts.
    /// </summary>
    private sealed class RecordingProgress : IProgress<ValidationProgress>
    {
        public List<ValidationProgress> Reports { get; } = [];

        public void Report(ValidationProgress value) => Reports.Add(value);
    }

    private static Mock<IContentAnalyzer> CreateContentAnalyzerMock()
    {
        var mock = new Mock<IContentAnalyzer>();
        mock.Setup(s => s.AnalyzeApiPricingContentAsync(It.IsAny<string>()))
            .ReturnsAsync((EPricingContentVerdict.HasPricing, "amount displayed on the page ('$1')"));
        mock.Setup(s => s.AnalyzeSubscriptionPricingContentAsync(It.IsAny<string>()))
            .ReturnsAsync(
                (EPricingContentVerdict.HasPricing, "priced plans displayed on the page ('$1', 1 tier signal(s))"));
        mock.Setup(s => s.HasNotFoundContentAsync(It.IsAny<string>()))
            .ReturnsAsync(false);
        // Tests about the login surface replace this; it is set here so that every other test keeps
        // the outcome it was written against instead of inheriting an abstention.
        mock.Setup(s => s.AnalyzeLoginUrlAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((ELoginUrlVerdict.Confirmed, "credential form on the page"));
        return mock;
    }

    private static Mock<IDuplicateIdChecker> CreateDuplicateIdCheckerMock()
    {
        var mock = new Mock<IDuplicateIdChecker>();
        mock.Setup(s => s.CheckDuplicateIds(It.IsAny<string>()))
            .Returns([]);
        return mock;
    }

    private static Mock<IWebContentFetcher> CreateFetcherMock()
    {
        var mock = new Mock<IWebContentFetcher>();
        mock.Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebTools.NET.Models.WebContent(true, "OK", null, null));
        mock.Setup(s => s.FetchAsAsync(
                It.IsAny<string>(),
                It.IsAny<WebTools.NET.Models.EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<WebTools.NET.Models.ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebTools.NET.Models.WebContent(true, "OK", null, null));
        mock.Setup(s => s.CheckReachabilityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebTools.NET.Models.UrlCheckResult(true, 200, null));
        return mock;
    }

    /// <summary>
    /// Serves <paramref name="pages"/> for the addresses a verdict is written against and a plain
    /// page for everything else the validator asks for, so no test reaches the network and each one
    /// only describes the pages that matter to it.
    /// </summary>
    private static Mock<IWebContentFetcher> CreatePageFetcherMock(
        Dictionary<string, StubPage> pages)
    {
        var mock = new Mock<IWebContentFetcher>();

        WebTools.NET.Models.WebContent Serve(string url)
        {
            var page = pages.TryGetValue(url, out var served) ? served : new StubPage("A page.");

            return new WebTools.NET.Models.WebContent(
                page.Success,
                page.Success ? page.Content : string.Empty,
                page.Success ? null : page.Error,
                page.FinalUrl ?? url);
        }

        mock.Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string url, int? _, CancellationToken _) => Serve(url));
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
                CancellationToken ____) => Serve(url));
        mock.Setup(s => s.CheckReachabilityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebTools.NET.Models.UrlCheckResult(true, 200, null));

        return mock;
    }

    /// <summary>
    /// One page as the fetcher reports it: what it says, where the browser ended up, and whether it
    /// could be read at all.
    /// </summary>
    private sealed record StubPage(
        string Content = "",
        string? FinalUrl = null,
        bool Success = true,
        string? Error = null);

    /// <summary>
    /// A base address that answers nothing a probe can judge, so that a test about another field
    /// abstains on the baseUrl instead of reaching the network for it.
    /// </summary>
    private static StubApiProbeHandler NeutralApiProbeHandler() =>
        new(HttpStatusCode.NotFound, "text/html", "<html><body>nothing</body></html>");

    private static Mock<IValidationMetadataService> CreateMetadataServiceMock()
    {
        var mock = new Mock<IValidationMetadataService>();
        mock.Setup(s => s.Read(It.IsAny<string>()))
            .Returns((ValidationMetadata?)null);
        return mock;
    }

    private static Mock<IProviderSchemaValidator> CreateSchemaValidatorMock()
    {
        var mock = new Mock<IProviderSchemaValidator>();
        mock.Setup(s => s.ValidateSchema(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([]);
        return mock;
    }

    private static Mock<IUrlReachabilityChecker> CreateUrlCheckerMock()
    {
        var mock = new Mock<IUrlReachabilityChecker>();
        mock.Setup(s => s.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebTools.NET.Models.UrlCheckResult(true, 200, null));
        return mock;
    }

    private static ProviderDefinitionValidator CreateValidator(
        Mock<IWebContentFetcher>? fetcherMock = null,
        Mock<IProviderSchemaValidator>? schemaValidatorMock = null,
        Mock<IUrlReachabilityChecker>? urlCheckerMock = null,
        Mock<IContentAnalyzer>? contentAnalyzerMock = null,
        Mock<IDuplicateIdChecker>? duplicateIdCheckerMock = null,
        Mock<IValidationMetadataService>? metadataServiceMock = null,
        HttpMessageHandler? httpHandler = null)
    {
        // A supplied handler owns the transport, so no test that probes an endpoint has to reach
        // the network — and nothing here may.
        var fetcher = (fetcherMock ?? CreateFetcherMock()).Object;
        var httpClient = httpHandler is null
            ? new HttpClient()
            : new HttpClient(httpHandler, disposeHandler: false);
        var analyzer = (contentAnalyzerMock ?? CreateContentAnalyzerMock()).Object;
        var urlChecker = (urlCheckerMock ?? CreateUrlCheckerMock()).Object;

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient(HttpConstants.ScraperHttpClientName))
            .Returns(httpClient);

        var fieldChecker = ValidatorGraphBuilder.BuildFieldChecker(
            urlChecker,
            fetcher,
            analyzer,
            httpClientFactory.Object);

        return new ProviderDefinitionValidator(
            (schemaValidatorMock ?? CreateSchemaValidatorMock()).Object,
            (duplicateIdCheckerMock ?? CreateDuplicateIdCheckerMock()).Object,
            (metadataServiceMock ?? CreateMetadataServiceMock()).Object,
            // Reads the manifest off disk exactly as the validator used to do inline.
            new ProviderManifestReader(),
            new SelfHostedApplicabilityEvaluator(),
            new SubscriptionConfiguredChecker(),
            new ServiceRetirementProbe(fetcher),
            fieldChecker);
    }
}
