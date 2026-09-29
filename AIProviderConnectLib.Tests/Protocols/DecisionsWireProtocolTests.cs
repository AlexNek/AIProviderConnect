using System.Text.Json;

using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Protocols;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Protocols;

public class DecisionsWireProtocolTests
{
    private static DecisionRequest ChoiceRequest(object? state = null) => new()
    {
        Model = "typesafe/jev-1.13",
        State = state ?? "A support ticket about a billing error.",
        Questions = new Dictionary<string, DecisionQuestion>
        {
            ["priority"] = new()
            {
                Kind = EDecisionQuestionKind.Choice,
                Instructions = "Pick the priority.",
                Criteria = new Dictionary<string, string>
                {
                    ["low"] = "Not urgent",
                    ["high"] = "Urgent"
                }
            }
        }
    };

    [Fact]
    public void MapRequest_Choice_SerializesModelStateAndQuestions()
    {
        // Arrange
        var request = ChoiceRequest();

        // Act
        var body = JsonSerializer.Serialize(DecisionsWireProtocol.MapRequest(request));

        // Assert
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("model").GetString().Should().Be("typesafe/jev-1.13");
        root.GetProperty("state").GetString().Should().Be("A support ticket about a billing error.");
        var question = root.GetProperty("questions").GetProperty("priority");
        question.GetProperty("type").GetString().Should().Be("choice");
        question.GetProperty("instructions").GetString().Should().Be("Pick the priority.");
        question.GetProperty("criteria").GetProperty("high").GetString().Should().Be("Urgent");
    }

    [Fact]
    public void MapRequest_Score_SerializesScaleUnderCriteriaKey()
    {
        // Arrange
        var request = new DecisionRequest
        {
            Model = "m",
            State = "text",
            Questions = new Dictionary<string, DecisionQuestion>
            {
                ["severity"] = new()
                {
                    Kind = EDecisionQuestionKind.Score,
                    Scale = ["low", "medium", "high"]
                }
            }
        };

        // Act
        var body = JsonSerializer.Serialize(DecisionsWireProtocol.MapRequest(request));

        // Assert — the live API sends score levels as an ordered "criteria" array.
        using var doc = JsonDocument.Parse(body);
        var question = doc.RootElement.GetProperty("questions").GetProperty("severity");
        question.GetProperty("type").GetString().Should().Be("score");
        question.TryGetProperty("scale", out _).Should().BeFalse();
        var criteria = question.GetProperty("criteria");
        criteria.ValueKind.Should().Be(JsonValueKind.Array);
        criteria.GetArrayLength().Should().Be(3);
        criteria[0].GetString().Should().Be("low");
    }

    [Fact]
    public void MapRequest_Noul_OmitsCriteriaWhenNotSupplied()
    {
        // Arrange
        var request = new DecisionRequest
        {
            Model = "m",
            State = "text",
            Questions = new Dictionary<string, DecisionQuestion>
            {
                ["is_billing"] = new() { Kind = EDecisionQuestionKind.Noul }
            }
        };

        // Act
        var body = JsonSerializer.Serialize(DecisionsWireProtocol.MapRequest(request));

        // Assert
        using var doc = JsonDocument.Parse(body);
        var question = doc.RootElement.GetProperty("questions").GetProperty("is_billing");
        question.GetProperty("type").GetString().Should().Be("noul");
        question.TryGetProperty("criteria", out _).Should().BeFalse();
    }

    [Fact]
    public void MapRequest_NullState_ThrowsConfigurationError()
    {
        // Arrange
        var request = ChoiceRequest() with { State = null };

        // Act
        var act = () => DecisionsWireProtocol.MapRequest(request);

        // Assert
        act.Should().Throw<AiException>().Which.Code.Should().Be(AiErrorCodes.ConfigurationError);
    }

    [Fact]
    public void MapRequest_EmptyQuestions_ThrowsConfigurationError()
    {
        // Arrange
        var request = ChoiceRequest() with
        {
            Questions = new Dictionary<string, DecisionQuestion>()
        };

        // Act
        var act = () => DecisionsWireProtocol.MapRequest(request);

        // Assert
        act.Should().Throw<AiException>().Which.Code.Should().Be(AiErrorCodes.ConfigurationError);
    }

    [Fact]
    public void MapRequest_ChoiceWithoutCriteria_ThrowsConfigurationError()
    {
        // Arrange
        var request = new DecisionRequest
        {
            Model = "m",
            State = "text",
            Questions = new Dictionary<string, DecisionQuestion>
            {
                ["q"] = new() { Kind = EDecisionQuestionKind.Choice }
            }
        };

        // Act
        var act = () => DecisionsWireProtocol.MapRequest(request);

        // Assert
        act.Should().Throw<AiException>().Which.Code.Should().Be(AiErrorCodes.ConfigurationError);
    }

    [Fact]
    public void MapRequest_ScoreWithSingleLevel_ThrowsConfigurationError()
    {
        // Arrange — Score requires 2–10 levels; one level violates the limit.
        var request = new DecisionRequest
        {
            Model = "m",
            State = "text",
            Questions = new Dictionary<string, DecisionQuestion>
            {
                ["q"] = new() { Kind = EDecisionQuestionKind.Score, Scale = ["only"] }
            }
        };

        // Act
        var act = () => DecisionsWireProtocol.MapRequest(request);

        // Assert
        act.Should().Throw<AiException>().Which.Code.Should().Be(AiErrorCodes.ConfigurationError);
    }

    [Fact]
    public void MapRequest_ScoreWith11Levels_ThrowsConfigurationError()
    {
        // Arrange — Score requires 2–10 levels; eleven levels exceeds the limit.
        var request = new DecisionRequest
        {
            Model = "m",
            State = "text",
            Questions = new Dictionary<string, DecisionQuestion>
            {
                ["q"] = new() { Kind = EDecisionQuestionKind.Score, Scale = ["a","b","c","d","e","f","g","h","i","j","k"] }
            }
        };

        // Act
        var act = () => DecisionsWireProtocol.MapRequest(request);

        // Assert
        act.Should().Throw<AiException>().Which.Code.Should().Be(AiErrorCodes.ConfigurationError);
    }

    [Fact]
    public void MapRequest_UnsupportedStateShape_ThrowsConfigurationError()
    {
        // Arrange — an int is not one of the documented state shapes.
        var request = ChoiceRequest() with { State = 42 };

        // Act
        var act = () => DecisionsWireProtocol.MapRequest(request);

        // Assert
        act.Should().Throw<AiException>().Which.Code.Should().Be(AiErrorCodes.ConfigurationError);
    }

    [Fact]
    public void MapRequest_ObjectState_IsAllowed()
    {
        // Arrange
        IReadOnlyDictionary<string, object?> state = new Dictionary<string, object?>
        {
            ["ticket"] = "billing"
        };
        var request = ChoiceRequest(state);

        // Act
        var body = JsonSerializer.Serialize(DecisionsWireProtocol.MapRequest(request));

        // Assert
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("state").GetProperty("ticket").GetString().Should().Be("billing");
    }

    [Fact]
    public void ParseResponse_LiveShape_RoundTripsEachPrimitive()
    {
        // Arrange — the exact response shape documented by the OpenRouter decisions API.
        const string json = """
        {
            "answers": {
                "is_bug": {
                    "noul": 0.96,
                    "type": "noul"
                },
                "team": {
                    "choice": "payments",
                    "confidence": 0.75,
                    "probabilities": { "account": 0, "frontend": 0.16, "payments": 0.84 },
                    "type": "choice"
                },
                "urgency": {
                    "confidence": 0.99,
                    "legend": { "0": "Can wait", "1": "This week", "2": "Blocking now" },
                    "probabilities": { "0": 0, "1": 0.01, "2": 0.99 },
                    "score": 1.99,
                    "type": "score"
                }
            },
            "id": "gen-dec-1789738314-X5e5eKGQdvR9rblyX250",
            "model": "typesafe/jev-1.13-20260917",
            "provider": "TypeSafe",
            "usage": { "cost": 0.000019992, "input_tokens": 476, "output_tokens": 70 }
        }
        """;

        // Act
        var response = DecisionsWireProtocol.ParseResponse(JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        response.Id.Should().Be("gen-dec-1789738314-X5e5eKGQdvR9rblyX250");
        response.Model.Should().Be("typesafe/jev-1.13-20260917");
        response.Provider.Should().Be("TypeSafe");
        response.Answers.Should().HaveCount(3);

        var noul = response.Answers["is_bug"];
        noul.Kind.Should().Be(EDecisionAnswerKind.Noul);
        noul.Noul!.ProbabilityOfYes.Should().Be(0.96);

        var choice = response.Answers["team"];
        choice.Kind.Should().Be(EDecisionAnswerKind.Choice);
        choice.Choice!.Selected.Should().Be("payments");
        choice.Choice.Confidence.Should().Be(0.75);
        choice.Choice.Probabilities["payments"].Should().Be(0.84);

        var score = response.Answers["urgency"];
        score.Kind.Should().Be(EDecisionAnswerKind.Score);
        score.Score!.Score.Should().Be(1.99);
        score.Score.Confidence.Should().Be(0.99);
        score.Score.LevelProbabilities["2"].Should().Be(0.99);
        // Legend is returned in ascending index order.
        score.Score.Legend.Should().Equal("Can wait", "This week", "Blocking now");
    }

    [Fact]
    public void ParseResponse_UnknownAnswerType_ParsesToUnknownWithoutFailing()
    {
        // Arrange
        const string json = """
        {
            "answers": {
                "future": { "type": "quantum", "value": 1 },
                "priority": { "type": "noul", "noul": 0.5 }
            }
        }
        """;

        // Act
        var response = DecisionsWireProtocol.ParseResponse(JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        response.Answers["future"].Kind.Should().Be(EDecisionAnswerKind.Unknown);
        response.Answers["priority"].Kind.Should().Be(EDecisionAnswerKind.Noul);
    }

    [Fact]
    public void ParseResponse_UsageWithoutCost_LeavesCostNullAndDerivesTotal()
    {
        // Arrange — the wire carries no total-tokens key and no cost.
        const string json = """
        {
            "answers": {},
            "usage": { "input_tokens": 30, "output_tokens": 12 }
        }
        """;

        // Act
        var response = DecisionsWireProtocol.ParseResponse(JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        response.Usage.PromptTokens.Should().Be(30);
        response.Usage.CompletionTokens.Should().Be(12);
        response.Usage.TotalTokens.Should().Be(42);
        response.Usage.Cost.Should().BeNull();
    }

    [Fact]
    public void MapRequest_ArrayOfTextState_IsAllowed()
    {
        // Arrange
        IReadOnlyList<string> state = new List<string> { "text1", "text2" };
        var request = ChoiceRequest(state);

        // Act
        var body = JsonSerializer.Serialize(DecisionsWireProtocol.MapRequest(request));

        // Assert
        using var doc = JsonDocument.Parse(body);
        var arr = doc.RootElement.GetProperty("state");
        arr.ValueKind.Should().Be(JsonValueKind.Array);
        arr.GetArrayLength().Should().Be(2);
        arr[0].GetString().Should().Be("text1");
        arr[1].GetString().Should().Be("text2");
    }

    [Fact]
    public void ApplyProtocolConfiguration_ReadsEndpointAndBaseUrl()
    {
        // Arrange
        var options = new DecisionProviderOptions
        {
            ProtocolConfiguration = new Dictionary<string, string>
            {
                ["decisionsEndpoint"] = "custom/decisions",
                ["decisionsBaseUrl"] = "https://decisions.example.com/"
            }
        };

        // Act
        DecisionsWireProtocol.ApplyProtocolConfiguration(options);

        // Assert
        options.DecisionsEndpoint.Should().Be("custom/decisions");
        options.DecisionsBaseUrl.Should().Be("https://decisions.example.com/");
    }
}
