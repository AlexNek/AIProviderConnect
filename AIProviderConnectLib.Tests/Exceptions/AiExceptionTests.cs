using AIProviderConnect.Exceptions;

using FluentAssertions;

namespace AIProviderConnect.Tests.Exceptions;

public class AiExceptionTests
{
    [Fact]
    public void Constructor_WithErrorCode_SetsProperties()
    {
        // Arrange
        var errorCode = AiErrorCodes.ProviderDisabled;
        var message = "Provider is disabled";

        // Act
        var exception = new AiException(errorCode, message);

        // Assert
        exception.Code.Should().Be(errorCode);
        exception.Message.Should().Contain(message);
    }

    [Fact]
    public void Constructor_WithInnerException_SetsInnerException()
    {
        // Arrange
        var errorCode = AiErrorCodes.NoConnection;
        var message = "Cannot connect to provider";
        var innerException = new HttpRequestException("Connection refused");

        // Act
        var exception = new AiException(errorCode, message, innerException);

        // Assert
        exception.Code.Should().Be(errorCode);
        exception.InnerException.Should().Be(innerException);
        exception.Message.Should().Contain(message);
    }

    [Theory]
    [InlineData(AiErrorCodes.NoApiKey)]
    [InlineData(AiErrorCodes.RateLimited)]
    [InlineData(AiErrorCodes.ProviderError)]
    [InlineData(AiErrorCodes.NoConnection)]
    [InlineData(AiErrorCodes.EndpointNotFound)]
    [InlineData(AiErrorCodes.ConfigurationError)]
    [InlineData(AiErrorCodes.Unauthorized)]
    [InlineData(AiErrorCodes.Forbidden)]
    public void ErrorCode_CoversAllErrorCodes(string errorCode)
    {
        // Arrange & Act
        var exception = new AiException(errorCode, "Test");

        // Assert
        exception.Code.Should().Be(errorCode);
    }

    [Fact]
    public void ToString_IncludesErrorCode()
    {
        // Arrange
        var exception = new AiException(AiErrorCodes.RateLimited, "Rate limited");

        // Act
        var result = exception.ToString();

        // Assert
        result.Should().Contain("Rate limited");
    }
}
