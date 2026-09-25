namespace ScraperTool.Services;

/// <summary>
/// Resolves the manifest path for provider JSON files.
/// Eliminates duplication across ViewModels.
/// </summary>
public interface IManifestPathResolver
{
    /// <summary>
    /// Resolves the manifest path from settings or falls back to searching from base directory.
    /// </summary>
    /// <returns>The full path to the ai-providers directory, or empty string if not found.</returns>
    string Resolve();
}
