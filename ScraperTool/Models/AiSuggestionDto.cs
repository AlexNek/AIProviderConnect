using System.Text.Json.Serialization;

namespace ScraperTool.Models;

public sealed class AiSuggestionDto
{
    [JsonPropertyName("currentValue")]
    public string? CurrentValue { get; set; }

    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "info";

    [JsonPropertyName("suggestedValue")]
    public string? SuggestedValue { get; set; }
}
