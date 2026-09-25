using System.Text.Json.Serialization;

using AIProviderConnect.Models;

using CommunityToolkit.Mvvm.ComponentModel;

namespace ScraperTool.Models;

public sealed partial class AiSuggestion : ObservableObject
{
    [ObservableProperty]
    private bool _isApproved;

    [ObservableProperty]
    private bool _isRejected;

    [JsonPropertyName("currentValue")]
    public string? CurrentValue { get; set; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "info";

    [JsonPropertyName("suggestedValue")]
    public string? SuggestedValue { get; set; }
}

