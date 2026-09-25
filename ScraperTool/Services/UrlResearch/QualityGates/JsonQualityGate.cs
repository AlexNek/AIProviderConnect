using System.Text.Json;

using AiCleverness.Abstractions;
using AiCleverness.Models;

namespace ScraperTool.Services.UrlResearch.QualityGates;

/// <summary>
/// Quality gate that validates AI output is valid JSON.
/// When registered, the runtime will retry the LLM if output is malformed JSON.
/// </summary>
public sealed class JsonQualityGate : IAgentQualityGate
{
    public string Name => "JsonValidation";

    public int Priority => 100;

    public bool AppliesTo(IAgentContext context)
    {
        // Only apply when the request indicates JSON output is expected
        return context.State.Data.ContainsKey("expect_json")
               || context.Goal.Contains("JSON", StringComparison.OrdinalIgnoreCase);
    }

    public Task<QualityGateResult> EvaluateAsync(
        AgentResult result,
        IAgentContext context,
        CancellationToken cancellationToken)
    {
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            // A failed run (turn limit exhausted, model error) cannot be fixed by a
            // JSON retry — re-running the whole tool loop only doubles time and tokens
            // (observed: an 8-turn research loop re-ran twice and failed identically).
            // Retry only succeeded runs whose output is missing; failed runs are left
            // to the runtime's failover/recovery machinery. A null reason keeps the
            // original failure message (e.g. "Exhausted N turns...") visible.
            return Task.FromResult(
                new QualityGateResult(
                    Approved: false,
                    Retry: result.Success,
                    Reason: result.Success ? "Agent returned no output" : null));
        }

        var trimmed = result.Output.Trim();

        // Strip markdown code fences if present
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```");
            if (firstNewline >= 0 && lastFence > firstNewline)
            {
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            return Task.FromResult(
                new QualityGateResult(
                    Approved: true,
                    Retry: false,
                    Reason: null));
        }
        catch (JsonException ex)
        {
            return Task.FromResult(
                new QualityGateResult(
                    Approved: false,
                    Retry: true,
                    Reason: $"Output is not valid JSON: {ex.Message}"));
        }
    }
}
