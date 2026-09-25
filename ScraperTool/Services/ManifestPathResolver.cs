using ScraperTool.Models;

namespace ScraperTool.Services;

/// <summary>
/// Default implementation of IManifestPathResolver.
/// Resolves manifest path from AppSettings or searches upward from base directory.
/// </summary>
public sealed class ManifestPathResolver : IManifestPathResolver
{
    private readonly AppSettings _settings;

    public ManifestPathResolver(AppSettings settings)
    {
        _settings = settings;
    }

    public string Resolve()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ManifestPath)
            && System.IO.Directory.Exists(_settings.ManifestPath))
            return System.IO.Path.GetFullPath(_settings.ManifestPath);

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = System.IO.Path.Combine(dir, "AIProviderConnectLib", "ai-providers");
            if (System.IO.Directory.Exists(candidate))
                return System.IO.Path.GetFullPath(candidate);

            var parent = System.IO.Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        return string.Empty;
    }
}
