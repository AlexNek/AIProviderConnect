using System.Text.Json;

using AIProviderConnect.Models;

using FluentAssertions;

namespace AIProviderConnect.Tests.Models;

public class ChatModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [Fact]
    public void ChatMessage_SerializeAndDeserialize_RoundTrip()
    {
        // Arrange
        var message = new ChatMessage
        {
            Role = EChatRole.User,
            Content = "Hello, world!"
        };

        // Act
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ChatMessage>(json, JsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Role.Should().Be(EChatRole.User);
        deserialized.Content.Should().Be("Hello, world!");
    }

    [Theory]
    [InlineData(EChatRole.User)]
    [InlineData(EChatRole.Assistant)]
    [InlineData(EChatRole.System)]
    [InlineData(EChatRole.Tool)]
    public void ChatRole_SerializesAsString(EChatRole role)
    {
        // Arrange
        var message = new ChatMessage
        {
            Role = role,
            Content = "Test"
        };

        // Act
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ChatMessage>(json, JsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Role.Should().Be(role);
    }

    [Fact]
    public void ChatCompletionRequest_WithMinimalFields_Serializes()
    {
        // Arrange
        var request = new ChatCompletionRequest
        {
            Model = "gpt-4",
            Messages = new List<ChatMessage>
            {
                new() { Role = EChatRole.User, Content = "Hello" }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(request, JsonOptions);

        // Assert
        json.Should().Contain("Model");
        json.Should().Contain("gpt-4");
        json.Should().Contain("Messages");
    }

    [Fact]
    public void ChatCompletionRequest_WithTemperature_Serializes()
    {
        // Arrange
        var request = new ChatCompletionRequest
        {
            Model = "gpt-4",
            Messages = new List<ChatMessage>
            {
                new() { Role = EChatRole.User, Content = "Hi" }
            },
            Temperature = 0.7f,
            MaxTokens = 1000
        };

        // Act
        var json = JsonSerializer.Serialize(request, JsonOptions);

        // Assert
        json.Should().Contain("Temperature");
        json.Should().Contain("MaxTokens");
    }

    [Fact]
    public void UsageInfo_CanBeCreated()
    {
        // Arrange & Act
        var usage = new UsageInfo
        {
            PromptTokens = 100,
            CompletionTokens = 50,
            TotalTokens = 150
        };

        // Assert
        usage.PromptTokens.Should().Be(100);
        usage.CompletionTokens.Should().Be(50);
        usage.TotalTokens.Should().Be(150);
    }

    [Fact]
    public void AIModel_WithRequiredFields_CanBeCreated()
    {
        // Arrange & Act
        var model = new AIModel
        {
            Id = "gpt-4",
            DisplayName = "GPT-4"
        };

        // Assert
        model.Id.Should().Be("gpt-4");
        model.DisplayName.Should().Be("GPT-4");
        model.PriceUnit.Should().Be(EModelPriceUnit.Per1M); // Default value
    }

    [Fact]
    public void AIModel_WithAllFields_CanBeCreated()
    {
        // Arrange & Act
        var model = new AIModel
        {
            Id = "gpt-4",
            DisplayName = "GPT-4",
            Description = "Advanced language model",
            OwnedBy = "openai",
            ProviderId = "openai",
            ContextWindow = 8192,
            PromptPrice = 10.00m,
            CompletionPrice = 30.00m,
            Modality = "text->text"
        };

        // Assert
        model.Id.Should().Be("gpt-4");
        model.DisplayName.Should().Be("GPT-4");
        model.Description.Should().Be("Advanced language model");
        model.OwnedBy.Should().Be("openai");
        model.ProviderId.Should().Be("openai");
        model.ContextWindow.Should().Be(8192);
        model.PromptPrice.Should().Be(10.00m);
        model.CompletionPrice.Should().Be(30.00m);
        model.Modality.Should().Be("text->text");
    }

    [Fact]
    public void ToolDefinition_WithRequiredFields_CanBeCreated()
    {
        // Arrange
        var json = "{}";
        var schemaElement = JsonDocument.Parse(json).RootElement;

        // Act
        var tool = new ToolDefinition
        {
            Name = "get_weather",
            Description = "Get weather for location",
            Parameters = schemaElement
        };

        // Assert
        tool.Name.Should().Be("get_weather");
        tool.Description.Should().Be("Get weather for location");
    }

    [Fact]
    public void ResponseFormat_Text_CanBeCreated()
    {
        // Arrange & Act
        var format = new ResponseFormat
        {
            Type = "text"
        };

        // Assert
        format.Type.Should().Be("text");
    }

    [Fact]
    public void ResponseFormat_Json_CanBeCreated()
    {
        // Arrange & Act
        var format = new ResponseFormat
        {
            Type = "json_object"
        };

        // Assert
        format.Type.Should().Be("json_object");
    }

    [Fact]
    public void EProviderProtocol_Enum_HasAllExpectedValues()
    {
        // Arrange & Act
        var values = Enum.GetValues<EProviderProtocol>();

        // Assert
        values.Should().Contain(EProviderProtocol.Native);
        values.Should().Contain(EProviderProtocol.OpenAICompatible);
        values.Should().Contain(EProviderProtocol.MessagesApi);
        values.Should().Contain(EProviderProtocol.KeyQuery);
        values.Should().Contain(EProviderProtocol.Catalog);
        values.Should().Contain(EProviderProtocol.HybridGateway);
    }
}
