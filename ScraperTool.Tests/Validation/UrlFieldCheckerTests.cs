using FluentAssertions;

using Moq;

using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.Services.Validation;
using ScraperTool.Services.Validation.Checks;

using System.Text.Json;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Tests.Validation;

/// <summary>
/// Direct unit tests for <see cref="UrlFieldChecker"/> that exercise the five key seams
/// (pricing verification, reachability with redirect caps, website root ownership,
/// login-url equality, and login surface analysis) using mocked collaborators.
/// </summary>
public class UrlFieldCheckerTests
{
    private readonly Mock<IUrlReachabilityChecker> _reachabilityMock = new();
    private readonly Mock<IPageContentProbe> _probeMock = new();
    private readonly Mock<IPricingPageVerifier> _pricingMock = new();
    private readonly Mock<IWebsiteOwnershipJudge> _judgeMock = new();
    private readonly Mock<IApiEndpointProbe> _apiMock = new();
    private readonly Mock<IManualBrowserVerifier> _manualVerifierMock = new();

    private readonly UrlFieldChecker _checker;

    public UrlFieldCheckerTests()
    {
        _checker = new UrlFieldChecker(
            _reachabilityMock.Object,
            _probeMock.Object,
            _pricingMock.Object,
            _judgeMock.Object,
            _apiMock.Object,
            _manualVerifierMock.Object);
    }

    private static (ValidationIssueSink Sink, List<ValidationIssue> Issues) CreateSink()
    {
        var issues = new List<ValidationIssue>();
        return (new ValidationIssueSink(issues, null), issues);
    }

    [Fact]
    public async Task CheckFieldAsync_PrivateUrl_WithoutLocalProviders_ReturnsWithoutChecking()
    {
        // Arrange
        var root = JsonDocument.Parse("""{"id":"test","baseUrl":"http://localhost:1234/v1"}""").RootElement;
        var (sink, issues) = CreateSink();
        var context = new FieldCheckContext(
            "test.json",
            ProviderJsonFields.BaseUrl,
            "http://localhost:1234/v1",
            new Uri("http://localhost:1234/v1"),
            root,
            sink,
            CancellationToken.None)
        {
            UseLocalProviders = false
        };

        // Act
        await _checker.CheckFieldAsync(context);

        // Assert
        _reachabilityMock.Verify(r => r.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Never);
        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckFieldAsync_PrivateUrl_WithLocalProviders_ChecksReachability()
    {
        // Arrange
        var root = JsonDocument.Parse("""{"id":"test","baseUrl":"http://localhost:1234/v1"}""").RootElement;
        var (sink, issues) = CreateSink();
        var context = new FieldCheckContext(
            "test.json",
            ProviderJsonFields.BaseUrl,
            "http://localhost:1234/v1",
            new Uri("http://localhost:1234/v1"),
            root,
            sink,
            CancellationToken.None)
        {
            UseLocalProviders = true
        };

        _reachabilityMock.Setup(r => r.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlCheckResult(true, 200, null));

        _apiMock.Setup(a => a.ProbeIsApiEndpointAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EApiProbeVerdict.IsApi, "API endpoint detected"));

        // Act
        await _checker.CheckFieldAsync(context);

        // Assert
        _reachabilityMock.Verify(r => r.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckFieldAsync_PricingField_CallsPricingVerifier()
    {
        // Arrange
        var root = JsonDocument.Parse("""{"id":"test","apiPricingUrl":"https://example.com/pricing"}""").RootElement;
        var (sink, issues) = CreateSink();
        var context = new FieldCheckContext(
            "test.json",
            ProviderJsonFields.ApiPricingUrl,
            "https://example.com/pricing",
            new Uri("https://example.com/pricing"),
            root,
            sink,
            CancellationToken.None);

        _pricingMock.Setup(p => p.VerifyPricingUrlAsync(
                It.IsAny<Uri>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IValidationIssueSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EPricingContentVerdict.HasPricing, "amount displayed"));

        // Act
        await _checker.CheckFieldAsync(context);

        // Assert
        _pricingMock.Verify(p => p.VerifyPricingUrlAsync(
            It.IsAny<Uri>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            ProviderJsonFields.ApiPricingUrl,
            It.IsAny<IValidationIssueSink>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckFieldAsync_WebsiteField_CallsWebsiteOwnershipJudge()
    {
        // Arrange
        var root = JsonDocument.Parse("""{"id":"test","website":"https://console.example.com"}""").RootElement;
        var (sink, issues) = CreateSink();
        var context = new FieldCheckContext(
            "test.json",
            ProviderJsonFields.Website,
            "https://console.example.com",
            new Uri("https://console.example.com"),
            root,
            sink,
            CancellationToken.None);

        _reachabilityMock.Setup(r => r.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlCheckResult(true, 200, null, RedirectCount: 0));

        _probeMock.Setup(p => p.ReadBodyForErrorPageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IValidationIssueSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageContentReadResult("page content"));

        _judgeMock.Setup(j => j.TrySettleWebsiteRootAsync(
                It.IsAny<Uri>(),
                It.IsAny<JsonElement>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IValidationIssueSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _checker.CheckFieldAsync(context);

        // Assert
        _judgeMock.Verify(j => j.TrySettleWebsiteRootAsync(
            It.IsAny<Uri>(),
            It.IsAny<JsonElement>(),
            It.IsAny<string>(),
            ProviderJsonFields.Website,
            It.IsAny<string>(),
            It.IsAny<IValidationIssueSink>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckFieldAsync_LoginUrlField_UsesLoginVerdictFromProbe()
    {
        // Arrange
        var root = JsonDocument.Parse("""{"id":"test","loginUrl":"https://example.com/login"}""").RootElement;
        var (sink, issues) = CreateSink();
        var context = new FieldCheckContext(
            "test.json",
            ProviderJsonFields.LoginUrl,
            "https://example.com/login",
            new Uri("https://example.com/login"),
            root,
            sink,
            CancellationToken.None);

        _reachabilityMock.Setup(r => r.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlCheckResult(true, 200, null, RedirectCount: 0));

        _probeMock.Setup(p => p.ReadBodyForErrorPageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IValidationIssueSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageContentReadResult("login page", ELoginUrlVerdict.Confirmed, "credential form detected"));

        // Act
        await _checker.CheckFieldAsync(context);

        // Assert
        _probeMock.Verify(p => p.ReadBodyForErrorPageAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            ProviderJsonFields.LoginUrl,
            It.IsAny<IValidationIssueSink>(),
            It.IsAny<CancellationToken>()), Times.Once);

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckFieldAsync_LoginUrlNotLoginPage_ReportsFailure()
    {
        // Arrange
        var root = JsonDocument.Parse("""{"id":"test","loginUrl":"https://example.com/login"}""").RootElement;
        var (sink, issues) = CreateSink();
        var context = new FieldCheckContext(
            "test.json",
            ProviderJsonFields.LoginUrl,
            "https://example.com/login",
            new Uri("https://example.com/login"),
            root,
            sink,
            CancellationToken.None);

        _reachabilityMock.Setup(r => r.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlCheckResult(true, 200, null, RedirectCount: 0));

        _probeMock.Setup(p => p.ReadBodyForErrorPageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IValidationIssueSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageContentReadResult("not a login page", ELoginUrlVerdict.NotLoginPage, "no credential form"));

        // Act
        await _checker.CheckFieldAsync(context);

        // Assert
        issues.Should().ContainSingle(i => i.Code == ValidationIssueCodes.LoginUrlNotLoginPage);
    }

    [Fact]
    public async Task CheckFieldAsync_ExcessiveRedirects_ReportsFailure()
    {
        // Arrange
        var root = JsonDocument.Parse("""{"id":"test","website":"https://example.com"}""").RootElement;
        var (sink, issues) = CreateSink();
        var context = new FieldCheckContext(
            "test.json",
            ProviderJsonFields.Website,
            "https://example.com",
            new Uri("https://example.com"),
            root,
            sink,
            CancellationToken.None);

        _reachabilityMock.Setup(r => r.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlCheckResult(true, 200, null, RedirectCount: 10, FinalUrl: "https://example.com/final"));

        // Act
        await _checker.CheckFieldAsync(context);

        // Assert
        issues.Should().ContainSingle(i => i.Code == "UrlNotFound");
    }

    [Fact]
    public async Task CheckFieldAsync_SelfHostedApplicability_SkipsApplicableFields()
    {
        // Arrange
        var root = JsonDocument.Parse("""{"id":"test","loginUrl":"https://example.com/login"}""").RootElement;
        var (sink, issues) = CreateSink();
        var context = new FieldCheckContext(
            "test.json",
            ProviderJsonFields.LoginUrl,
            "https://example.com/login",
            new Uri("https://example.com/login"),
            root,
            sink,
            CancellationToken.None)
        {
            Applicability = new SelfHostedApplicabilityFlags(true, false, false)
        };

        // Act
        await _checker.CheckFieldAsync(context);

        // Assert
        _reachabilityMock.Verify(r => r.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Never);
        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAuthRequired_ProtectionType_Verified_PassesManuallyVerified()
    {
        // Arrange
        var root = JsonDocument.Parse("""{"id":"test","website":"https://example.com"}""").RootElement;
        var (sink, issues) = CreateSink();
        var context = new FieldCheckContext(
            "test.json",
            ProviderJsonFields.Website,
            "https://example.com",
            new Uri("https://example.com"),
            root,
            sink,
            CancellationToken.None);

        _reachabilityMock.Setup(r => r.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlCheckResult(false, 403, null, ProtectionType: "Cloudflare"));

        _manualVerifierMock.Setup(v => v.TryVerifyAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EManualVerificationResult.Verified);

        _probeMock.Setup(p => p.ReadBodyForErrorPageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IValidationIssueSink>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageContentReadResult("page body"));

        _judgeMock.Setup(j => j.TrySettleWebsiteRootAsync(
                It.IsAny<Uri>(), It.IsAny<System.Text.Json.JsonElement>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IValidationIssueSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _checker.CheckFieldAsync(context);

        // Assert
        issues.Should().NotContain(i => i.Code == ValidationIssueCodes.BotProtected);
        _manualVerifierMock.Verify(v => v.TryVerifyAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAuthRequired_ProtectionType_StillProtected_EmitsBotProtected()
    {
        // Arrange
        var root = JsonDocument.Parse("""{"id":"test","website":"https://example.com"}""").RootElement;
        var (sink, issues) = CreateSink();
        var context = new FieldCheckContext(
            "test.json",
            ProviderJsonFields.Website,
            "https://example.com",
            new Uri("https://example.com"),
            root,
            sink,
            CancellationToken.None);

        _reachabilityMock.Setup(r => r.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlCheckResult(false, 403, null, ProtectionType: "Cloudflare"));

        _manualVerifierMock.Setup(v => v.TryVerifyAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EManualVerificationResult.StillProtected);

        // Act
        await _checker.CheckFieldAsync(context);

        // Assert
        issues.Should().ContainSingle(i => i.Code == ValidationIssueCodes.BotProtected);
    }

    [Fact]
    public async Task HandleAuthRequired_ProtectionType_UserCancelled_EmitsBotProtected()
    {
        // Arrange
        var root = JsonDocument.Parse("""{"id":"test","website":"https://example.com"}""").RootElement;
        var (sink, issues) = CreateSink();
        var context = new FieldCheckContext(
            "test.json",
            ProviderJsonFields.Website,
            "https://example.com",
            new Uri("https://example.com"),
            root,
            sink,
            CancellationToken.None);

        _reachabilityMock.Setup(r => r.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlCheckResult(false, 403, null, ProtectionType: "Cloudflare"));

        _manualVerifierMock.Setup(v => v.TryVerifyAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EManualVerificationResult.UserCancelled);

        // Act
        await _checker.CheckFieldAsync(context);

        // Assert
        issues.Should().ContainSingle(i => i.Code == ValidationIssueCodes.BotProtected);
    }

    [Fact]
    public async Task HandleAuthRequired_ProtectionType_VerifierThrows_EmitsBotProtected()
    {
        // Arrange
        var root = JsonDocument.Parse("""{"id":"test","website":"https://example.com"}""").RootElement;
        var (sink, issues) = CreateSink();
        var context = new FieldCheckContext(
            "test.json",
            ProviderJsonFields.Website,
            "https://example.com",
            new Uri("https://example.com"),
            root,
            sink,
            CancellationToken.None);

        _reachabilityMock.Setup(r => r.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlCheckResult(false, 403, null, ProtectionType: "Cloudflare"));

        _manualVerifierMock.Setup(v => v.TryVerifyAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("browser crash"));

        // Act
        await _checker.CheckFieldAsync(context);

        // Assert
        issues.Should().ContainSingle(i => i.Code == ValidationIssueCodes.BotProtected);
    }

    [Fact]
    public async Task HandleAuthRequired_NoProtectionType_EmitsUrlRequiresAuth_VerifierNotCalled()
    {
        // Arrange
        var root = JsonDocument.Parse("""{"id":"test","loginUrl":"https://example.com/login"}""").RootElement;
        var (sink, issues) = CreateSink();
        var context = new FieldCheckContext(
            "test.json",
            ProviderJsonFields.LoginUrl,
            "https://example.com/login",
            new Uri("https://example.com/login"),
            root,
            sink,
            CancellationToken.None);

        _reachabilityMock.Setup(r => r.CheckAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlCheckResult(false, 401, null));

        // Act
        await _checker.CheckFieldAsync(context);

        // Assert
        issues.Should().ContainSingle(i => i.Code == "UrlRequiresAuth");
        _manualVerifierMock.Verify(v => v.TryVerifyAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
