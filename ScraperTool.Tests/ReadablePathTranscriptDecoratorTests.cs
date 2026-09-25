using AiCleverness.Models;
using AiCleverness.Models.DecisionTree;

using FluentAssertions;

using ScraperTool.Services.UrlResearch.DecisionTree.Transcript;

namespace ScraperTool.Tests;

public class ReadablePathTranscriptDecoratorTests
{
    private static string Render(ReadablePathTranscriptDecorator sut, IReadOnlyList<string> path)
        => sut.DecisionResult(
            DecisionTreeOutcome.Terminal,
            succeeded: true,
            verdict: null,
            error: null,
            new ResourceUsage(),
            path);

    [Fact]
    public void DecisionResult_ActionNode_PrependsReadableSummaryAboveTechnicalLine()
    {
        var sut = new ReadablePathTranscriptDecorator();
        sut.DecisionAction(
            "web-search", "webSearch", "Web Search",
            DecisionActionStatus.Success, "5 candidates found", null, null);

        var result = Render(
            sut,
            ["`web-search` (Action, 24386ms) -- `success` --> `has-search-results`"]);

        result.Should().Contain("1. Web Search — 5 candidates found");
        result.Should().Contain("   `web-search` (Action, 24386ms) -- `success` --> `has-search-results`");
    }

    [Fact]
    public void DecisionResult_TwoVisitsToSameActionNode_ProduceDistinctSummaries()
    {
        var sut = new ReadablePathTranscriptDecorator();
        sut.DecisionAction(
            "fetch-models-page", "fetchModelsPage", "Fetch Models Page",
            DecisionActionStatus.TransientFailure, "tried https://test.example.com/models — not found", null, null);
        sut.DecisionAction(
            "fetch-models-page", "fetchModelsPage", "Fetch Models Page",
            DecisionActionStatus.PermanentFailure, "already attempted, falling back", null, null);

        var result = Render(
            sut,
            [
                "`fetch-models-page` (Action, 10624ms) -- `transientFailure` --> `fetch-models-page`",
                "`fetch-models-page` (Action, 0ms) -- `permanentFailure` --> `web-search`"
            ]);

        result.Should().Contain("tried https://test.example.com/models — not found");
        result.Should().Contain("already attempted, falling back");
    }

    [Fact]
    public void DecisionResult_PredicateConditionNode_RendersTitleCasedIdAndOutcome()
    {
        var sut = new ReadablePathTranscriptDecorator();

        // A predicate Condition node emits no DecisionClassification callback; the outcome
        // exists only in the rendered path entry.
        var result = Render(
            sut,
            ["`has-search-results` (Condition, 1ms) [Predicate: hasCandidates] -- `true` --> `classify-model-count`"]);

        result.Should().Contain("1. Has Search Results → true");
        result.Should().Contain("   `has-search-results` (Condition, 1ms)");
    }

    [Fact]
    public void DecisionResult_ClassifyNode_RendersCapturedAnswer()
    {
        var sut = new ReadablePathTranscriptDecorator();
        sut.DecisionClassification("classify-model-count", "many", null, null, 1);

        var result = Render(
            sut,
            ["`classify-model-count` (Classify, 800ms) -- `many` --> `done`"]);

        result.Should().Contain("1. Classify Model Count → many");
        result.Should().Contain("   `classify-model-count` (Classify, 800ms)");
    }

    [Fact]
    public void DecisionResult_UncapturedNode_PassesEntryThroughUnchanged()
    {
        var sut = new ReadablePathTranscriptDecorator();

        var result = Render(
            sut,
            ["`start` (Start, 0ms) -- `begin` --> `web-search`"]);

        result.Should().Contain("1. `start` (Start, 0ms) -- `begin` --> `web-search`");
        result.Should().NotContain("   `start`");
    }

    [Fact]
    public void DecisionResult_FailureWithoutSummary_UsesStatusTextAndError()
    {
        var sut = new ReadablePathTranscriptDecorator();
        sut.DecisionAction(
            "scan", "scanLinks", null,
            DecisionActionStatus.TransientFailure, null, "timeout", null);

        var result = Render(
            sut,
            ["`scan` (Action, 5ms) -- `transientFailure` --> `web-search`"]);

        result.Should().Contain("1. scanLinks — failed: timeout");
    }
}
