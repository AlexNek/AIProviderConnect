using Moq;

using ScraperTool.Services.Validation;
using ScraperTool.Services.Validation.Checks;

using WebTools.NET.Abstractions;

namespace ScraperTool.Tests.Validation;

/// <summary>
/// Builds the validator's object graph for tests. The same wiring is needed in multiple
/// test classes, so the construction lives here rather than being duplicated in each arrange block.
/// </summary>
internal static class ValidatorGraphBuilder
{
    /// <summary>
    /// Builds a fully-wired <see cref="UrlFieldChecker"/> from the supplied collaborators.
    /// </summary>
    public static UrlFieldChecker BuildFieldChecker(
        IUrlReachabilityChecker urlChecker,
        IWebContentFetcher fetcher,
        IContentAnalyzer analyzer,
        IHttpClientFactory httpClientFactory,
        IManualBrowserVerifier? manualVerifier = null)
    {
        return new UrlFieldChecker(
            urlChecker,
            new PageContentProbe(fetcher, analyzer),
            new PricingPageVerifier(fetcher, analyzer),
            new WebsiteOwnershipJudge(fetcher),
            new ApiEndpointProbe(httpClientFactory, urlChecker),
            manualVerifier ?? CreateDefaultManualVerifier());
    }

    /// <summary>
    /// Builds a fully-wired <see cref="UrlFieldChecker"/> from the supplied mocks.
    /// Convenience overload for tests that work with <see cref="Mock{T}"/> instances.
    /// </summary>
    public static UrlFieldChecker BuildFieldChecker(
        IUrlReachabilityChecker urlChecker,
        Mock<IWebContentFetcher> fetcherMock,
        Mock<IContentAnalyzer> analyzerMock,
        Mock<IHttpClientFactory> httpClientFactoryMock,
        Mock<IManualBrowserVerifier>? manualVerifierMock = null)
    {
        return BuildFieldChecker(
            urlChecker,
            fetcherMock.Object,
            analyzerMock.Object,
            httpClientFactoryMock.Object,
            manualVerifierMock?.Object);
    }

    /// <summary>
    /// Creates a mock <see cref="IManualBrowserVerifier"/> that returns <see cref="EManualVerificationResult.UserCancelled"/>
    /// by default. This preserves the existing BotProtected fallback behavior in characterization tests
    /// that do not exercise the manual verification path.
    /// </summary>
    private static IManualBrowserVerifier CreateDefaultManualVerifier()
    {
        var mock = new Mock<IManualBrowserVerifier>();
        mock.Setup(v => v.TryVerifyAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EManualVerificationResult.UserCancelled);
        return mock.Object;
    }
}
