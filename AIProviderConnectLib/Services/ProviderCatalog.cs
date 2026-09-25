using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

namespace AIProviderConnect.Services;

/// <summary>
/// Provides access to provider definitions loaded from embedded JSON, with support for reloading individual definitions from disk.
/// </summary>
public sealed class ProviderCatalog : IProviderCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
                                                                    {
                                                                        PropertyNameCaseInsensitive =
                                                                            true
                                                                    };

    private readonly object _sync = new();

    private readonly List<ProviderDefinition> _allProviders;

    private readonly List<string> _loadErrors = [];

    private readonly Dictionary<string, ProviderDefinition> _providers;

    private readonly Dictionary<string, ProviderResearchMetadata> _researchMetadata =
        new(StringComparer.OrdinalIgnoreCase);

    private List<ProviderDefinition>? _allSnapshot;
    private List<ProviderDefinition>? _withModelDiscoverySnapshot;
    private List<ProviderDefinition>? _withDynamicCatalogSnapshot;

    /// <summary>
    /// Gets all available provider definitions.
    /// </summary>
    public IReadOnlyList<ProviderDefinition> All
    {
        get
        {
            lock (_sync)
            {
                if (_allSnapshot is null)
                    _allSnapshot = _allProviders.ToList();
                return _allSnapshot;
            }
        }
    }

    /// <summary>
    /// Gets errors that occurred while loading provider definitions.
    /// </summary>
    public IReadOnlyList<string> LoadErrors
    {
        get { lock (_sync) return _loadErrors.ToList(); }
    }

    /// <summary>
    /// Gets providers that support model discovery.
    /// </summary>
    public IReadOnlyList<ProviderDefinition> WithModelDiscovery
    {
        get
        {
            lock (_sync)
            {
                if (_withModelDiscoverySnapshot is null)
                    _withModelDiscoverySnapshot = _allProviders.Where(p => p.HasModelDiscoveryApi).ToList();
                return _withModelDiscoverySnapshot;
            }
        }
    }

    /// <summary>
    /// Gets providers whose model catalog is dynamic (user-managed, unknowable
    /// by any external method). These providers cannot have their model count
    /// verified via API, web search, or page scraping.
    /// </summary>
    public IReadOnlyList<ProviderDefinition> WithDynamicCatalog
    {
        get
        {
            lock (_sync)
            {
                if (_withDynamicCatalogSnapshot is null)
                    _withDynamicCatalogSnapshot = _allProviders.Where(p => _researchMetadata.GetValueOrDefault(p.Id)?.IsDynamicModelCatalog == true).ToList();
                return _withDynamicCatalogSnapshot;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderCatalog"/> class
    /// from the embedded JSON definitions only.
    /// </summary>
    public ProviderCatalog()
    {
        _providers = new Dictionary<string, ProviderDefinition>(StringComparer.OrdinalIgnoreCase);
        _allProviders = LoadProviders();

        foreach (var provider in _allProviders)
            _providers[provider.Id] = provider;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderCatalog"/> class, merging
    /// consumer-supplied definitions over the embedded ones. A custom definition whose id
    /// matches an embedded one (case-insensitive) replaces it in place; a new id is appended.
    /// </summary>
    /// <param name="customProviders">Optional consumer definitions merged over the embedded catalog.</param>
    public ProviderCatalog(IEnumerable<ProviderDefinition>? customProviders)
    {
        _providers = new Dictionary<string, ProviderDefinition>(StringComparer.OrdinalIgnoreCase);
        _allProviders = LoadProviders();

        foreach (var provider in _allProviders)
            _providers[provider.Id] = provider;

        if (customProviders is not null)
            Merge(customProviders);
    }

    internal void Merge(IEnumerable<ProviderDefinition> definitions)
    {
        lock (_sync)
        {
            foreach (var definition in definitions)
                Upsert(definition);
            InvalidateSnapshots();
        }
    }

    private void Upsert(ProviderDefinition definition)
    {
        ProviderDefinitionListUpsert(_allProviders, definition);
        _providers[definition.Id] = definition;
        _researchMetadata.TryAdd(definition.Id, new ProviderResearchMetadata());
    }

    internal static void ProviderDefinitionListUpsert(
        List<ProviderDefinition> list,
        ProviderDefinition definition)
    {
        var index = list.FindIndex(
            p => string.Equals(p.Id, definition.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            list[index] = definition;
        else
            list.Add(definition);
    }

    /// <summary>
    /// Gets a provider definition by ID.
    /// </summary>
    /// <param name="providerId">The provider ID.</param>
    /// <returns>The provider definition, or null if not found.</returns>
    public ProviderDefinition? Get(string providerId)
    {
        lock (_sync)
            return _providers.TryGetValue(providerId, out var provider) ? provider : null;
    }

    public ProviderResearchMetadata? GetResearchMetadata(string providerId)
    {
        lock (_sync)
            return _researchMetadata.GetValueOrDefault(providerId);
    }

    private static ProviderResearchMetadata SnapshotMetadata(ProviderResearchMetadata metadata) => metadata with
    {
        RegionalEndpoints = metadata.RegionalEndpoints?.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
    };

    private static ProviderResearchMetadata ReadResearchMetadata(string json) =>
        SnapshotMetadata(JsonSerializer.Deserialize<ProviderResearchMetadata>(json, JsonOptions)
            ?? throw new JsonException("Provider research metadata deserialized to null."));

    /// <summary>
    /// Gets providers by category.
    /// </summary>
    /// <param name="category">The category to filter by.</param>
    /// <returns>Providers in the specified category.</returns>
    public IReadOnlyList<ProviderDefinition> GetByCategory(string category)
    {
        lock (_sync)
            return _allProviders
                .Where(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    /// <summary>
    /// Reloads one or more provider definitions from disk JSON files, replacing the in-memory embedded-resource versions.
    /// </summary>
    /// <param name="manifestPath">The directory containing provider JSON files.</param>
    /// <param name="providerIds">Provider IDs to reload (file name without .json extension).</param>
    /// <returns>Number of providers successfully reloaded.</returns>
    public int ReloadFromDisk(string manifestPath, IEnumerable<string> providerIds)
    {
        var reloaded = 0;
        foreach (var providerId in providerIds)
        {
            var filePath = Path.Combine(manifestPath, $"{providerId}.json");
            if (!File.Exists(filePath))
                continue;

            // Read and parse outside the lock so readers are not blocked on I/O.
            string json;
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (_sync) { _loadErrors.Add($"Failed to reload provider '{providerId}' from disk: {ex.Message}"); }
                continue;
            }

            ProviderDefinition? provider;
            ProviderResearchMetadata? metadata;
            try
            {
                provider = JsonSerializer.Deserialize<ProviderDefinition>(json, JsonOptions);
                if (provider is null)
                {
                    lock (_sync) { _loadErrors.Add($"Reloaded provider '{providerId}' from disk but deserialized to null."); }
                    continue;
                }

                if (!string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase))
                    throw new JsonException($"Provider ID '{provider.Id}' does not match '{providerId}'.");

                metadata = ReadResearchMetadata(json);
            }
            catch (JsonException ex)
            {
                lock (_sync) { _loadErrors.Add($"Failed to reload provider '{providerId}' from disk: {ex.Message}"); }
                continue;
            }

            // Mutate collections under the lock.
            lock (_sync)
            {
                Upsert(provider);
                _researchMetadata[provider.Id] = metadata;
                InvalidateSnapshots();
            }

            reloaded++;
        }

        return reloaded;
    }

    private void InvalidateSnapshots()
    {
        _allSnapshot = null;
        _withModelDiscoverySnapshot = null;
        _withDynamicCatalogSnapshot = null;
    }

    private List<ProviderDefinition> LoadProviders()
    {
        var providers = new List<ProviderDefinition>();
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(n => n.Contains("ai_providers")
                        && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        && !n.EndsWith(".validation.json", StringComparison.OrdinalIgnoreCase));

        foreach (var resourceName in resourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            try
            {
                var provider = JsonSerializer.Deserialize<ProviderDefinition>(json, JsonOptions);
                if (provider != null)
                {
                    var metadata = ReadResearchMetadata(json);
                    providers.Add(provider);
                    _researchMetadata[provider.Id] = metadata;
                }
            }
            catch (JsonException ex)
            {
                lock (_sync) { _loadErrors.Add($"Failed to load provider definition from embedded resource '{resourceName}': {ex.Message}"); }
            }
        }

        return providers;
    }
}
