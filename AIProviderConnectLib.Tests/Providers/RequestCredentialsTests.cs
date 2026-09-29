using AIProviderConnect.Models;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Providers;

public class RequestCredentialsTests
{
    [Fact]
    public void ToString_LongApiKey_MasksKeyAndKeepsAtMostLastFourCharacters()
    {
        // Arrange
        var credentials = new RequestCredentials
        {
            ApiKey = "fake-api-key",
            BaseUrl = "https://test.example.com/v1/",
            Model = "override-model"
        };

        // Act
        var text = credentials.ToString();

        // Assert
        text.Should().NotContain("fake-api-key");
        text.Should().Contain("-key");
        text.Should().Contain("override-model");
    }

    [Theory]
    [InlineData("abcd")]
    [InlineData("ab")]
    public void ToString_ShortApiKey_NeverAppearsInOutput(string apiKey)
    {
        // Arrange
        var credentials = new RequestCredentials { ApiKey = apiKey };

        // Act
        var text = credentials.ToString();

        // Assert
        text.Should().NotContain(apiKey);
    }

    [Fact]
    public void ToString_NullApiKey_RendersNullSentinel()
    {
        // Arrange
        var credentials = new RequestCredentials();

        // Act
        var text = credentials.ToString();

        // Assert
        text.Should().Contain("ApiKey = null");
    }
}
