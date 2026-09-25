using FluentAssertions;

using Moq;

using ScraperTool.Services;
using ScraperTool.Services.Validation;
using ScraperTool.Services.Validation.Checks;

using System.Net;
using System.Net.Http;

using WebTools.NET.Abstractions;

namespace ScraperTool.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="ApiEndpointProbe"/> that verify the probe correctly classifies
/// API endpoints — including the auth-gate recognition for plain-text "provide an API key"
/// responses that some providers return with HTTP 200 instead of a structured 401/403 error.
/// </summary>
public class ApiEndpointProbeTests
{
    private readonly Mock<IUrlReachabilityChecker> _reachabilityMock = new();

    [Fact]
    public async Task ProbeIsApiEndpointAsync_PlainTextAuthGate_WithApiKeyPhrase_ReturnsIsApi()
    {
        // Arrange — mirrors the Blablador response: HTTP 200, plain text, "API key" phrase.
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "You must provide a valid API key. Obtain one from http://helmholtz.cloud",
                System.Text.Encoding.UTF8,
                "text/plain"),
        };

        var probe = CreateProbe(response);

        // Act
        var (verdict, reason) = await probe.ProbeIsApiEndpointAsync(
            "https://api.blablador.fz-juelich.de/v1/", CancellationToken.None);

        // Assert
        verdict.Should().Be(EApiProbeVerdict.IsApi);
        reason.Should().Contain("auth-gate");
    }

    [Fact]
    public async Task ProbeIsApiEndpointAsync_PlainTextBearerToken_ReturnsIsApi()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "Missing bearer token. Include an Authorization header to access this API.",
                System.Text.Encoding.UTF8,
                "text/plain"),
        };

        var probe = CreateProbe(response);

        // Act
        var (verdict, _) = await probe.ProbeIsApiEndpointAsync(
            "https://api.example.com/v1/", CancellationToken.None);

        // Assert
        verdict.Should().Be(EApiProbeVerdict.IsApi);
    }

    [Fact]
    public async Task ProbeIsApiEndpointAsync_PlainTextNoAuthPhrase_ReturnsNotApi()
    {
        // Arrange — a plain-text response with no auth-related keywords.
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "Welcome to our website. This is not an API endpoint.",
                System.Text.Encoding.UTF8,
                "text/plain"),
        };

        var probe = CreateProbe(response);

        // Act
        var (verdict, reason) = await probe.ProbeIsApiEndpointAsync(
            "https://example.com/", CancellationToken.None);

        // Assert
        verdict.Should().Be(EApiProbeVerdict.NotApi);
        reason.Should().Contain("nothing can read as data");
    }

    [Fact]
    public async Task ProbeIsApiEndpointAsync_JsonContentType_ReturnsIsApi()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"models\":[]}",
                System.Text.Encoding.UTF8,
                "application/json"),
        };

        var probe = CreateProbe(response);

        // Act
        var (verdict, _) = await probe.ProbeIsApiEndpointAsync(
            "https://api.example.com/v1/", CancellationToken.None);

        // Assert
        verdict.Should().Be(EApiProbeVerdict.IsApi);
    }

    [Fact]
    public async Task ProbeIsApiEndpointAsync_JsonBodyPrefix_ReturnsIsApi()
    {
        // Arrange — no JSON content type, but body starts with '{'.
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"error\":\"unauthorized\"}",
                System.Text.Encoding.UTF8,
                "text/plain"),
        };

        var probe = CreateProbe(response);

        // Act
        var (verdict, _) = await probe.ProbeIsApiEndpointAsync(
            "https://api.example.com/v1/", CancellationToken.None);

        // Assert
        verdict.Should().Be(EApiProbeVerdict.IsApi);
    }

    [Fact]
    public async Task ProbeIsApiEndpointAsync_HtmlContentType_ReturnsNotApi()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "<html><body>Welcome</body></html>",
                System.Text.Encoding.UTF8,
                "text/html"),
        };

        var probe = CreateProbe(response);

        // Act
        var (verdict, reason) = await probe.ProbeIsApiEndpointAsync(
            "https://example.com/", CancellationToken.None);

        // Assert
        verdict.Should().Be(EApiProbeVerdict.NotApi);
        reason.Should().Contain("HTML");
    }

    [Fact]
    public async Task ProbeIsApiEndpointAsync_NonSuccessStatus_ReturnsNotEvaluated()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);

        var probe = CreateProbe(response);

        // Act
        var (verdict, _) = await probe.ProbeIsApiEndpointAsync(
            "https://api.example.com/v1/", CancellationToken.None);

        // Assert
        verdict.Should().Be(EApiProbeVerdict.NotEvaluated);
    }

    [Fact]
    public async Task ProbeIsApiEndpointAsync_AccessTokenPhrase_ReturnsIsApi()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "A valid access token is required. Please create one at https://example.com/tokens.",
                System.Text.Encoding.UTF8,
                "text/plain"),
        };

        var probe = CreateProbe(response);

        // Act
        var (verdict, _) = await probe.ProbeIsApiEndpointAsync(
            "https://api.example.com/v1/", CancellationToken.None);

        // Assert
        verdict.Should().Be(EApiProbeVerdict.IsApi);
    }

    private ApiEndpointProbe CreateProbe(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return new ApiEndpointProbe(factoryMock.Object, _reachabilityMock.Object);
    }

    private ApiEndpointProbe CreateProbe(HttpResponseMessage response)
    {
        var handler = new SingleResponseHandler(response);
        return CreateProbe(handler);
    }

    /// <summary>
    /// Returns the same response for every request — enough for probe unit tests.
    /// </summary>
    private sealed class SingleResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}
