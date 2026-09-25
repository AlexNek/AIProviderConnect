using AiCleverness.Abstractions;
using AiCleverness.Models;
using AiCleverness.Models.DecisionTree;
using AiCleverness.Runtime;
using AiCleverness.Runtime.Conversation;
using AiCleverness.Runtime.DecisionTree;

using FluentAssertions;

using DecisionTreeModel = AiCleverness.Models.DecisionTree.DecisionTree;

namespace ScraperTool.Tests;

/// <summary>
/// Verifies that token usage reported by the LLM pipeline flows through to the decision-tree result.
/// </summary>
public class DecisionTreeTokenUsageTests
{
    [Fact]
    public async Task DecisionTreeExecutor_WhenPipelineReportsUsage_ResultContainsUsage()
    {
        var pipeline = new ScriptedPipeline();
        pipeline.Enqueue(
            "{\"answer\":\"homepage\",\"observation\":\"looks like homepage\"}",
            promptTokens: 42,
            completionTokens: 7);

        var executor = new DecisionTreeExecutor(
            pipeline,
            new DefaultConversationManager(),
            new InMemoryExecutionJournal(),
            null,
            Array.Empty<IDecisionPredicate>(),
            new DefaultDecisionLlmContextBuilder(),
            new DecisionTreeLoader(Array.Empty<IDecisionPredicate>()));

        var tree = new DecisionTreeModel
        {
            TreeId = "tokenTest",
            Version = 1,
            StartNodeId = "classify",
            SystemPrompt = "Classify the page.",
            Task = "Find the homepage.",
            Budget = new DecisionBudget
            {
                MaxNodeVisits = 10,
                MaxLlmCalls = 5,
                MaxContextTokens = 4000
            },
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["classify"] = new()
                {
                    Type = EDecisionNodeType.Classify,
                    Task = "Classify this page.",
                    Answers = new[] { "homepage", "other" },
                    Transitions = new[]
                    {
                        new DecisionTransition { Condition = "homepage", NextNodeId = "win" },
                        new DecisionTransition { Condition = "other", NextNodeId = "lose" },
                        new DecisionTransition { Condition = "unknown", NextNodeId = "lose" }
                    }
                },
                ["win"] = new()
                {
                    Type = EDecisionNodeType.Terminal,
                    Verdict = "winner"
                },
                ["lose"] = new()
                {
                    Type = EDecisionNodeType.Terminal,
                    Verdict = "null"
                }
            }
        };

        // The tree holds no action nodes, so the execution is given an empty action list.
        var result = await executor.ExecuteAsync(Array.Empty<IDecisionAction>(), tree);

        result.Outcome.Should().Be(DecisionTreeOutcome.Terminal, $"error: {result.Error}");
        result.Succeeded.Should().BeTrue();
        result.Usage.InputTokens.Should().Be(42);
        result.Usage.OutputTokens.Should().Be(7);
    }

    private sealed class ScriptedPipeline : ILlmCompletionPipeline
    {
        private readonly Queue<(string Content, int PromptTokens, int CompletionTokens)> _responses = new();

        public void Enqueue(string content, int promptTokens, int completionTokens)
        {
            _responses.Enqueue((content, promptTokens, completionTokens));
        }

        public Task<LlmResponse> CompleteAsync(
            LlmCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_responses.Count == 0)
            {
                return Task.FromResult(new LlmResponse("{\"answer\":\"unknown\"}"));
            }

            var (content, promptTokens, completionTokens) = _responses.Dequeue();
            return Task.FromResult(new LlmResponse(
                content,
                Usage: new LlmTokenUsage(promptTokens, completionTokens)));
        }

        public Task<LlmResponse> CompleteAsync(
            LlmCompletionRequest request,
            LlmCompletionExecutionContext executionContext,
            CancellationToken cancellationToken = default)
            => CompleteAsync(request, cancellationToken);
    }
}
