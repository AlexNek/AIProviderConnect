using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Actions;

/// <summary>
/// Fetches the provider's documentation page so that <see cref="LlmExtractModelCountAction"/>
/// can extract a model count from its content. Used when the models endpoint probe could not
/// return a count (auth-gated or undocumented) but the documentation page may list the
/// available models.
/// <para>
/// The fetched page is stored in state as <c>lastFetchedUrl</c> and cached in
/// <see cref="ProviderResearchCache"/> so the downstream extraction action finds it via
/// its existing page-text lookup without any modification.
/// </para>
/// </summary>
public sealed class FetchDocumentationPageAction : IDecisionAction
{
    private readonly IWebContentFetcher _fetcher;
    private readonly ProviderResearchCache _cache;

    public string Key => "fetchDocumentationPage";

    public FetchDocumentationPageAction(
        IWebContentFetcher fetcher,
        ProviderResearchCache cache)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<DecisionActionResult> ExecuteAsync(
        DecisionActionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!context.TemplateParameters.TryGetValue("siblingUrls", out var rawSiblings)
            || string.IsNullOrWhiteSpace(rawSiblings))
        {
            return new DecisionActionResult(
                null,
                null,
                DecisionActionStatus.PermanentFailure,
                "No sibling URLs available to locate the documentation page.");
        }

        var documentationUrl = ExtractDocumentationUrl(rawSiblings);
        if (string.IsNullOrWhiteSpace(documentationUrl))
        {
            return new DecisionActionResult(
                null,
                null,
                DecisionActionStatus.PermanentFailure,
                "No documentationUrl found in sibling URLs.");
        }

        if (context.State.Properties.ContainsKey("documentationPageAttempted"))
        {
            return new DecisionActionResult(
                null,
                null,
                DecisionActionStatus.PermanentFailure,
                $"Documentation page fetch already attempted — falling through. ({documentationUrl})");
        }

        context.State.Properties["documentationPageAttempted"] = true;

        try
        {
            var result = await _fetcher.FetchAsAsync(
                documentationUrl,
                EContentFormat.Markdown,
                ct: cancellationToken);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
            {
                var failDetail = result.ErrorMessage ?? "empty response";
                return new DecisionActionResult(
                    null,
                    null,
                    DecisionActionStatus.TransientFailure,
                    $"{failDetail} — {documentationUrl}");
            }

            _cache.SetPageFetch(documentationUrl, new PageFetchCacheEntry
            {
                MarkdownContent = result.Content,
                Success = true,
                FinalUrl = result.FinalUrl
            });

            context.State.Properties["lastFetchedUrl"] = documentationUrl;

            var pageData = new DecisionData
            {
                Id = $"doc-page-{Guid.NewGuid():N}",
                Source = documentationUrl,
                Type = "PageText",
                Content = result.Content,
                CreatedAt = DateTimeOffset.UtcNow,
                ActionId = context.NodeId,
                Metadata = new Dictionary<string, string>
                {
                    ["url"] = documentationUrl,
                    ["contentType"] = "documentation-page"
                }
            };

            return new DecisionActionResult(
                new[] { pageData },
                new Dictionary<string, string>
                {
                    ["fetchResult"] = "success",
                    ["url"] = documentationUrl,
                    ["contentLength"] = result.Content.Length.ToString()
                },
                DecisionActionStatus.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DecisionActionResult(
                null,
                null,
                DecisionActionStatus.TransientFailure,
                $"{ex.Message} — {documentationUrl}");
        }
    }

    /// <summary>
    /// Extracts the documentationUrl value from the semicolon-separated sibling URL list.
    /// Format: "field1=url1;field2=url2".
    /// </summary>
    internal static string? ExtractDocumentationUrl(string rawSiblings)
    {
        foreach (var segment in rawSiblings.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = segment.IndexOf('=');
            if (equalsIndex <= 0 || equalsIndex >= segment.Length - 1)
                continue;

            var fieldKey = segment[..equalsIndex].Trim();
            var url = segment[(equalsIndex + 1)..].Trim();

            if (string.Equals(fieldKey, "documentationUrl", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }

        return null;
    }
}
