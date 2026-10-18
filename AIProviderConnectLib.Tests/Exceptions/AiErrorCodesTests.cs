using AIProviderConnect.Exceptions;

using FluentAssertions;

namespace AIProviderConnect.Tests.Exceptions;

public class AiErrorCodesTests
{
    [Fact]
    public void AllErrorCodes_AreUnique()
    {
        // Arrange
        var errorCodes = new[]
        {
            AiErrorCodes.NoApiKey,
            AiErrorCodes.NoBaseUrl,
            AiErrorCodes.ConfigurationError,
            AiErrorCodes.Unauthorized,
            AiErrorCodes.Forbidden,
            AiErrorCodes.EndpointNotFound,
            AiErrorCodes.RateLimited,
            AiErrorCodes.ProviderError,
            AiErrorCodes.ProviderCallFailed,
            AiErrorCodes.NoConnection,
            AiErrorCodes.NoServer,
            AiErrorCodes.Timeout,
            AiErrorCodes.ProviderDisabled,
            AiErrorCodes.ProviderMissingConfiguration,
            AiErrorCodes.DecisionReadoutUnavailable,
            AiErrorCodes.DecisionReadoutUnreadable
        };

        // Act
        var distinctCodes = errorCodes.Distinct().ToArray();

        // Assert
        distinctCodes.Length.Should().Be(errorCodes.Length, "All error codes should be unique");
    }

    [Theory]
    [InlineData(AiErrorCodes.NoApiKey, "ai/no-api-key")]
    [InlineData(AiErrorCodes.NoBaseUrl, "ai/no-base-url")]
    [InlineData(AiErrorCodes.ConfigurationError, "ai/configuration-error")]
    [InlineData(AiErrorCodes.Unauthorized, "ai/unauthorized")]
    [InlineData(AiErrorCodes.Forbidden, "ai/forbidden")]
    [InlineData(AiErrorCodes.EndpointNotFound, "ai/endpoint-not-found")]
    [InlineData(AiErrorCodes.RateLimited, "ai/rate-limited")]
    [InlineData(AiErrorCodes.ProviderError, "ai/provider-error")]
    [InlineData(AiErrorCodes.ProviderCallFailed, "ai/provider-call-failed")]
    [InlineData(AiErrorCodes.NoConnection, "ai/no-connection")]
    [InlineData(AiErrorCodes.NoServer, "ai/no-server")]
    [InlineData(AiErrorCodes.Timeout, "ai/timeout")]
    [InlineData(AiErrorCodes.ProviderDisabled, "ai/provider-disabled")]
    [InlineData(AiErrorCodes.ProviderMissingConfiguration, "ai/provider-missing-configuration")]
    [InlineData(AiErrorCodes.DecisionReadoutUnavailable, "ai/decision-readout-unavailable")]
    [InlineData(AiErrorCodes.DecisionReadoutUnreadable, "ai/decision-readout-unreadable")]
    public void ErrorCodes_HaveExpectedValues(string errorCode, string expectedValue)
    {
        // Assert
        errorCode.Should().Be(expectedValue);
    }
}
