using System.ComponentModel;

using AIProviderConnect.Models;

namespace ScraperTool.Models;

public sealed class ProviderSelectionItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private DateTime? _lastValidatedAt;

    private ValidationLevel _validationLevel = ValidationLevel.NotValidated;

    public string ApiPricingUrl { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public required string DisplayName { get; set; }

    public bool HasFreeTier { get; set; }

    public bool HasModelDiscovery { get; set; }

    /// <summary>
    /// Gets whether this provider has regional endpoint variants.
    /// </summary>
    public bool HasRegionalEndpoints => RegionalEndpointsDisplay is { Length: > 0 };

    public bool IsConfigured { get; set; }

    /// <summary>
    /// Gets or sets the last validation timestamp.
    /// </summary>
    public DateTime? LastValidatedAt
    {
        get => _lastValidatedAt;
        set
        {
            if (_lastValidatedAt == value) return;
            _lastValidatedAt = value;
            OnPropertyChanged(nameof(LastValidatedAt));
            OnPropertyChanged(nameof(ValidationDisplay));
        }
    }

    public string ModelDescription { get; set; } = string.Empty;

    public bool PayAsYouGo { get; set; }

    public string Protocol { get; set; } = string.Empty;

    public required string ProviderId { get; set; }

    /// <summary>
    /// Gets the regional endpoints as a display string, or null if none.
    /// </summary>
    public string? RegionalEndpointsDisplay { get; set; }

    public string Summary
    {
        get
        {
            var line1 = Category is { Length: > 0 } ? $"{Category} | {Protocol}" : Protocol;
            if (ModelDescription is { Length: > 0 })
                return $"{line1} — {ModelDescription}";
            return line1;
        }
    }

    public bool SupportsFineTuning { get; set; }

    /// <summary>
    /// Gets a display string for the validation state.
    /// </summary>
    public string ValidationDisplay =>
        ValidationLevel switch
            {
                ValidationLevel.ValidationError => "\u2716 Failed",
                ValidationLevel.AutoValidated => $"\u2713 Auto {LastValidatedAt:MM/dd}",
                ValidationLevel.ManuallyValidated => $"\u2713 Manual {LastValidatedAt:MM/dd}",
                ValidationLevel.VerifiedByOwner => $"\u2713 Verified {LastValidatedAt:MM/dd}",
                ValidationLevel.NotValidated => "\u25cb Unchecked",
                _ => string.Empty
            };

    /// <summary>
    /// Gets or sets the validation level for this provider.
    /// </summary>
    public ValidationLevel ValidationLevel
    {
        get => _validationLevel;
        set
        {
            if (_validationLevel == value) return;
            _validationLevel = value;
            OnPropertyChanged(nameof(ValidationLevel));
            OnPropertyChanged(nameof(ValidationDisplay));
        }
    }

    public string Website { get; set; } = string.Empty;

    public static ProviderSelectionItem FromDefinition(
        ProviderDefinition def,
        ProviderResearchMetadata? research)
    {
        return new ProviderSelectionItem
                   {
                       ProviderId = def.Id,
                       DisplayName = def.DisplayName,
                       Category = def.Category ?? string.Empty,
                       Protocol = def.Protocol.ToString(),
                       BaseUrl = def.BaseUrl,
                       Website = research?.Website ?? string.Empty,
                       ApiPricingUrl = research?.ApiPricingUrl ?? string.Empty,
                       ModelDescription = research?.ModelDescription ?? string.Empty,
                       HasModelDiscovery = def.HasModelDiscoveryApi,
                       PayAsYouGo = research?.PayAsYouGo ?? false,
                       HasFreeTier = research?.HasFreeTier ?? false,
                       SupportsFineTuning = research?.SupportsFineTuning ?? false
                   };
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
