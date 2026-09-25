using System.Text.Json;

using AIProviderConnect.Models;

using ScraperTool.Models;

namespace ScraperTool.Services;

/// <summary>
/// Result of a patch operation over a set of approved suggestions.
/// </summary>
public sealed record ProviderPatchResult(
    int UpdatedFileCount,
    int AppliedSuggestionCount,
    IReadOnlyList<string> Errors);
