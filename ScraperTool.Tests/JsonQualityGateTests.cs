using AiCleverness.Abstractions;
using AiCleverness.Models;

using FluentAssertions;

using Moq;

using ScraperTool.Services.UrlResearch.QualityGates;

namespace ScraperTool.Tests;

public class JsonQualityGateTests
{
    private readonly JsonQualityGate _gate = new();
    private readonly Mock<IAgentContext> _context = new();

    [Fact]
    public async Task EvaluateAsync_WhenRunFailed_DoesNotRequestRetry()
    {
        // Arrange — a failed run (e.g. turn limit exhausted) must not re-run the loop
        var result = new AgentResult(
            false,
            Output: null,
            Reasoning: "Exhausted 8 turns without a final response.",
            FailureKind: EFailureKind.TurnLimitExceeded);

        // Act
        var gateResult = await _gate.EvaluateAsync(result, _context.Object, CancellationToken.None);

        // Assert — no retry, and no reason so the original failure stays visible
        gateResult.Approved.Should().BeFalse();
        gateResult.Retry.Should().BeFalse();
        gateResult.Reason.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAsync_WhenSucceededButEmptyOutput_RequestsRetry()
    {
        // Arrange — the model answered nothing despite a successful run
        var result = new AgentResult(true, Output: "   ");

        // Act
        var gateResult = await _gate.EvaluateAsync(result, _context.Object, CancellationToken.None);

        // Assert
        gateResult.Approved.Should().BeFalse();
        gateResult.Retry.Should().BeTrue();
        gateResult.Reason.Should().Be("Agent returned no output");
    }

    [Fact]
    public async Task EvaluateAsync_WhenValidJson_Approves()
    {
        // Arrange
        var result = new AgentResult(
            true,
            Output: "{\"suggestedValue\": \"https://test.example.com\", \"reason\": \"ok\"}");

        // Act
        var gateResult = await _gate.EvaluateAsync(result, _context.Object, CancellationToken.None);

        // Assert
        gateResult.Approved.Should().BeTrue();
        gateResult.Retry.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WhenJsonInsideCodeFence_Approves()
    {
        // Arrange
        var result = new AgentResult(
            true,
            Output: "```json\n{\"suggestedValue\": \"https://test.example.com\"}\n```");

        // Act
        var gateResult = await _gate.EvaluateAsync(result, _context.Object, CancellationToken.None);

        // Assert
        gateResult.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_WhenMalformedJson_RequestsRetry()
    {
        // Arrange — the model answered, but the JSON is broken
        var result = new AgentResult(true, Output: "{\"suggestedValue\": ");

        // Act
        var gateResult = await _gate.EvaluateAsync(result, _context.Object, CancellationToken.None);

        // Assert
        gateResult.Approved.Should().BeFalse();
        gateResult.Retry.Should().BeTrue();
        gateResult.Reason.Should().StartWith("Output is not valid JSON");
    }
}
