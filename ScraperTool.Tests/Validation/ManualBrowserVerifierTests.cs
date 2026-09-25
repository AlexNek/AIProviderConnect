using FluentAssertions;

using ScraperTool.Services.Validation;

namespace ScraperTool.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="ManualBrowserVerifier"/>.
/// </summary>
public class ManualBrowserVerifierTests
{
    [Fact]
    public async Task TryVerifyAsync_NoWpfApplication_ReturnsUserCancelled()
    {
        // Arrange — in a test host, System.Windows.Application.Current is null
        // because no WPF Application instance has been created.
        var verifier = new ManualBrowserVerifier();

        // Act
        var result = await verifier.TryVerifyAsync(
            "https://test.example.com",
            "website",
            "test.json",
            CancellationToken.None);

        // Assert
        result.Should().Be(EManualVerificationResult.UserCancelled);
    }
}
