using AIProviderConnect.Models;

namespace AIProviderConnect.Abstractions;

/// <summary>
/// Read-only access to provider definitions.
/// </summary>
public interface IProviderCatalog
{
    /// <summary>Gets all available provider definitions.</summary>
    IReadOnlyList<ProviderDefinition> All { get; }

    /// <summary>Gets a provider definition by ID, or null if not found.</summary>
    ProviderDefinition? Get(string providerId);

    /// <summary>Gets the separate research metadata for a provider, or null if not found.</summary>
    ProviderResearchMetadata? GetResearchMetadata(string providerId);

    IReadOnlyList<ProviderDefinition> WithModelDiscovery { get; }
    IReadOnlyList<ProviderDefinition> WithDynamicCatalog { get; }
    IReadOnlyList<string> LoadErrors { get; }
    IReadOnlyList<ProviderDefinition> GetByCategory(string category);
}
