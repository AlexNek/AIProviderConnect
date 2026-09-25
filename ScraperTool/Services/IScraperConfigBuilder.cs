using AIProviderConnect.Models;

using ScraperTool.Models;

namespace ScraperTool.Services;

/// <summary>
/// Strategy interface for building provider-specific scraper configurations.
/// Follows Open/Closed Principle - add new providers without modifying existing code.
/// </summary>
public interface IScraperConfigBuilder
{
    /// <summary>
    /// Builds a scraper configuration for the given provider.
    /// </summary>
    /// <param name="provider">Runtime provider definition.</param>
    /// <param name="research">Research metadata (pricing URL, etc.), if available.</param>
    ScraperConfiguration Build(ProviderDefinition provider, ProviderResearchMetadata? research);

    /// <summary>
    /// Determines if this builder can handle the given provider.
    /// </summary>
    bool CanBuild(ProviderDefinition provider);
}
