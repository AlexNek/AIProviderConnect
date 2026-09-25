using System.Text.Json;

using AIProviderConnect.Models;

using FluentAssertions;

namespace AIProviderConnect.Tests.Models;

public class EModelPriceUnitJsonConverterTests
{
    [Theory]
    [InlineData(EModelPriceUnit.Per1M, "\"Per1M\"")]
    [InlineData(EModelPriceUnit.Per1K, "\"Per1K\"")]
    public void Serialize_ProducesStringRepresentation(EModelPriceUnit value, string expectedJson)
    {
        // Act
        var json = JsonSerializer.Serialize(value);

        // Assert
        json.Should().Be(expectedJson);
    }

    [Theory]
    [InlineData("\"Per1M\"", EModelPriceUnit.Per1M)]
    [InlineData("\"Per1K\"", EModelPriceUnit.Per1K)]
    public void Deserialize_ParsesStringRepresentation(string json, EModelPriceUnit expected)
    {
        // Act
        var value = JsonSerializer.Deserialize<EModelPriceUnit>(json);

        // Assert
        value.Should().Be(expected);
    }

    [Fact]
    public void Deserialize_IntegerValue_Throws()
    {
        // Act
        var act = () => JsonSerializer.Deserialize<EModelPriceUnit>("0");

        // Assert
        act.Should().Throw<JsonException>("allowIntegerValues is false");
    }

    [Fact]
    public void RoundTrip_PreservesValue()
    {
        // Arrange
        var original = EModelPriceUnit.Per1K;

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<EModelPriceUnit>(json);

        // Assert
        deserialized.Should().Be(original);
    }

    [Fact]
    public void AIModel_PriceUnit_RoundTrips()
    {
        // Arrange
        var model = new AIModel
        {
            Id = "test-model",
            DisplayName = "Test",
            PriceUnit = EModelPriceUnit.Per1K
        };

        // Act
        var json = JsonSerializer.Serialize(model);
        var deserialized = JsonSerializer.Deserialize<AIModel>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.PriceUnit.Should().Be(EModelPriceUnit.Per1K);
    }
}
