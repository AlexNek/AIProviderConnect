using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

using AIProviderConnect.Constants;
using AIProviderConnect.Models;
using AIProviderConnect.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ScraperTool.Data.Entities;
using ScraperTool.Models;
using ScraperTool.Services;

using Serilog;

namespace ScraperTool.ViewModels;

public sealed partial class ProviderManualEditorViewModel : SuggestionManagementViewModelBase
{
    private static readonly JsonSerializerOptions ReadOptions = new()
                                                                    {
                                                                        PropertyNameCaseInsensitive =
                                                                            true
                                                                    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// Maps protocol identifiers (enum names and JSON aliases, case-insensitive)
    /// to the list of configuration keys each protocol reads from ProtocolConfiguration.
    /// Protocols not listed have no recognized keys.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyList<string>> ProtocolKnownKeysMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["anthropiccompatible"] = ["anthropicVersion", "apiKeyHeaderName"],
            ["MessagesApi"] = ["anthropicVersion", "apiKeyHeaderName"],
            ["geminicompatible"] = ["apiKeyHeaderName", "generationEndpoint", "streamEndpoint"],
            ["KeyQuery"] = ["apiKeyHeaderName", "generationEndpoint", "streamEndpoint"],
            ["githubmodelscompatible"] = ["apiVersion", "accept"],
            ["Catalog"] = ["apiVersion", "accept"],
        };

    private readonly AiUrlFixService? _aiUrlFix;

    private readonly List<ProviderSelectionItem> _allProviders = [];

    private readonly ProviderCatalog _catalog;

    private readonly ProviderJsonPatchService? _jsonPatch;

    private readonly string _manifestPath;

    private readonly IValidationMetadataService _metadataService;

    private readonly Action _navigateBack;

    private readonly AppSettings _settings;

    private readonly IOperationTimer _timer;

    private readonly IOperationLogger _uiLogger = new OperationLogger();

    private readonly ProviderDefinitionValidator? _validator;

    [ObservableProperty]
    private string _apiPricingUrl = string.Empty;

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private string _category = string.Empty;

    [ObservableProperty]
    private string _chatEndpoint = "chat/completions";

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private ValidationMetadata? _currentValidationState;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _documentationUrl = string.Empty;

    [ObservableProperty]
    private string _elapsedTime = "0m 0s";

    [ObservableProperty]
    private bool _hasElapsedSummary;

    [ObservableProperty]
    private bool _hasFreeTier;

    [ObservableProperty]
    private bool _hasModelDiscoveryApi;

    [ObservableProperty]
    private bool _hasRegionalEndpoints;

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isDynamicModelCatalog;

    [ObservableProperty]
    private bool _isPaused;

    private List<ValidationIssue> _lastValidationIssues = [];

    [ObservableProperty]
    private string _loginUrl = string.Empty;

    [ObservableProperty]
    private string _minimumCommitment = string.Empty;

    [ObservableProperty]
    private int _minModelCount;

    [ObservableProperty]
    private string _modelDescription = string.Empty;

    [ObservableProperty]
    private string _modelDiscoveryNotes = string.Empty;

    [ObservableProperty]
    private string _modelsEndpoint = "models";

    [ObservableProperty]
    private bool _payAsYouGo;

    [ObservableProperty]
    private string _payAsYouGoDescription = string.Empty;

    [ObservableProperty]
    private string _protocol = string.Empty;

    public ObservableCollection<ProtocolConfigEntry> ProtocolConfiguration { get; } = [];

    /// <summary>
    /// Per-operation endpoint override rows backing the definition's <c>endpoints</c> block.
    /// </summary>
    public ObservableCollection<EndpointConfigEntry> EndpointConfiguration { get; } = [];

    [ObservableProperty]
    private string _messagesEndpoint = EndpointDefaults.Messages;

    [ObservableProperty]
    private IReadOnlyList<string> _knownProtocolKeys = Array.Empty<string>();

    [ObservableProperty]
    private bool _hasProtocolKeys;

    [ObservableProperty]
    private string _regionalEndpointsText = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ProviderSelectionItem? _selectedProvider;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _subscriptionPricingUrl = string.Empty;

    [ObservableProperty]
    private bool _supportsFineTuning;

    [ObservableProperty]
    private string _website = string.Empty;

    public IReadOnlyList<string> Categories { get; }

    public ObservableCollection<ProviderSelectionItem> FilteredProviders { get; } = [];

    /// <summary>
    /// Gets whether AI suggestions are available after revalidation.
    /// </summary>
    public bool HasAiSuggestions => AiSuggestions.Count > 0;

    public bool HasManifestPath =>
        !string.IsNullOrWhiteSpace(_manifestPath) && Directory.Exists(_manifestPath);

    /// <summary>
    /// Gets whether there are validation errors to display.
    /// True when the validation level is ValidationError — even if the error
    /// messages array is empty (e.g. stale sidecar written by older code).
    /// </summary>
    public bool HasValidationErrors =>
        CurrentValidationState?.Level == ValidationLevel.ValidationError;

    /// <summary>
    /// Gets whether the current provider has been validated successfully.
    /// </summary>
    public bool IsValidated =>
        CurrentValidationState?.Level > ValidationLevel.NotValidated &&
        CurrentValidationState.Level != ValidationLevel.ValidationError;

    public IReadOnlyList<string> Protocols { get; } =
        [
            "Native",
            "OpenAICompatible",
            "AnthropicCompatible",
            "GeminiCompatible",
            "GitHubModelsCompatible",
            "HybridGateway"
        ];

    public ReadOnlyObservableCollection<string> RevalidationLog => _uiLogger.Entries;

    public IOperationTimer Timer => _timer;

    /// <summary>
    /// Gets the validation errors as a formatted string for display.
    /// Falls back to a note when the level is ValidationError but no
    /// individual error messages were recorded.
    /// </summary>
    public string ValidationErrorsText =>
        CurrentValidationState?.ValidationErrors is { Length: > 0 } errors
            ? string.Join("\n", errors)
            : CurrentValidationState?.Level == ValidationLevel.ValidationError
                ? "Validation failed — re-validate to capture detailed errors."
                : string.Empty;

    public string ValidationStateDisplay =>
        CurrentValidationState switch
            {
                null => "Not validated",
                { Level: ValidationLevel.ValidationError } =>
                    $"Validation failed ({CurrentValidationState.ValidationErrors.Length} issues)",
                { Level: ValidationLevel.AutoValidated } =>
                    $"Auto-validated {CurrentValidationState.LastValidatedAt:yyyy-MM-dd HH:mm}",
                { Level: ValidationLevel.ManuallyValidated } =>
                    $"Manually validated {CurrentValidationState.LastValidatedAt:yyyy-MM-dd HH:mm}",
                { Level: ValidationLevel.VerifiedByOwner } =>
                    $"Verified by owner {CurrentValidationState.LastValidatedAt:yyyy-MM-dd HH:mm}",
                _ => "Unknown"
            };

    public ProviderManualEditorViewModel(
        ProviderCatalog catalog,
        string manifestPath,
        Action navigateBack,
        IClipboardService clipboardService,
        IValidationMetadataService? metadataService = null,
        AiUrlFixService? aiUrlFix = null,
        ProviderDefinitionValidator? validator = null,
        ProviderJsonPatchService? jsonPatch = null,
        IOperationTimer? timer = null,
        AppSettings? settings = null)
        : base(clipboardService)
    {
        _catalog = catalog;
        _manifestPath = manifestPath;
        _navigateBack = navigateBack;
        _metadataService = metadataService ?? new ValidationMetadataService();
        _aiUrlFix = aiUrlFix;
        _validator = validator;
        _jsonPatch = jsonPatch;
        _timer = timer ?? new OperationTimer();
        _settings = settings ?? AppSettings.Load();

        _timer.PropertyChanged += OnTimerPropertyChanged;

        Categories = catalog.All
            .Select(p => p.Category)
            .OfType<string>()
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Prepend(string.Empty)
            .ToList();

        foreach (var def in catalog.All.OrderBy(p => p.DisplayName))
        {
            var research = catalog.GetResearchMetadata(def.Id);
            var item = ProviderSelectionItem.FromDefinition(def, research);
            item.RegionalEndpointsDisplay = research?.RegionalEndpoints is { Count: > 0 }
                                                ? string.Join(", ", research.RegionalEndpoints.Keys)
                                                : null;

            // Load validation state for this provider
            var filePath = GetFilePath(def.Id);
            if (File.Exists(filePath))
            {
                var metadata = _metadataService.Read(filePath);
                if (metadata != null)
                {
                    item.ValidationLevel = metadata.Level;
                    item.LastValidatedAt = metadata.LastValidatedAt;
                }
            }

            _allProviders.Add(item);
            FilteredProviders.Add(item);
        }

        // Show providers that failed to load in the catalog (e.g. JSON type
        // errors) so they are visible in the list, not silently missing.
        foreach (var error in catalog.LoadErrors)
        {
            // Error format: "...from embedded resource 'AIProviderConnect.ai_providers.<id>.json': ..."
            var providerId = ExtractProviderIdFromError(error);
            if (providerId is null) continue;

            var displayName = providerId;
            var filePath = GetFilePath(providerId);
            if (File.Exists(filePath))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("displayName", out var nameEl))
                        displayName = nameEl.GetString() ?? providerId;
                }
                catch { /* unparseable — use id as fallback */ }
            }

            var item = new ProviderSelectionItem
            {
                ProviderId = providerId,
                DisplayName = $"{displayName} (load error)"
            };
            item.ValidationLevel = ValidationLevel.ValidationError;

            _allProviders.Add(item);
            FilteredProviders.Add(item);
            AppendLog($"⚠ {error}");
        }

        // Re-sort so failed providers appear in alphabetical order with the rest
        if (catalog.LoadErrors.Count > 0)
        {
            var sorted = _allProviders.OrderBy(p => p.DisplayName).ToList();
            _allProviders.Clear();
            FilteredProviders.Clear();
            foreach (var item in sorted)
            {
                _allProviders.Add(item);
                FilteredProviders.Add(item);
            }
        }

        AiSuggestions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAiSuggestions));
    }

    private static string? ExtractProviderIdFromError(string error)
    {
        const string prefix = "ai_providers.";
        const string suffix = ".json";

        var start = error.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += prefix.Length;

        var end = error.IndexOf(suffix, start, StringComparison.OrdinalIgnoreCase);
        return end > start ? error[start..end] : null;
    }

    /// <summary>
    /// Initializes the editor after the view bindings are established.
    /// Call this after setting the ViewModel as ContentControl.Content.
    /// </summary>
    public void Initialize()
    {
        if (FilteredProviders.Count > 0)
        {
            SelectedProvider = FilteredProviders[0];
        }
    }

    private void AppendLog(string message)
    {
        _uiLogger.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    [RelayCommand]
    private async Task ApplyApprovedSuggestions()
    {
        var approved = AiSuggestions.Where(s => s.IsApproved && !s.IsRejected).ToList();

        if (approved.Count == 0)
        {
            StatusText = "No approved suggestions to apply.";
            return;
        }

        if (_jsonPatch is null)
        {
            StatusText = "JSON patch service not available.";
            return;
        }

        IsBusy = true;

        try
        {
            var (updatedFileCount, _) = ApplyPatchAndReloadCore(
                approved, _manifestPath, _catalog, _jsonPatch, AppendLog);

            AppendLog(
                $"Applied {approved.Count} suggestion(s) to {updatedFileCount} file(s).");
            StatusText = $"Applied {approved.Count} change(s) to {updatedFileCount} file(s).";

            // Remove applied suggestions from the list
            foreach (var s in approved)
            {
                AiSuggestions.Remove(s);
            }

            // Remove rejected suggestions too
            var rejected = AiSuggestions.Where(s => s.IsRejected && !s.IsApproved).ToList();
            foreach (var s in rejected)
            {
                AiSuggestions.Remove(s);
            }

            // Resolve the applied issues in the existing validation state instead of
            // wiping it. Validation already ran to produce these suggestions, so a
            // correct applied fix just clears the matching issue(s) and updates the
            // status — no second validation pass is needed.
            var remainingIssues = _lastValidationIssues
                .Where(i => i.SuggestionStatus != (int)IssueSuggestionStatus.Dismissed)
                .Where(i => !approved.Any(s =>
                    SuggestionResolvesIssue(i, s, SelectedProvider?.ProviderId)))
                .ToList();

            if (SelectedProvider is not null)
            {
                var providerFilePath = GetFilePath(SelectedProvider.ProviderId);
                await _metadataService.SaveFromValidationAsync(providerFilePath, remainingIssues);
            }

            _lastValidationIssues = remainingIssues;

            // Reload the provider to reflect applied changes
            if (SelectedProvider is not null)
            {
                LoadProvider(SelectedProvider.ProviderId);

                // Directly apply suggestion values to ViewModel properties so the
                // editor fields always reflect the patch even if LoadProvider reads
                // a stale catalog entry or the file round-trip loses a value.
                foreach (var s in approved)
                {
                    if (!string.Equals(s.ProviderId, SelectedProvider.ProviderId,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    var value = s.SuggestedValue ?? string.Empty;

                    if (string.Equals(s.Field, ProviderJsonFields.Website, StringComparison.OrdinalIgnoreCase))
                        Website = value;
                    else if (string.Equals(s.Field, ProviderJsonFields.LoginUrl, StringComparison.OrdinalIgnoreCase))
                        LoginUrl = value;
                    else if (string.Equals(s.Field, ProviderJsonFields.ApiPricingUrl, StringComparison.OrdinalIgnoreCase))
                        ApiPricingUrl = value;
                    else if (string.Equals(s.Field, ProviderJsonFields.SubscriptionPricingUrl, StringComparison.OrdinalIgnoreCase))
                        SubscriptionPricingUrl = value;
                    else if (string.Equals(s.Field, ProviderJsonFields.DocumentationUrl, StringComparison.OrdinalIgnoreCase))
                        DocumentationUrl = value;
                    else if (string.Equals(s.Field, ProviderJsonFields.BaseUrl, StringComparison.OrdinalIgnoreCase))
                        BaseUrl = value;
                    else if (string.Equals(s.Field, ProviderJsonFields.MinModelCount, StringComparison.OrdinalIgnoreCase)
                             && int.TryParse(value, out var minCount))
                        MinModelCount = minCount;
                }

                // Update the provider list item to reflect the post-apply state:
                // auto-validated when the applied fixes cleared every remaining
                // issue, otherwise still carrying the unresolved ones.
                RefreshProviderListItem(
                    SelectedProvider.ProviderId,
                    remainingIssues.Count == 0
                        ? ValidationLevel.AutoValidated
                        : ValidationLevel.ValidationError,
                    DateTime.UtcNow);
            }

            Log.Information(
                "User applied {ApprovedCount} approved suggestions in editor for {ProviderId}",
                approved.Count,
                SelectedProvider?.ProviderId);
        }
        catch (Exception ex)
        {
            StatusText = $"Error applying suggestions: {ex.Message}";
            AppendLog($"Apply failed: {ex.Message}");
            Log.Error(ex, "Failed to apply suggestions in editor");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Returns true when an approved suggestion resolves a validation issue:
    /// same provider and matching field (or the field name appears in the issue
    /// message when the issue carries no explicit field).
    /// </summary>
    private static bool SuggestionResolvesIssue(
        ValidationIssue issue,
        AiSuggestion suggestion,
        string? providerId)
    {
        if (string.IsNullOrWhiteSpace(suggestion.Field))
            return false;

        if (!string.Equals(suggestion.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            return false;

        return !string.IsNullOrWhiteSpace(issue.Field)
            ? issue.Field.Equals(suggestion.Field, StringComparison.OrdinalIgnoreCase)
            : issue.Message.Contains(suggestion.Field, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        FilteredProviders.Clear();

        foreach (var item in _allProviders)
        {
            if (string.IsNullOrEmpty(query) ||
                item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.ProviderId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Protocol.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredProviders.Add(item);
            }
            else if (item == SelectedProvider)
            {
                // Keep the selected provider visible even if it does not match
                FilteredProviders.Add(item);
            }
        }
    }

    private ProviderDefinition BuildDefinition()
    {
        var protocolConfig = ProtocolConfiguration
            .Where(e => !string.IsNullOrWhiteSpace(e.Key)
                        && !string.IsNullOrWhiteSpace(e.Value))
            .GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value!, StringComparer.OrdinalIgnoreCase);

        // Sync legacy flat fields into their auto-seeded EndpointConfiguration rows
        // (entries without a baseUrl or protocol override) so edits to the flat
        // properties are captured by the save path.
        SyncFlatFieldToRow(EndpointOperations.Chat, ChatEndpoint);
        SyncFlatFieldToRow(EndpointOperations.Models, ModelsEndpoint);
        SyncFlatFieldToRow(EndpointOperations.Messages, MessagesEndpoint);

        var endpoints = EndpointConfiguration
            .Where(e => !string.IsNullOrWhiteSpace(e.Operation)
                        && (!string.IsNullOrWhiteSpace(e.Path)
                            || !string.IsNullOrWhiteSpace(e.BaseUrl)
                            || !string.IsNullOrWhiteSpace(e.Protocol))
                        && !IsAutoSeededDefaultWithNoOverride(e))
            .GroupBy(e => e.Operation.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new EndpointDefinition
                        {
                            Path = NullIfEmpty(g.Last().Path),
                            BaseUrl = NullIfEmpty(g.Last().BaseUrl),
                            Protocol = string.IsNullOrWhiteSpace(g.Last().Protocol)
                                        || g.Last().Protocol == EndpointConfigEntry.InheritProtocolDisplay
                                ? null
                                : ProviderProtocolMapper.FromJson(g.Last().Protocol)
                        },
                StringComparer.OrdinalIgnoreCase);

        return new ProviderDefinition
                   {
                       Id = SelectedProvider?.ProviderId ?? Id,
                       DisplayName = DisplayName,
                       Protocol = ProviderProtocolMapper.FromJson(Protocol),
                       BaseUrl = BaseUrl,
                       ChatEndpoint = ChatEndpoint,
                       ModelsEndpoint = ModelsEndpoint,
                       MessagesEndpoint = MessagesEndpoint,
                       Category = Category,
                       HasModelDiscoveryApi = HasModelDiscoveryApi,
                       ProtocolConfiguration = protocolConfig.Count > 0 ? protocolConfig : null,
                       Endpoints = endpoints.Count > 0 ? endpoints : null
                   };
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// True when <paramref name="entry"/> was auto-seeded with a library default and
    /// the user has not changed the path or added a baseUrl/protocol override. Such
    /// entries are visible reference rows in the editor but must not be written to
    /// the JSON (the library would reject them as no-op entries).
    /// </summary>
    private static bool IsAutoSeededDefaultWithNoOverride(EndpointConfigEntry entry)
    {
        if (!entry.IsAutoSeededDefault)
        {
            return false;
        }

        var hasBaseUrl = !string.IsNullOrWhiteSpace(entry.BaseUrl);
        var hasProtocol = !string.IsNullOrWhiteSpace(entry.Protocol)
                          && entry.Protocol != EndpointConfigEntry.InheritProtocolDisplay;
        var pathDiffersFromDefault = !string.Equals(
            entry.Path, entry.DefaultPath, StringComparison.OrdinalIgnoreCase);

        return !hasBaseUrl && !hasProtocol && !pathDiffersFromDefault;
    }

    /// <summary>
    /// Pushes the current flat-field value (e.g. <see cref="MessagesEndpoint"/>)
    /// into the matching <see cref="EndpointConfiguration"/> row when that row is
    /// a simple auto-seeded entry (no baseUrl or protocol override). Explicit
    /// overrides created through the endpoint editor keep their own path.
    /// </summary>
    private void SyncFlatFieldToRow(string operation, string flatValue)
    {
        var entry = EndpointConfiguration
            .FirstOrDefault(e => string.Equals(e.Operation, operation, StringComparison.OrdinalIgnoreCase));
        if (entry is not null
            && string.IsNullOrWhiteSpace(entry.BaseUrl)
            && (string.IsNullOrWhiteSpace(entry.Protocol)
                || entry.Protocol == EndpointConfigEntry.InheritProtocolDisplay))
        {
            entry.Path = flatValue;
        }
    }

    private ProviderResearchMetadata BuildResearchMetadata()
    {
        return new ProviderResearchMetadata
                   {
                       Website = Website,
                       LoginUrl = LoginUrl,
                       ApiPricingUrl = ApiPricingUrl,
                       SubscriptionPricingUrl = SubscriptionPricingUrl,
                       DocumentationUrl = DocumentationUrl,
                       ModelDescription = ModelDescription,
                       PayAsYouGo = PayAsYouGo,
                       PayAsYouGoDescription = PayAsYouGoDescription,
                       ModelDiscoveryNotes = ModelDiscoveryNotes,
                       MinimumCommitment = MinimumCommitment,
                       HasFreeTier = HasFreeTier,
                       SupportsFineTuning = SupportsFineTuning,
                       MinModelCount = MinModelCount,
                       IsDynamicModelCatalog = IsDynamicModelCatalog,
                       RegionalEndpoints = ParseRegionalEndpoints(RegionalEndpointsText)
                   };
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _cts?.Cancel();
        IsPaused = false;
        _uiLogger.Add("=== Revalidation cancelled by user ===");
    }

    private void UpdateProtocolKnownKeys()
    {
        IReadOnlyList<string> keys;
        if (ProtocolKnownKeysMap.TryGetValue(Protocol, out var mapped))
        {
            KnownProtocolKeys = mapped;
            HasProtocolKeys = true;
            keys = mapped;
        }
        else
        {
            KnownProtocolKeys = Array.Empty<string>();
            HasProtocolKeys = false;
            keys = Array.Empty<string>();
        }

        foreach (var entry in ProtocolConfiguration)
        {
            entry.KnownKeys = keys;
        }
    }

    private void ClearFields()
    {
        Id = string.Empty;
        DisplayName = string.Empty;
        Protocol = string.Empty;
        Website = string.Empty;
        LoginUrl = string.Empty;
        ApiPricingUrl = string.Empty;
        SubscriptionPricingUrl = string.Empty;
        DocumentationUrl = string.Empty;
        BaseUrl = string.Empty;
        ChatEndpoint = EndpointDefaults.ChatCompletions;
        ModelsEndpoint = EndpointDefaults.Models;
        MessagesEndpoint = EndpointDefaults.Messages;
        Category = string.Empty;
        ModelDescription = string.Empty;
        PayAsYouGo = false;
        PayAsYouGoDescription = string.Empty;
        HasModelDiscoveryApi = false;
        ModelDiscoveryNotes = string.Empty;
        MinimumCommitment = string.Empty;
        HasFreeTier = false;
        SupportsFineTuning = false;
        MinModelCount = 0;
        IsDynamicModelCatalog = false;
        RegionalEndpointsText = string.Empty;
        HasRegionalEndpoints = false;
        IsDirty = false;
        ProtocolConfiguration.Clear();
        EndpointConfiguration.Clear();
    }

    [RelayCommand]
    private void AddEndpointConfigEntry()
    {
        EndpointConfiguration.Add(new EndpointConfigEntry());
        IsDirty = true;
    }

    [RelayCommand]
    private void RemoveEndpointConfigEntry(EndpointConfigEntry? entry)
    {
        if (entry is not null)
        {
            EndpointConfiguration.Remove(entry);
            IsDirty = true;
        }
    }

    [RelayCommand]
    private void AddProtocolConfigEntry()
    {
        ProtocolConfiguration.Add(new ProtocolConfigEntry { KnownKeys = KnownProtocolKeys });
        IsDirty = true;
    }

    [RelayCommand]
    private void RemoveProtocolConfigEntry(ProtocolConfigEntry? entry)
    {
        if (entry is not null)
        {
            ProtocolConfiguration.Remove(entry);
            IsDirty = true;
        }
    }

    [RelayCommand]
    private void CopyRevalidationLog()
    {
        if (_uiLogger.Entries.Count == 0)
            return;

        try
        {
            ClipboardService.SetText(string.Join(Environment.NewLine, _uiLogger.Entries));
            StatusText = "Revalidation log copied to clipboard.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy revalidation log to clipboard");
        }
    }

    /// <summary>
    /// Copies validation errors to clipboard.
    /// </summary>
    [RelayCommand]
    private void CopyValidationErrors()
    {
        if (string.IsNullOrEmpty(ValidationErrorsText))
            return;

        try
        {
            Clipboard.SetText(ValidationErrorsText);
            Log.Information(
                "Validation errors copied to clipboard for provider {ProviderId}",
                SelectedProvider?.ProviderId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy validation errors to clipboard");
            MessageBox.Show(
                $"Failed to copy to clipboard: {ex.Message}",
                "Copy Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string GetFilePath(string providerId)
    {
        return Path.Combine(_manifestPath, $"{providerId}.json");
    }

    [RelayCommand]
    private void GoBack()
    {
        if (IsDirty)
        {
            var result = MessageBox.Show(
                "You have unsaved changes. Leave without saving?",
                "Unsaved Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;
        }

        _navigateBack();
    }

    [RelayCommand]
    private void LoadProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return;

        ProviderDefinition? def = null;
        ProviderResearchMetadata? research = null;
        var filePath = GetFilePath(providerId);

        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                def = JsonSerializer.Deserialize<ProviderDefinition>(json, ReadOptions);
                research = JsonSerializer.Deserialize<ProviderResearchMetadata>(json, ReadOptions);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load provider definition from {FilePath}", filePath);
            }
        }

        def ??= _catalog.Get(providerId);
        research ??= _catalog.GetResearchMetadata(providerId);

        if (def is null)
        {
            StatusText = $"Provider '{providerId}' failed to load — JSON contains errors.";
            ClearFields();
            return;
        }

        SetFields(def, research);
        LoadValidationState(filePath);
        IsDirty = false;
        StatusText = $"Loaded {def.DisplayName} ({providerId}).";
    }

    private void LoadValidationState(string filePath)
    {
        CurrentValidationState = _metadataService.Read(filePath);

        // Keep the provider list item in sync with the actual sidecar content.
        // The list item's ValidationLevel is an in-memory cache that can go stale
        // when the sidecar is updated externally (e.g. by CheckDataPanel validation)
        // or when ApplySuggestions leaves a stale sidecar on disk.
        if (SelectedProvider is not null)
        {
            if (CurrentValidationState is not null)
            {
                SelectedProvider.ValidationLevel = CurrentValidationState.Level;
                SelectedProvider.LastValidatedAt = CurrentValidationState.LastValidatedAt;
            }
            else
            {
                SelectedProvider.ValidationLevel = ValidationLevel.NotValidated;
                SelectedProvider.LastValidatedAt = null;
            }
        }
    }

    private void LogProviderAiFixResult(
        AiUrlFixRunResult result,
        int totalCount,
        IReadOnlyList<ValidationIssue> issues,
        int additionalSuggestions = 0)
    {
        var suggestionCount = result.Suggestions.Count + additionalSuggestions;
        if (suggestionCount > 0)
        {
            foreach (var s in result.Suggestions)
                AiSuggestions.Add(s);

            AppendLog(
                $"{suggestionCount} suggestion(s) generated — review and apply below.");
            StatusText = $"{suggestionCount} suggestion(s) — approve then Apply.";
        }
        else
        {
            // Surface the real failure reason (e.g. unusable AI model slug) instead
            // of a generic "no suggestions" note — the user must see why nothing
            // was produced. A run that completed its research and concluded that no
            // value exists is an answer about the field, not a breakdown, and has to
            // be phrased as one — naming the field it concerns.
            var failures = issues
                .Where(i => i.SuggestionStatus == (int)IssueSuggestionStatus.Failed
                            && !string.IsNullOrWhiteSpace(i.SuggestionReason))
                .ToList();

            var error = failures.FirstOrDefault(i => !i.ResearchCompletedWithoutValue);
            var noValue = failures.Where(i => i.ResearchCompletedWithoutValue).ToList();

            if (error is not null)
            {
                AppendLog($"✖ AI fix error: {error.SuggestionReason}");
                StatusText = $"AI fix error: {error.SuggestionReason}";
            }
            else if (noValue.Count > 0)
            {
                var fields = string.Join(", ", noValue.Select(i => $"'{i.Field}'"));
                var detail = noValue[0].SuggestionReason;

                AppendLog($"— AI fix found no value for {fields}: {detail}");
                StatusText = noValue.Count == 1
                    ? $"AI fix found no value for '{noValue[0].Field}'."
                    : $"AI fix found no value for {noValue.Count} fields.";
            }
            else
            {
                AppendLog("AI fix completed but no suggestions were generated.");
                StatusText = $"{totalCount} issue(s) found — no AI suggestions.";
            }
        }
    }

    [RelayCommand]
    private async Task MarkAsManuallyValidatedAsync()
    {
        var provider = SelectedProvider;
        if (provider is null)
        {
            StatusText = "Select a provider first.";
            return;
        }

        var filePath = GetFilePath(provider.ProviderId);
        if (!File.Exists(filePath))
        {
            StatusText = "Provider file does not exist.";
            return;
        }

        try
        {
            await _metadataService.UpdateAsync(
                filePath,
                ValidationLevel.ManuallyValidated,
                ValidatorConstants.ManualValidator,
                "Manually reviewed and confirmed by user");

            // Reload the validation state to reflect the change
            LoadValidationState(filePath);
            RefreshProviderListItem(
                provider.ProviderId,
                ValidationLevel.ManuallyValidated,
                DateTime.UtcNow);
            StatusText = $"Marked {provider.DisplayName} as manually validated.";
            Log.Information(
                "Provider {ProviderId} marked as manually validated",
                provider.ProviderId);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to save validation state: {ex.Message}";
            Log.Error(
                ex,
                "Failed to save validation metadata for {ProviderId}",
                provider.ProviderId);
        }
    }

    partial void OnApiPricingUrlChanged(string value) => IsDirty = true;

    partial void OnBaseUrlChanged(string value) => IsDirty = true;

    partial void OnCategoryChanged(string value) => IsDirty = true;

    partial void OnChatEndpointChanged(string value) => IsDirty = true;

    partial void OnCurrentValidationStateChanged(ValidationMetadata? value)
    {
        OnPropertyChanged(nameof(ValidationStateDisplay));
        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(ValidationErrorsText));
    }

    partial void OnDisplayNameChanged(string value) => IsDirty = true;

    partial void OnDocumentationUrlChanged(string value) => IsDirty = true;

    partial void OnHasFreeTierChanged(bool value) => IsDirty = true;

    partial void OnHasModelDiscoveryApiChanged(bool value) => IsDirty = true;

    partial void OnIsDynamicModelCatalogChanged(bool value) => IsDirty = true;

    partial void OnLoginUrlChanged(string value) => IsDirty = true;

    partial void OnMinimumCommitmentChanged(string value) => IsDirty = true;

    partial void OnMinModelCountChanged(int value) => IsDirty = true;

    partial void OnModelDescriptionChanged(string value) => IsDirty = true;

    partial void OnModelDiscoveryNotesChanged(string value) => IsDirty = true;

    partial void OnModelsEndpointChanged(string value) => IsDirty = true;

    partial void OnMessagesEndpointChanged(string value) => IsDirty = true;

    partial void OnPayAsYouGoChanged(bool value) => IsDirty = true;

    partial void OnPayAsYouGoDescriptionChanged(string value) => IsDirty = true;

    partial void OnProtocolChanged(string value)
    {
        IsDirty = true;
        UpdateProtocolKnownKeys();
    }

    partial void OnRegionalEndpointsTextChanged(string value) => IsDirty = true;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedProviderChanged(ProviderSelectionItem? value)
    {
        if (value is null)
        {
            ClearFields();
            return;
        }

        _uiLogger.Clear();
        AiSuggestions.Clear();
        LoadProvider(value.ProviderId);
    }

    partial void OnSubscriptionPricingUrlChanged(string value) => IsDirty = true;

    partial void OnSupportsFineTuningChanged(bool value) => IsDirty = true;

    private void OnTimerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IOperationTimer.ElapsedTime))
        {
            ElapsedTime = _timer.ElapsedTime;
        }
    }

    partial void OnWebsiteChanged(string value) => IsDirty = true;

    [RelayCommand]
    private void OpenLink(string? field)
    {
        var url = field switch
            {
                _ when string.Equals(
                    field,
                    ProviderJsonFields.Website,
                    StringComparison.OrdinalIgnoreCase) => Website,
                _ when string.Equals(
                    field,
                    ProviderJsonFields.LoginUrl,
                    StringComparison.OrdinalIgnoreCase) => LoginUrl,
                _ when string.Equals(
                    field,
                    ProviderJsonFields.ApiPricingUrl,
                    StringComparison.OrdinalIgnoreCase) => ApiPricingUrl,
                _ when string.Equals(
                    field,
                    ProviderJsonFields.SubscriptionPricingUrl,
                    StringComparison.OrdinalIgnoreCase) => SubscriptionPricingUrl,
                _ when string.Equals(
                    field,
                    ProviderJsonFields.DocumentationUrl,
                    StringComparison.OrdinalIgnoreCase) => DocumentationUrl,
                _ when string.Equals(
                    field,
                    ProviderJsonFields.BaseUrl,
                    StringComparison.OrdinalIgnoreCase) => BaseUrl,
                _ => string.Empty
            };

        if (string.IsNullOrWhiteSpace(url))
        {
            StatusText = $"No URL entered for {field}.";
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            StatusText = $"'{url}' is not a valid URL.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            StatusText = $"Opened {field}: {uri.AbsoluteUri}";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open link: {ex.Message}";
        }
    }

    private static Dictionary<string, string>? ParseRegionalEndpoints(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx > 0)
            {
                var key = line[..colonIdx].Trim();
                var url = line[(colonIdx + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(url))
                    entries[key] = url;
            }
        }

        return entries.Count > 0 ? entries : null;
    }

    [RelayCommand]
    private void PauseOperation()
    {
        _cts?.Cancel();
        IsPaused = true;
        _uiLogger.Add("=== Revalidation paused by user — click Resume to continue ===");
    }

    private void RefreshProviderListItem(
        string providerId,
        ValidationLevel level,
        DateTime? validatedAt)
    {
        var item = _allProviders.FirstOrDefault(p => p.ProviderId == providerId);
        if (item is null) return;

        // Update in-place — ValidationLevel and LastValidatedAt raise PropertyChanged
        item.ValidationLevel = level;
        item.LastValidatedAt = validatedAt;
    }

    /// <summary>
    /// Removes issues dismissed by AI research (current URL confirmed valid) from the
    /// validation sidecar so they stop resurfacing on every revalidation run.
    /// </summary>
    private async Task RemoveDismissedIssuesFromValidationAsync(
        string filePath,
        List<ValidationIssue> issues)
    {
        var dismissed = issues
            .Where(i => i.SuggestionStatus == (int)IssueSuggestionStatus.Dismissed)
            .ToList();
        if (dismissed.Count == 0)
            return;

        var remaining = issues.Except(dismissed).ToList();
        await _metadataService.SaveFromValidationAsync(filePath, remaining);
        LoadValidationState(filePath);

        foreach (var issue in dismissed)
        {
            AppendLog($"  Dismissed [{issue.Code}] — current URL confirmed valid by AI research.");
        }

        if (SelectedProvider is not null)
        {
            var level = remaining.Count == 0
                            ? ValidationLevel.AutoValidated
                            : ValidationLevel.ValidationError;
            RefreshProviderListItem(SelectedProvider.ProviderId, level, DateTime.UtcNow);
        }
    }

    [RelayCommand]
    private async Task ResumeOperationAsync()
    {
        IsPaused = false;
        _uiLogger.Add("=== Resuming revalidation ===");
        _timer.Resume();
        await RevalidateProviderAsync();
    }

    [RelayCommand]
    private async Task RevalidateProviderAsync()
    {
        var provider = SelectedProvider;
        if (provider is null)
        {
            StatusText = "Select a provider first.";
            return;
        }

        var filePath = GetFilePath(provider.ProviderId);
        if (!File.Exists(filePath))
        {
            StatusText = "Provider file does not exist.";
            return;
        }

        if (_validator is null)
        {
            StatusText = "Validator not available.";
            return;
        }

        IsBusy = true;
        IsPaused = false;
        AiSuggestions.Clear();
        _lastValidationIssues = [];

        if (!_timer.IsRunning)
            _timer.Start();
        _cts = new CancellationTokenSource();

        try
        {
            _uiLogger.Clear();
            AppendLog($"Revalidating {provider.DisplayName}...");

            // Re-run validation — persistValidation: true writes the new sidecar.
            // forceRevalidation: true bypasses the manual-validation skip so the
            // full check always runs.  The old sidecar is preserved on disk until
            // the new one is written at the end of ValidateFileAsync, so cancelling
            // before that point keeps the previous error information intact.
            var validationProgress = new Progress<ValidationProgress>(p =>
            {
                var msg = p.Stage switch
                {
                    ValidationStage.CheckingUrl => $"  Checking {p.Field}: {p.Url}",
                    ValidationStage.CheckingContent => $"  Checking if page exists: {p.Url}",
                    ValidationStage.CheckingPricingContent => $"  Checking pricing content: {p.Url}",
                    ValidationStage.CheckPassed => $"  ✓ {p.Field}: {p.ResultMessage}",
                    ValidationStage.CheckFailed => $"  ✗ {p.Field} failed",
                    _ => null
                };
                if (msg != null)
                    AppendLog(msg);
            });

            var issues = await _validator.ValidateFileAsync(
                             filePath,
                             persistValidation: true,
                             progress: validationProgress,
                             ct: _cts.Token,
                             forceRevalidation: true);

            _cts.Token.ThrowIfCancellationRequested();

            if (issues.Count == 0)
            {
                RefreshProviderListItem(
                    provider.ProviderId,
                    ValidationLevel.AutoValidated,
                    DateTime.UtcNow);
                LoadValidationState(filePath);
                StatusText = $"All checks passed for {provider.DisplayName}.";
                AppendLog("All checks passed — provider marked as auto-validated.");
                return;
            }

            _lastValidationIssues = issues;
            var urlErrors = issues.Count(i => ValidationIssueCodes.UrlErrorCodes.Contains(i.Code));
            var structural = issues.Count - urlErrors;

            RefreshProviderListItem(
                provider.ProviderId,
                ValidationLevel.ValidationError,
                DateTime.UtcNow);
            LoadValidationState(filePath);

            foreach (var issue in issues)
            {
                AppendLog(
                    $"  {(ValidationIssueCodes.UrlErrorCodes.Contains(issue.Code) ? "\u2716" : "\u26A0")} [{issue.Code}] {issue.Message}");
            }

            AppendLog(
                $"Found {issues.Count} issue(s): {structural} structural, {urlErrors} broken URL(s).");

            // If AI is available and there are fixable issues, run AI fix to generate suggestions.
            // Fixable issues include:
            // 1. URL errors (broken URLs needing repair)
            // 2. Structural issues with a Field property set (like minModelCount) that have decision trees
            var brokenUrls = issues
                .Where(i => ValidationIssueCodes.UrlErrorCodes.Contains(i.Code)
                            || (!string.IsNullOrEmpty(i.Field)
                                && ValidationIssueCodes.StructuralErrorCodes.Contains(i.Code)))
                .ToList();

            var fixableCount = brokenUrls.Count;
            var urlFixCount = brokenUrls.Count(i => ValidationIssueCodes.UrlErrorCodes.Contains(i.Code));
            var structuralFixCount = fixableCount - urlFixCount;

            if (structuralFixCount > 0)
            {
                AppendLog($"  {structuralFixCount} structural issue(s) with AI fix support detected.");
            }

            if (fixableCount > 0 && _aiUrlFix is not null && _aiUrlFix.IsAvailable)
            {
                if (string.IsNullOrWhiteSpace(_settings.PrimaryModel)
                    && string.IsNullOrWhiteSpace(_settings.FallbackModel))
                {
                    AppendLog(
                        "AI fix skipped — no primary or fallback model configured in AI Setup.");
                    StatusText = $"{issues.Count} issue(s) found for {provider.DisplayName}.";
                }
                else
                {
                    await RunProviderAiFixWithFallbackAsync(brokenUrls);
                    await RemoveDismissedIssuesFromValidationAsync(filePath, issues);
                }
            }
            else if (fixableCount > 0)
            {
                AppendLog("AI fix not available — review issues manually.");
                StatusText = $"{issues.Count} issue(s) found for {provider.DisplayName}.";
            }
            else
            {
                StatusText =
                    $"{issues.Count} structural issue(s) found for {provider.DisplayName}.";
            }
        }
        catch (OperationCanceledException) when (IsPaused)
        {
            AppendLog("=== Revalidation paused ===");
            StatusText = "Paused — click Resume to continue.";
        }
        catch (OperationCanceledException)
        {
            AppendLog("=== Revalidation cancelled ===");
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Revalidation failed: {ex.Message}";
            AppendLog($"Failed: {ex.Message}");
            Log.Error(ex, "Revalidation failed for {ProviderId}", provider.ProviderId);
        }
        finally
        {
            if (IsPaused)
                _timer.Pause();
            else
            {
                _timer.Stop();
                ElapsedTime = _timer.ElapsedTime;
                HasElapsedSummary = true;
            }

            if (!IsPaused)
            {
                _cts?.Dispose();
                _cts = null;
            }

            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Revert()
    {
        if (SelectedProvider is null) return;

        var result = MessageBox.Show(
            $"Revert all changes to {SelectedProvider.DisplayName}?",
            "Revert Changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        LoadProvider(SelectedProvider.ProviderId);
    }

    private async Task RunProviderAiFixWithFallbackAsync(List<ValidationIssue> issuesToFix)
    {
        if (_aiUrlFix is null || _cts is null) return;

        // Model selection is configuration only: use the fallback model when the
        // primary is not set. Model recovery is the AI runtime's job (failover
        // chain) — this layer never retries with a different model.
        var model = _settings.PrimaryModel;
        if (string.IsNullOrWhiteSpace(model)
            && !string.IsNullOrWhiteSpace(_settings.FallbackModel))
        {
            AppendLog(
                $"Primary model not configured — using fallback model '{_settings.FallbackModel}'");
            model = _settings.FallbackModel;
        }

        var totalCount = issuesToFix.Count;
        var urlCount = issuesToFix.Count(i => ValidationIssueCodes.UrlErrorCodes.Contains(i.Code));
        var structuralCount = totalCount - urlCount;

        var fixDescription = urlCount > 0 && structuralCount > 0
            ? $"{urlCount} URL(s) and {structuralCount} structural issue(s)"
            : urlCount > 0
                ? $"{urlCount} broken URL(s)"
                : $"{structuralCount} structural issue(s)";

        AppendLog($"Running AI fix for {fixDescription} with model '{model}'...");
        StatusText = $"AI fixing {fixDescription} for {SelectedProvider?.DisplayName ?? "provider"}...";
        var progress = new Progress<AiUrlFixProgress>(p =>
            {
                AppendLog(p.Message);
                StatusText = p.Message;
            });
        var result = await _aiUrlFix.RunAsync(issuesToFix, model, progress, _cts.Token);

        LogProviderAiFixResult(result, totalCount, issuesToFix);
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedProvider is null)
        {
            StatusText = "Select a provider first.";
            return;
        }

        if (!HasManifestPath)
        {
            StatusText = "Manifest path is not configured or does not exist. Set it in Settings.";
            return;
        }

        var providerId = SelectedProvider.ProviderId;
        var filePath = GetFilePath(providerId);

        try
        {
            var def = BuildDefinition();
            var research = BuildResearchMetadata();
            var json = SerializeWithRoundTrip(def, research);
            File.WriteAllText(filePath, json);
            IsDirty = false;
            StatusText = $"Saved {def.DisplayName} to {Path.GetFileName(filePath)}.";
            Log.Information("User manually saved provider definition for {ProviderId}", providerId);
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            Log.Error(ex, "Failed to save provider definition for {ProviderId}", providerId);
        }
    }

    private string SerializeWithRoundTrip(ProviderDefinition def, ProviderResearchMetadata research)
        => ProviderManifestSerializer.Flatten(def, research).ToJsonString(WriteOptions);

    private void SetFields(ProviderDefinition def, ProviderResearchMetadata? research)
    {
        Id = def.Id;
        DisplayName = def.DisplayName;
        Protocol = def.Protocol.ToString();
        Website = research?.Website ?? string.Empty;
        LoginUrl = research?.LoginUrl ?? string.Empty;
        ApiPricingUrl = research?.ApiPricingUrl ?? string.Empty;
        SubscriptionPricingUrl = research?.SubscriptionPricingUrl ?? string.Empty;
        DocumentationUrl = research?.DocumentationUrl ?? string.Empty;
        BaseUrl = def.BaseUrl;
        ChatEndpoint = def.ChatEndpoint;
        ModelsEndpoint = def.ModelsEndpoint;
        MessagesEndpoint = def.MessagesEndpoint;
        Category = def.Category ?? string.Empty;
        ModelDescription = research?.ModelDescription ?? string.Empty;
        PayAsYouGo = research?.PayAsYouGo ?? false;
        PayAsYouGoDescription = research?.PayAsYouGoDescription ?? string.Empty;
        HasModelDiscoveryApi = def.HasModelDiscoveryApi;
        ModelDiscoveryNotes = research?.ModelDiscoveryNotes ?? string.Empty;
        MinimumCommitment = research?.MinimumCommitment ?? string.Empty;
        HasFreeTier = research?.HasFreeTier ?? false;
        SupportsFineTuning = research?.SupportsFineTuning ?? false;
        MinModelCount = research?.MinModelCount ?? 0;
        IsDynamicModelCatalog = research?.IsDynamicModelCatalog ?? false;
        RegionalEndpointsText = research?.RegionalEndpoints is { Count: > 0 }
                                    ? string.Join(
                                        "\n",
                                        research.RegionalEndpoints.Select(kv => $"{kv.Key}: {kv.Value}"))
                                    : string.Empty;
        HasRegionalEndpoints = research?.RegionalEndpoints is { Count: > 0 };

        ProtocolConfiguration.Clear();
        if (def.ProtocolConfiguration is not null)
        {
            foreach (var kvp in def.ProtocolConfiguration)
            {
                ProtocolConfiguration.Add(new ProtocolConfigEntry
                                              {
                                                  Key = kvp.Key,
                                                  Value = kvp.Value,
                                                  KnownKeys = KnownProtocolKeys
                                              });
            }
        }

        EndpointConfiguration.Clear();
        if (def.Endpoints is not null)
        {
            foreach (var kvp in def.Endpoints.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                EndpointConfiguration.Add(new EndpointConfigEntry
                                                 {
                                                     Operation = kvp.Key,
                                                     Path = kvp.Value.Path ?? string.Empty,
                                                     BaseUrl = kvp.Value.BaseUrl ?? string.Empty,
                                                     Protocol = kvp.Value.Protocol is null
                                                         ? EndpointConfigEntry.InheritProtocolDisplay
                                                         : ProviderProtocolMapper.ToJson(kvp.Value.Protocol.Value)
                                                 });
            }
        }

        // Seed from legacy flat fields not already covered by the endpoints dictionary
        // so the unified editor shows all endpoint data, including defaults.
        SeedFlatFieldIfMissing(EndpointOperations.Chat, def.ChatEndpoint);
        SeedFlatFieldIfMissing(EndpointOperations.Models, def.ModelsEndpoint);
        SeedFlatFieldIfMissing(EndpointOperations.Messages, def.MessagesEndpoint);

        // Seed embeddings with its library default for OpenAI-compatible and hybrid-
        // gateway providers (the only protocols whose class implements IEmbeddingProvider)
        // so the editor shows the operation even when the definition has no explicit
        // entry. Decisions are NOT auto-seeded: only providers with an explicit
        // decisions override in their endpoints block need the row.
        SeedEmbeddingsDefaultIfApplicable(def.Protocol, def.Endpoints);

        void SeedFlatFieldIfMissing(string operation, string flatValue)
        {
            if (!string.IsNullOrWhiteSpace(flatValue)
                && EndpointOperations.Find(def.Endpoints, operation) is null)
            {
                EndpointConfiguration.Add(new EndpointConfigEntry
                                                 {
                                                     Operation = operation,
                                                     Path = flatValue,
                                                     IsSeeded = true
                                                 });
            }
        }

        void SeedEmbeddingsDefaultIfApplicable(
            EProviderProtocol protocol,
            IReadOnlyDictionary<string, EndpointDefinition>? endpoints)
        {
            if (protocol is not (EProviderProtocol.OpenAICompatible or EProviderProtocol.HybridGateway))
            {
                return;
            }

            if (EndpointOperations.Find(endpoints, EndpointOperations.Embeddings) is not null
                || EndpointConfiguration.Any(e => string.Equals(e.Operation, EndpointOperations.Embeddings, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var entry = new EndpointConfigEntry
                            { Operation = EndpointOperations.Embeddings, IsAutoSeededDefault = true };
            entry.Path = entry.DefaultPath;
            EndpointConfiguration.Add(entry);
        }
    }
}
