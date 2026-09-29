using System.Text.Json;

using AIProviderConnect.Models;

using FluentAssertions;

namespace AIProviderConnect.Tests.Models;

public class ProviderProtocolMapperTests
{
    [Theory]
    [InlineData(EProviderProtocol.OpenAICompatible, "openaicompatible")]
    [InlineData(EProviderProtocol.MessagesApi, "anthropiccompatible")]
    [InlineData(EProviderProtocol.KeyQuery, "geminicompatible")]
    [InlineData(EProviderProtocol.Catalog, "githubmodelscompatible")]
    [InlineData(EProviderProtocol.HybridGateway, "hybridgateway")]
    [InlineData(EProviderProtocol.Decision, "decision")]
    [InlineData(EProviderProtocol.Native, "native")]
    public void ToJson_MapsAllEnumValues(EProviderProtocol protocol, string expectedJson)
    {
        ProviderProtocolMapper.ToJson(protocol).Should().Be(expectedJson);
    }

    [Theory]
    [InlineData("openaicompatible", EProviderProtocol.OpenAICompatible)]
    [InlineData("OpenAICompatible", EProviderProtocol.OpenAICompatible)]
    [InlineData("anthropiccompatible", EProviderProtocol.MessagesApi)]
    [InlineData("AnthropicCompatible", EProviderProtocol.MessagesApi)]
    [InlineData("geminicompatible", EProviderProtocol.KeyQuery)]
    [InlineData("githubmodelscompatible", EProviderProtocol.Catalog)]
    [InlineData("hybridgateway", EProviderProtocol.HybridGateway)]
    [InlineData("HybridGateway", EProviderProtocol.HybridGateway)]
    [InlineData("decision", EProviderProtocol.Decision)]
    [InlineData("Decision", EProviderProtocol.Decision)]
    [InlineData("native", EProviderProtocol.Native)]
    [InlineData("Native", EProviderProtocol.Native)]
    public void FromJson_MapsAllAliases(string json, EProviderProtocol expected)
    {
        ProviderProtocolMapper.FromJson(json).Should().Be(expected);
    }

    [Fact]
    public void FromJson_UnknownToken_ThrowsJsonException()
    {
        var act = () => ProviderProtocolMapper.FromJson("bogus-protocol");
        act.Should().Throw<JsonException>().WithMessage("*bogus-protocol*");
    }

    [Fact]
    public void RoundTrip_AllEnumValues_SurviveSerializeDeserialize()
    {
        foreach (var protocol in Enum.GetValues<EProviderProtocol>())
        {
            var json = ProviderProtocolMapper.ToJson(protocol);
            var roundTripped = ProviderProtocolMapper.FromJson(json);
            roundTripped.Should().Be(protocol, "protocol {0} must survive a round-trip", protocol);
        }
    }
}
