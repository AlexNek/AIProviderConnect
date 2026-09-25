using AiCleverness.Models.DecisionTree;

using FluentAssertions;

using ScraperTool.Services.UrlResearch.DecisionTree.Adapters;

namespace ScraperTool.Tests;

public class DecisionTreeProgressAdapterTests
{
    private static (DecisionTreeProgressAdapter Adapter, ExecutionSessionTracker Tracker) CreateSut(
        List<string> reported)
    {
        var tracker = new ExecutionSessionTracker();
        var progress = new Progress<string>(m => reported.Add(m));
        tracker.Begin("test-tree", progress);
        var adapter = new DecisionTreeProgressAdapter(tracker);
        return (adapter, tracker);
    }

    [Fact]
    public async Task HandleAsync_ActionNode_ReportsProgress()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var evt = new DecisionNodeVisitedBusEvent(
            "exec-1", "node-1", EDecisionNodeType.Action, TimeSpan.FromMilliseconds(50), "success");

        await adapter.HandleAsync(evt);
        await Task.Delay(50);

        reported.Should().HaveCount(1);
        reported[0].Should().Contain("[Action]");
        reported[0].Should().Contain("node-1");
        reported[0].Should().Contain("50ms");

        tracker.End();
    }

    [Fact]
    public async Task HandleAsync_ConditionNode_ReportsProgress()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var evt = new DecisionNodeVisitedBusEvent(
            "exec-1", "cond-1", EDecisionNodeType.Condition, TimeSpan.FromMilliseconds(10), "true");

        await adapter.HandleAsync(evt);
        await Task.Delay(50);

        reported.Should().HaveCount(1);
        reported[0].Should().Contain("[Condition]");
        reported[0].Should().Contain("cond-1");

        tracker.End();
    }

    [Fact]
    public async Task HandleAsync_TerminalNode_ReportsProgress()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var evt = new DecisionNodeVisitedBusEvent(
            "exec-1", "end-1", EDecisionNodeType.Terminal, TimeSpan.FromMilliseconds(5), "skip");

        await adapter.HandleAsync(evt);
        await Task.Delay(50);

        reported.Should().HaveCount(1);
        reported[0].Should().Contain("[Terminal]");
        reported[0].Should().Contain("end-1");

        tracker.End();
    }

    [Fact]
    public async Task HandleAsync_NoActiveSession_DoesNotThrow()
    {
        var tracker = new ExecutionSessionTracker();
        var adapter = new DecisionTreeProgressAdapter(tracker);

        var evt = new DecisionNodeVisitedBusEvent(
            "exec-1", "node-1", EDecisionNodeType.Action, TimeSpan.FromMilliseconds(50), null);

        var act = () => adapter.HandleAsync(evt);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HandleAsync_IncludesDurationInMessage()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var evt = new DecisionNodeVisitedBusEvent(
            "exec-1", "node-1", EDecisionNodeType.Action, TimeSpan.FromMilliseconds(123), "success");

        await adapter.HandleAsync(evt);
        await Task.Delay(50);

        reported.Should().HaveCount(1);
        reported[0].Should().Contain("123ms");
        reported[0].Should().Contain("Action");

        tracker.End();
    }

    // Phase 1: Classify NodeVisited events are suppressed (duplicate line removal).
    [Fact]
    public async Task HandleAsync_ClassifyNodeVisited_DoesNotReport()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var evt = new DecisionNodeVisitedBusEvent(
            "exec-1", "classify-1", EDecisionNodeType.Classify, TimeSpan.FromMilliseconds(30), "unknown");

        await adapter.HandleAsync(evt);
        await Task.Delay(50);

        reported.Should().BeEmpty();

        tracker.End();
    }

    // Phase 2: Retry detection — second action visit with failure gets retry prefix.
    [Fact]
    public async Task HandleAsync_RetryActionFailure_PrefixesRetryIndicator()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var first = new DecisionNodeVisitedBusEvent(
            "exec-1", "fetch-page", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(11000), "transientFailure");
        var second = new DecisionNodeVisitedBusEvent(
            "exec-1", "fetch-page", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(500), "transientFailure");

        await adapter.HandleAsync(first);
        await adapter.HandleAsync(second);
        await Task.Delay(50);

        reported.Should().HaveCount(2);
        reported[0].Should().NotContain("retry");
        reported[1].Should().Contain("↻ retry,");

        tracker.End();
    }

    [Fact]
    public async Task HandleAsync_RetryActionSuccess_NoRetryPrefix()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var first = new DecisionNodeVisitedBusEvent(
            "exec-1", "fetch-page", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(100), "success");
        var second = new DecisionNodeVisitedBusEvent(
            "exec-1", "fetch-page", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(100), "success");

        await adapter.HandleAsync(first);
        await adapter.HandleAsync(second);
        await Task.Delay(50);

        reported.Should().HaveCount(2);
        reported[0].Should().NotContain("retry");
        reported[1].Should().NotContain("retry");

        tracker.End();
    }

    // Phase 3: Error detail from DecisionActionCompletedBusEvent.
    [Fact]
    public async Task HandleAsync_ActionError_AppendsErrorDetail()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var actionCompleted = new DecisionActionCompletedBusEvent(
            "exec-1", "fetch-page", "fetchPage", DecisionActionStatus.TransientFailure,
            Error: "Request timed out");
        var nodeVisited = new DecisionNodeVisitedBusEvent(
            "exec-1", "fetch-page", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(11000), "transientFailure");

        await adapter.HandleAsync(actionCompleted);
        await adapter.HandleAsync(nodeVisited);
        await Task.Delay(50);

        reported.Should().HaveCount(1);
        reported[0].Should().Contain("— Request timed out");

        tracker.End();
    }

    // Phase 4: Misleading HTTP observations are replaced.
    [Fact]
    public async Task HandleAsync_ClassifyWithHttpObservation_ReplacesWithLabel()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var evt = new DecisionClassificationCompletedBusEvent(
            "exec-1", "classify-count", "unknown", "HTTP 404", null, 1);

        await adapter.HandleAsync(evt);
        await Task.Delay(50);

        reported.Should().HaveCount(1);
        reported[0].Should().Contain("[Classify]");
        reported[0].Should().Contain("LLM could not determine the answer");
        reported[0].Should().NotContain("HTTP 404");

        tracker.End();
    }

    [Fact]
    public async Task HandleAsync_ClassifyWithMeaningfulObservation_PreservesIt()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var evt = new DecisionClassificationCompletedBusEvent(
            "exec-1", "classify-count", "15", "Found 15 models listed", "high", 1);

        await adapter.HandleAsync(evt);
        await Task.Delay(50);

        reported.Should().HaveCount(1);
        reported[0].Should().Contain("Found 15 models listed");

        tracker.End();
    }

    [Fact]
    public async Task HandleAsync_ClassificationCompleted_UsesClassifyLabel()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var evt = new DecisionClassificationCompletedBusEvent(
            "exec-1", "classify-page", "api_pricing", "Found API pricing table", "high", 1);

        await adapter.HandleAsync(evt);
        await Task.Delay(50);

        reported.Should().HaveCount(1);
        reported[0].Should().Contain("[Classify]");
        reported[0].Should().Contain("classify-page");
        reported[0].Should().Contain("api_pricing");
        reported[0].Should().Contain("attempt 1");

        tracker.End();
    }

    [Fact]
    public async Task HandleAsync_ClassificationNoObservation_OmitsDash()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var evt = new DecisionClassificationCompletedBusEvent(
            "exec-1", "classify-page", "none", null, null, 1);

        await adapter.HandleAsync(evt);
        await Task.Delay(50);

        reported.Should().HaveCount(1);
        reported[0].Should().Contain("→ none");
        reported[0].Should().NotContain("—");

        tracker.End();
    }

    // State resets between executions.
    [Fact]
    public async Task HandleAsync_NewExecution_ResetsRetryCounts()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        // First execution: action visited once with failure
        var exec1 = new DecisionNodeVisitedBusEvent(
            "exec-1", "fetch", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(100), "transientFailure");
        await adapter.HandleAsync(exec1);

        // Second execution: same node, first visit — should NOT be a retry
        var exec2 = new DecisionNodeVisitedBusEvent(
            "exec-2", "fetch", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(100), "transientFailure");
        await adapter.HandleAsync(exec2);
        await Task.Delay(50);

        reported.Should().HaveCount(2);
        reported[0].Should().NotContain("retry");
        reported[1].Should().NotContain("retry");

        tracker.End();
    }

    // Feature 18: bounded data summaries reported by the actions themselves.
    [Fact]
    public async Task HandleAsync_ActionDataSummary_ReportsItemCountAndPreviews()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var actionCompleted = new DecisionActionCompletedBusEvent(
            "exec-1", "scan-links", "scanProviderLinks", DecisionActionStatus.Success)
        {
            DataSummary = new DecisionActionDataSummary(
                2,
                ["url"],
                ["https://test.example.com/pricing", "https://test.example.com/models"])
        };
        var nodeVisited = new DecisionNodeVisitedBusEvent(
            "exec-1", "scan-links", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(200), "success");

        await adapter.HandleAsync(actionCompleted);
        await adapter.HandleAsync(nodeVisited);
        await Task.Delay(50);

        reported.Should().HaveCount(1);
        reported[0].Should().Contain(
            "— extracted 2 items: https://test.example.com/pricing, https://test.example.com/models");
        reported[0].Should().NotContain("…", "every produced item is listed");

        tracker.End();
    }

    [Fact]
    public async Task HandleAsync_DataSummaryWithMoreItemsThanPreviews_AppendsEllipsis()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        // The library bounds the preview list, so a count above it means items are not shown.
        var actionCompleted = new DecisionActionCompletedBusEvent(
            "exec-1", "scan-links", "scanProviderLinks", DecisionActionStatus.Success)
        {
            DataSummary = new DecisionActionDataSummary(
                7,
                ["url"],
                [
                    "https://test.example.com/p1", "https://test.example.com/p2",
                    "https://test.example.com/p3", "https://test.example.com/p4",
                    "https://test.example.com/p5"
                ])
        };
        var nodeVisited = new DecisionNodeVisitedBusEvent(
            "exec-1", "scan-links", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(200), "success");

        await adapter.HandleAsync(actionCompleted);
        await adapter.HandleAsync(nodeVisited);
        await Task.Delay(50);

        reported.Should().HaveCount(1);
        reported[0].Should().Contain("— extracted 7 items:");
        reported[0].Should().Contain("https://test.example.com/p5, …");

        tracker.End();
    }

    [Fact]
    public async Task HandleAsync_ActionWithoutDataSummary_UsesStaticActionDetail()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var actionCompleted = new DecisionActionCompletedBusEvent(
            "exec-1", "scan-links", "scanProviderLinks", DecisionActionStatus.Success);
        var nodeVisited = new DecisionNodeVisitedBusEvent(
            "exec-1", "scan-links", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(200), "success");

        await adapter.HandleAsync(actionCompleted);
        await adapter.HandleAsync(nodeVisited);
        await Task.Delay(50);

        reported.Should().HaveCount(1);
        reported[0].Should().Contain("— scanned page links");
        reported[0].Should().NotContain("extracted");

        tracker.End();
    }

    [Fact]
    public async Task HandleAsync_DataSummary_IsReportedOnlyForTheProducingNode()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var scanCompleted = new DecisionActionCompletedBusEvent(
            "exec-1", "scan-links", "scanProviderLinks", DecisionActionStatus.Success)
        {
            DataSummary = new DecisionActionDataSummary(
                1, ["url"], ["https://test.example.com/pricing"])
        };
        var fetchCompleted = new DecisionActionCompletedBusEvent(
            "exec-1", "fetch-page", "fetchNextCandidate", DecisionActionStatus.Success)
        {
            DataSummary = new DecisionActionDataSummary(
                1, ["html"], ["https://test.example.com/models"])
        };
        var scanVisited = new DecisionNodeVisitedBusEvent(
            "exec-1", "scan-links", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(120), "success");
        var fetchVisited = new DecisionNodeVisitedBusEvent(
            "exec-1", "fetch-page", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(80), "success");

        await adapter.HandleAsync(scanCompleted);
        await adapter.HandleAsync(fetchCompleted);
        await adapter.HandleAsync(scanVisited);
        await adapter.HandleAsync(fetchVisited);
        await Task.Delay(50);

        reported.Should().HaveCount(2);
        reported[0].Should().Contain("— extracted 1 item: https://test.example.com/pricing");
        reported[0].Should().NotContain("https://test.example.com/models");
        reported[1].Should().Contain("— extracted 1 item: https://test.example.com/models");
        reported[1].Should().NotContain("https://test.example.com/pricing");

        tracker.End();
    }

    [Fact]
    public async Task HandleAsync_ConsumedDataSummary_DoesNotAffectALaterVisit()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var actionCompleted = new DecisionActionCompletedBusEvent(
            "exec-1", "scan-links", "scanProviderLinks", DecisionActionStatus.Success)
        {
            DataSummary = new DecisionActionDataSummary(
                2, ["url"], ["https://test.example.com/a", "https://test.example.com/b"])
        };
        var firstVisit = new DecisionNodeVisitedBusEvent(
            "exec-1", "scan-links", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(200), "success");
        var secondVisit = new DecisionNodeVisitedBusEvent(
            "exec-1", "scan-links", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(150), "success");

        await adapter.HandleAsync(actionCompleted);
        await adapter.HandleAsync(firstVisit);
        await adapter.HandleAsync(secondVisit);
        await Task.Delay(50);

        reported.Should().HaveCount(2);
        reported[0].Should().Contain("— extracted 2 items:");
        reported[1].Should().NotContain("extracted", "the summary was consumed by the first visit");
        reported[1].Should().Contain("— scanned page links");

        tracker.End();
    }

    [Fact]
    public async Task HandleAsync_NullDataSummary_DropsThePreviousSummaryForThatNode()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var firstAttempt = new DecisionActionCompletedBusEvent(
            "exec-1", "scan-links", "scanProviderLinks", DecisionActionStatus.Success)
        {
            DataSummary = new DecisionActionDataSummary(
                2, ["url"], ["https://test.example.com/a", "https://test.example.com/b"])
        };
        // A retry that produced nothing must not inherit the first attempt's summary.
        var secondAttempt = new DecisionActionCompletedBusEvent(
            "exec-1", "scan-links", "scanProviderLinks", DecisionActionStatus.TransientFailure,
            Error: "Request timed out");
        var nodeVisited = new DecisionNodeVisitedBusEvent(
            "exec-1", "scan-links", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(11000), "transientFailure");

        await adapter.HandleAsync(firstAttempt);
        await adapter.HandleAsync(secondAttempt);
        await adapter.HandleAsync(nodeVisited);
        await Task.Delay(50);

        reported.Should().HaveCount(1);
        reported[0].Should().Contain("— Request timed out");
        reported[0].Should().NotContain("extracted");

        tracker.End();
    }

    [Fact]
    public async Task HandleAsync_SeparateExecutions_DoNotReuseSummaries()
    {
        var reported = new List<string>();
        var (adapter, tracker) = CreateSut(reported);

        var exec1Completed = new DecisionActionCompletedBusEvent(
            "exec-1", "scan-links", "scanProviderLinks", DecisionActionStatus.Success)
        {
            DataSummary = new DecisionActionDataSummary(
                2, ["url"], ["https://test.example.com/a", "https://test.example.com/b"])
        };
        await adapter.HandleAsync(exec1Completed);

        // The second execution visits the same node id but reports no summary of its own.
        var exec2Visited = new DecisionNodeVisitedBusEvent(
            "exec-2", "scan-links", EDecisionNodeType.Action,
            TimeSpan.FromMilliseconds(90), "success");
        await adapter.HandleAsync(exec2Visited);
        await Task.Delay(50);

        reported.Should().HaveCount(1);
        reported[0].Should().NotContain("extracted");

        tracker.End();
    }
}
