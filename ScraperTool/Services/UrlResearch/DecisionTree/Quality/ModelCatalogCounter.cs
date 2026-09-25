using System.Text.RegularExpressions;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Quality;

/// <summary>
/// Default implementation of <see cref="IModelCatalogCounter"/>.
/// Extracts every Markdown link from the page, groups the links by the URL path they
/// share, and counts the entries of the group that represents the model collection.
/// Deterministic and LLM-free, so the count can only ever come from entries the page
/// actually links to.
/// </summary>
public sealed class ModelCatalogCounter : IModelCatalogCounter
{
    /// <summary>
    /// A single link is not a collection — it is usually a breadcrumb or a nav item.
    /// </summary>
    private const int MinCollectionSize = 2;

    /// <summary>
    /// Minimum size of the dominant collection when the page URL (but no link path)
    /// identifies the page as a models listing.
    /// </summary>
    private const int MinDominantCollectionSize = 8;

    private const int MaxSampleNames = 5;
    private const int MaxSampleNameLength = 60;
    private const string ModelPathToken = "model";

    /// <summary>
    /// Matches <c>[text](target)</c> but not <c>![alt](target)</c>, and stops the target at
    /// whitespace so an optional link title is not captured as part of the URL.
    /// </summary>
    private static readonly Regex MarkdownLinkRegex = new(
        @"(?<!!)\[([^\]]*)\]\(\s*([^)\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] SkippedSchemes =
    [
        "mailto:", "tel:", "javascript:", "data:", "ftp:", "file:"
    ];

    private static readonly string[] SkippedExtensions =
    [
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico", ".bmp",
        ".css", ".js", ".json", ".xml", ".yaml", ".yml", ".pdf", ".zip",
        ".mp4", ".webm", ".woff", ".woff2", ".ttf", ".eot"
    ];

    public ModelCatalogCountResult? Count(string markdown, Uri? pageUri)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return null;

        var collections = CollectLinkCollections(markdown, pageUri);
        if (collections.Count == 0)
            return null;

        var pagePath = pageUri?.AbsolutePath.TrimEnd('/');

        // Preferred evidence: a collection whose own path says it holds models.
        var result = PickBest(collections, pagePath, ModelPathToken, MinCollectionSize);

        // Fallback: the page itself is a models listing (e.g. /docs/models/all) but the
        // entries hang off a differently named path. Its dominant link cluster is the catalog.
        if (result is null
            && pagePath is not null
            && pagePath.Contains(ModelPathToken, StringComparison.OrdinalIgnoreCase))
        {
            result = PickBest(collections, pagePath, token: null, MinDominantCollectionSize);
        }

        return result;
    }

    /// <summary>
    /// Selects the largest collection whose path contains <paramref name="token"/>
    /// (or the largest collection overall when the token is null). Ties are broken by the
    /// deeper path, then alphabetically, so the outcome never depends on enumeration order.
    /// </summary>
    private static ModelCatalogCountResult? PickBest(
        Dictionary<string, Dictionary<string, string>> collections,
        string? pagePath,
        string? token,
        int minSize)
    {
        ModelCatalogCountResult? best = null;
        string? bestCollection = null;

        foreach (var (collection, entries) in collections)
        {
            if (entries.Count < minSize)
                continue;

            if (token is not null
                && !collection.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A link back to the page itself is navigation, not a catalog entry.
            var count = pagePath is not null && entries.ContainsKey(pagePath)
                ? entries.Count - 1
                : entries.Count;

            if (count < minSize)
                continue;

            if (best is not null
                && (count < best.Count
                    || (count == best.Count
                        && (collection.Length < bestCollection!.Length
                            || (collection.Length == bestCollection.Length
                                && string.CompareOrdinal(collection, bestCollection) >= 0)))))
            {
                continue;
            }

            best = new ModelCatalogCountResult(
                count,
                collection,
                BuildSampleNames(entries, pagePath));
            bestCollection = collection;
        }

        return best;
    }

    /// <summary>
    /// Groups every resolvable Markdown link by the path it lives under
    /// (e.g. <c>/api/docs/models/gpt-5</c> → collection <c>/api/docs/models</c>).
    /// </summary>
    private static Dictionary<string, Dictionary<string, string>> CollectLinkCollections(
        string markdown,
        Uri? pageUri)
    {
        var collections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in MarkdownLinkRegex.Matches(markdown))
        {
            var linkText = match.Groups[1].Value;
            var target = match.Groups[2].Value;

            if (!TryResolvePath(target, pageUri, out var collection, out var path))
                continue;

            if (!collections.TryGetValue(collection, out var entries))
            {
                entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                collections[collection] = entries;
            }

            // Distinct targets only: a catalog row often links the same model several
            // times (name, docs, changelog) and must not be counted more than once.
            entries.TryAdd(path, CleanLinkText(linkText, path));
        }

        return collections;
    }

    /// <summary>
    /// Resolves a Markdown link target to an absolute path and the collection it belongs to.
    /// Returns false for anchors, non-HTTP schemes, assets, off-site links, and single-segment
    /// paths (which have no collection to belong to).
    /// </summary>
    private static bool TryResolvePath(
        string target,
        Uri? pageUri,
        out string collection,
        out string path)
    {
        collection = string.Empty;
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(target))
            return false;

        var trimmed = target.Trim().Trim('<', '>');
        if (trimmed.Length == 0 || trimmed[0] == '#')
            return false;

        foreach (var scheme in SkippedSchemes)
        {
            if (trimmed.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        string absolutePath;
        string? host;

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
                return false;

            host = absolute.Host;
            absolutePath = absolute.AbsolutePath;
        }
        else if (trimmed[0] == '/')
        {
            host = null;
            absolutePath = trimmed;
        }
        else
        {
            // A relative link can only be resolved against the page it came from.
            if (pageUri is null || !Uri.TryCreate(pageUri, trimmed, out var resolved))
                return false;

            host = resolved.Host;
            absolutePath = resolved.AbsolutePath;
        }

        // Off-site links are not entries of this provider's catalog.
        if (host is not null
            && pageUri is not null
            && !IsSameOrSubDomain(host, pageUri.Host))
        {
            return false;
        }

        var cut = absolutePath.IndexOfAny(['?', '#']);
        if (cut >= 0)
            absolutePath = absolutePath[..cut];

        if (absolutePath.Length > 1)
            absolutePath = absolutePath.TrimEnd('/');

        foreach (var extension in SkippedExtensions)
        {
            if (absolutePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        var lastSlash = absolutePath.LastIndexOf('/');
        if (lastSlash <= 0)
            return false;

        collection = absolutePath[..lastSlash];
        path = absolutePath;
        return true;
    }

    private static bool IsSameOrSubDomain(string host, string rootHost)
    {
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];

        var root = rootHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? rootHost[4..]
            : rootHost;

        return string.Equals(host, root, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + root, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildSampleNames(
        Dictionary<string, string> entries,
        string? pagePath)
    {
        var names = new List<string>(MaxSampleNames);

        foreach (var (path, text) in entries)
        {
            if (names.Count == MaxSampleNames)
                break;

            if (pagePath is not null
                && string.Equals(path, pagePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            names.Add(text);
        }

        return names;
    }

    private static string CleanLinkText(string linkText, string path)
    {
        var text = Regex.Replace(linkText, @"\s+", " ").Trim();

        if (text.Length == 0)
        {
            // Fall back to the last path segment, which is usually the model slug.
            var lastSlash = path.LastIndexOf('/');
            text = lastSlash >= 0 && lastSlash < path.Length - 1 ? path[(lastSlash + 1)..] : path;
        }

        return text.Length > MaxSampleNameLength ? text[..MaxSampleNameLength] : text;
    }
}
