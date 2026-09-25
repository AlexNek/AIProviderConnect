using System.Text.Json.Serialization;

namespace ScraperTool.Models;

public sealed class ExportData
{
    [JsonPropertyName("exportedAt")]
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("results")]
    public IReadOnlyList<ProviderPriceResult> Results { get; set; } =
        Array.Empty<ProviderPriceResult>();
}
