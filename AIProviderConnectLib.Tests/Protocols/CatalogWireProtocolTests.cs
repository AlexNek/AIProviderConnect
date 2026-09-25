using AIProviderConnect.Constants;
using AIProviderConnect.Options;
using AIProviderConnect.Protocols;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Protocols;

public class CatalogWireProtocolTests
{
    [Fact]
    public void ApplyProtocolConfiguration_SetsApiVersionHeader()
    {
        // Arrange
        var options = new OpenAICompatibleProviderOptions
        {
            ProtocolConfiguration = new Dictionary<string, string>
            {
                ["apiVersion"] = "2026-03-10",
                ["accept"] = "application/vnd.github+json"
            }
        };

        // Act
        CatalogWireProtocol.ApplyProtocolConfiguration(options);

        // Assert
        options.DefaultHeaders.Should().ContainKey("X-GitHub-Api-Version")
            .WhoseValue.Should().Be("2026-03-10");
    }

    [Fact]
    public void ApplyProtocolConfiguration_SetsAcceptHeader()
    {
        // Arrange
        var options = new OpenAICompatibleProviderOptions
        {
            ProtocolConfiguration = new Dictionary<string, string>
            {
                ["accept"] = "application/vnd.github+json"
            }
        };

        // Act
        CatalogWireProtocol.ApplyProtocolConfiguration(options);

        // Assert
        options.DefaultHeaders.Should().ContainKey("Accept")
            .WhoseValue.Should().Be("application/vnd.github+json");
    }

    [Fact]
    public void ApplyProtocolConfiguration_NullDictionary_NoOp()
    {
        // Arrange
        var options = new OpenAICompatibleProviderOptions { ProtocolConfiguration = null };

        // Act
        var act = () => CatalogWireProtocol.ApplyProtocolConfiguration(options);

        // Assert
        act.Should().NotThrow();
        options.DefaultHeaders.Should().BeEmpty();
    }
}
