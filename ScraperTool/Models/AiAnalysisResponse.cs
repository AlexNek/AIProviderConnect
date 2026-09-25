using System.Text.Json.Serialization;

namespace ScraperTool.Models;

public sealed class AiAnalysisResponse
{
    [JsonPropertyName("apiPricingUrl")]
    public string? ApiPricingUrl { get; set; }

    [JsonPropertyName("completionCellIndex")]
    public int? CompletionCellIndex { get; set; }

    [JsonPropertyName("modelCellIndex")]
    public int? ModelCellIndex { get; set; }

    [JsonPropertyName("priceUnit")]
    public string? PriceUnit { get; set; }

    [JsonPropertyName("promptCellIndex")]
    public int? PromptCellIndex { get; set; }

    [JsonPropertyName("rowOffset")]
    public int? RowOffset { get; set; }

    [JsonPropertyName("subscriptionPricingUrl")]
    public string? SubscriptionPricingUrl { get; set; }

    [JsonPropertyName("suggestions")]
    public List<AiSuggestionDto> Suggestions { get; set; } = [];

    [JsonPropertyName("tableXPath")]
    public string? TableXPath { get; set; }
}
