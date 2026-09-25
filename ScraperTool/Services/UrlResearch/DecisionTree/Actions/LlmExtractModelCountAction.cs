using System.Globalization;
using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using ScraperTool.Models;
using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Actions;

/// <summary>
/// Extracts the number of models a provider lists, in descending order of trust:
/// (1) a deterministic count of the model entries the whole fetched page links to,
/// (2) an LLM reading of the page whose answer is then checked mechanically against that
/// page: the span it cites must state the number, or it must name every entry it counted
/// and each name must be present. Nothing is extracted by matching number patterns in
/// prose: how a provider words and formats its catalog is unknowable in advance, so a
/// pattern can only ever guess. Stores the count in state["modelCount"] for downstream
/// comparison, together with the method and the page URL it came from.
/// </summary>
public sealed class LlmExtractModelCountAction : IDecisionAction
{
    private const string SystemPrompt = """
        You are a data extraction assistant. Extract the number of available AI models from the provided page content.
        
        Use these strategies in priority order:
        1. Look for explicit counts like "100+ models", "over 500 models", "supports 15 models".
        2. Count individual model entries listed in a table or list (e.g. rows with model names).
        
        Also check data freshness: if dates on the page (release dates, update timestamps, "last updated") are more than 6 months before today, set "stale": true.
        
        Return a JSON object with these fields:
        - "count": the number of models (integer), or null if no models are mentioned at all
        - "quote": the exact text from the page where you found the count or the first few model names (short phrase, max 50 words)
        - "modelNames": when you counted listed entries to reach the total, the name of every entry you counted, copied verbatim from the page. Omit this field when the page states the count itself.
        - "stale": true if the data appears outdated (dates older than 6 months), false otherwise
        
        Your answer is verified against the page. A count is accepted only when the quoted text states that exact number, or when "modelNames" holds exactly that many names and every one of them appears on the page. Never estimate, round, or report a total the page does not support.
        
        If the page lists individual models (even just one), count them and report the total.
        Only return null if the page contains no model information whatsoever.
        """;

    private const int MaxEvidenceLength = 4000;
    private const int MaxQuoteLength = 140;

    /// <summary>
    /// Largest count that can be certified by enumeration. A page listing more models than
    /// this links to them, so the deterministic catalog count has already answered.
    /// </summary>
    private const int MaxCertifiedModelNames = 100;

    private const int MaxModelNameLength = 80;

    private const string CatalogLinkMethod = "catalog-links";
    private const string LlmMethod = "llm";

    private readonly IAIProviderFactory _providerFactory;
    private readonly AppSettings _settings;
    private readonly ProviderResearchCache _cache;
    private readonly IModelCatalogCounter _catalogCounter;

    public string Key => "llmExtractModelCount";

    public LlmExtractModelCountAction(
        IAIProviderFactory providerFactory,
        AppSettings settings,
        ProviderResearchCache cache,
        IModelCatalogCounter catalogCounter)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _catalogCounter = catalogCounter ?? throw new ArgumentNullException(nameof(catalogCounter));
    }

    public async Task<DecisionActionResult> ExecuteAsync(
        DecisionActionContext context,
        CancellationToken cancellationToken = default)
    {
        var pageUrl = GetPageUrl(context);

        // The whole page, not the bounded excerpt the classifier sees: counting model
        // entries over a truncated window can only undercount, and the excerpt (top-3
        // candidate regions) drops most of a long catalog.
        var fullPage = GetPageText(context, pageUrl);
        var curatedPage = FindCurrentPageText(context);
        var pageText = !string.IsNullOrWhiteSpace(fullPage) ? fullPage : curatedPage;

        // Curated regions are the better LLM input (navigation stripped); the raw page is
        // the better deterministic input (nothing removed).
        var llmEvidence = !string.IsNullOrWhiteSpace(curatedPage) ? curatedPage : pageText;

        if (string.IsNullOrWhiteSpace(pageText) && string.IsNullOrWhiteSpace(llmEvidence))
        {
            var snippetEvidence = CollectSearchSnippetEvidence(context);
            if (string.IsNullOrWhiteSpace(snippetEvidence))
            {
                return new DecisionActionResult(
                    null,
                    new Dictionary<string, string> { ["extractResult"] = "no-evidence" },
                    DecisionActionStatus.PermanentFailure,
                    "No evidence text available for model count extraction.");
            }

            llmEvidence = snippetEvidence;
        }

        // 1. Count the model entries the page actually links to. Not gated on data
        //    freshness: the page was fetched moments ago, so its links are current even
        //    when the release dates it mentions are old.
        if (!string.IsNullOrWhiteSpace(pageText))
        {
            var catalog = _catalogCounter.Count(pageText, ResolvePageUri(pageUrl));
            if (catalog is { Count: > 0 })
            {
                return CreateSuccessResult(
                    context, catalog.Count, CatalogLinkMethod, pageUrl, BuildCatalogQuote(catalog));
            }
        }

        // 2. The LLM reads the page. It is the only source that can handle a count stated
        //    in prose, because it understands the wording instead of matching it: a page
        //    may say "over 250 models", "12k models", or "two hundred and fifty entries",
        //    and no fixed pattern can anticipate how a provider phrases its catalog.
        if (!string.IsNullOrWhiteSpace(llmEvidence) && IsLlmConfigured())
        {
            var boundedEvidence = llmEvidence.Length > MaxEvidenceLength
                ? llmEvidence[..MaxEvidenceLength]
                : llmEvidence;

            try
            {
                var llmResult = await ExtractWithLlmAsync(boundedEvidence, cancellationToken);

                if (llmResult is { Stale: true })
                {
                    return new DecisionActionResult(
                        null,
                        new Dictionary<string, string> { ["extractResult"] = "data-stale" },
                        DecisionActionStatus.TransientFailure,
                        "The page describes an outdated catalog; ignoring its model count.");
                }

                if (llmResult is { Count: > 0 })
                {
                    return CreateSuccessResult(
                        context, llmResult.Value.Count, LlmMethod, pageUrl, llmResult.Value.Quote);
                }
            }
            catch (Exception)
            {
                // LLM failed — no further extraction source is available.
            }
        }

        return new DecisionActionResult(
            null,
            new Dictionary<string, string> { ["extractResult"] = "not-found" },
            DecisionActionStatus.TransientFailure,
            "Could not extract a model count from the page via catalog links or the LLM.");
    }

    private bool IsLlmConfigured()
    {
        return !string.IsNullOrWhiteSpace(_settings.ApiKey)
            && !string.IsNullOrWhiteSpace(_settings.SelectedProviderId)
            && (!string.IsNullOrWhiteSpace(_settings.PrimaryModel)
                || !string.IsNullOrWhiteSpace(_settings.FallbackModel));
    }

    private async Task<(int Count, string Quote, bool Stale)?> ExtractWithLlmAsync(
        string evidence,
        CancellationToken ct)
    {
        var provider = _providerFactory.GetProvider(_settings.SelectedProviderId ?? string.Empty);
        var request = new ChatCompletionRequest
        {
            Model = _settings.PrimaryModel ?? _settings.FallbackModel ?? string.Empty,
            Messages =
            [
                new ChatMessage { Role = EChatRole.System, Content = SystemPrompt },
                new ChatMessage { Role = EChatRole.User, Content = $"Extract the model count from this page content:\n\n{evidence}" }
            ],
            Temperature = 0.1f
        };

        var response = await provider.ChatAsync(request, ct);

        if (string.IsNullOrWhiteSpace(response?.Content))
        {
            return null;
        }

        var content = StripCodeFence(response.Content.Trim());

        // Try to parse JSON response: {"count": 100, "quote": "..."}
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Check if count is null or missing
            if (!root.TryGetProperty("count", out var countProp)
                || countProp.ValueKind == System.Text.Json.JsonValueKind.Null)
            {
                return null;
            }

            // Parse the count
            int count;
            if (countProp.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                count = countProp.GetInt32();
            }
            else if (countProp.ValueKind == System.Text.Json.JsonValueKind.String
                && int.TryParse(countProp.GetString(), out var parsed))
            {
                count = parsed;
            }
            else
            {
                return null;
            }

            if (count <= 0)
                return null;

            // Extract the quote
            var quote = root.TryGetProperty("quote", out var quoteProp)
                && quoteProp.ValueKind == System.Text.Json.JsonValueKind.String
                    ? quoteProp.GetString() ?? string.Empty
                    : string.Empty;

            // A verbatim quote proves the model looked at real page text; it does not prove
            // the number came from it. Accept the answer only when the count is attested
            // mechanically: the cited span states that number, or the model enumerated the
            // entries it counted and every one of them is on the page.
            if (string.IsNullOrWhiteSpace(quote)
                || !evidence.Contains(quote, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!QuoteAttestsCount(quote, count)
                && !NamesAttestCount(ReadModelNames(root), count, evidence))
            {
                return null;
            }

            return (count, quote, IsStaleFlagSet(root));
        }
        catch (JsonException)
        {
            // Not valid JSON — no trustworthy count in this reply.
        }

        // A free-text reply is not a count source: scraping the first digits out of the
        // model's prose produced fabricated values (e.g. "1" for a provider listing
        // hundreds of models) which downstream steps could not distinguish from a quoted
        // count, so they were suggested verbatim. Only a reply that states a count and
        // quotes the page back is accepted.
        return null;
    }

    /// <summary>
    /// Removes markdown code fences so a fenced JSON object can still be parsed.
    /// Small models frequently wrap their JSON reply in a ```json block.
    /// </summary>
    private static string StripCodeFence(string content)
    {
        if (!content.StartsWith("```", StringComparison.Ordinal))
            return content;

        var openingLineEnd = content.IndexOf('\n');
        if (openingLineEnd < 0)
            return content;

        var closingFence = content.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFence <= openingLineEnd)
            return content;

        return content[(openingLineEnd + 1)..closingFence].Trim();
    }

    /// <summary>
    /// Reads the freshness verdict the model was asked for. Only an explicit <c>true</c>
    /// counts as stale: a missing or malformed flag must not discard a good count.
    /// </summary>
    private static bool IsStaleFlagSet(System.Text.Json.JsonElement root)
    {
        return root.TryGetProperty("stale", out var staleProp)
            && staleProp.ValueKind == System.Text.Json.JsonValueKind.True;
    }

    /// <summary>
    /// Reads the enumeration the model claims to have counted, so each name can be checked
    /// against the page it says they came from.
    /// </summary>
    private static IReadOnlyList<string> ReadModelNames(System.Text.Json.JsonElement root)
    {
        if (!root.TryGetProperty("modelNames", out var namesProp)
            || namesProp.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>(namesProp.GetArrayLength());

        foreach (var item in namesProp.EnumerateArray())
        {
            if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                names.Add(item.GetString() ?? string.Empty);
        }

        return names;
    }

    /// <summary>
    /// Checks whether the span the model cited actually states the number it reported.
    /// An invented total quoted against a real sentence fails here: the sentence exists on
    /// the page, but it does not say that number.
    /// </summary>
    private static bool QuoteAttestsCount(string quote, int count)
    {
        foreach (var value in ReadNumericTokens(quote))
        {
            if (value == count)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks an enumerated count: the model must have named every entry it counted, and
    /// each name must appear on the page. Counting is where a model is most likely to
    /// invent a total, so the total is accepted only when the items behind it can be
    /// verified one by one.
    /// </summary>
    private static bool NamesAttestCount(IReadOnlyList<string> names, int count, string evidence)
    {
        if (names.Count == 0 || names.Count != count || count > MaxCertifiedModelNames)
            return false;

        foreach (var name in names)
        {
            var trimmed = name.Trim();

            if (trimmed.Length == 0
                || trimmed.Length > MaxModelNameLength
                || !evidence.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads every number written in a short span, so a reported count can be compared
    /// against what the span says: plain digits ("250"), thousands grouping ("1,200") and
    /// thousands suffixes ("12k", "1.5k"). Digit runs are read as whole numbers, so a
    /// count of 25 is not attested by a span saying "250".
    /// </summary>
    private static IEnumerable<int> ReadNumericTokens(string text)
    {
        var i = 0;

        while (i < text.Length)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
                i++;

            // A comma groups thousands only when exactly three digits follow it.
            while (i + 3 < text.Length
                && text[i] == ','
                && char.IsAsciiDigit(text[i + 1])
                && char.IsAsciiDigit(text[i + 2])
                && char.IsAsciiDigit(text[i + 3])
                && (i + 4 >= text.Length || !char.IsAsciiDigit(text[i + 4])))
            {
                i += 4;
            }

            // A fractional part belongs to the number only when a thousands suffix follows,
            // so a model name such as "gpt-3.5" is not read as the number 3.5.
            var suffixStart = -1;
            if (i < text.Length && text[i] == '.')
            {
                var j = i + 1;
                while (j < text.Length && char.IsAsciiDigit(text[j]))
                    j++;

                if (j > i + 1 && j < text.Length && (text[j] == 'k' || text[j] == 'K'))
                    suffixStart = j;
            }

            if (suffixStart > 0)
                i = suffixStart;

            // The suffix multiplies the number but is not part of it: "12k" parses as 12.
            var numberEnd = i;

            var multiplier = 1;
            if (i < text.Length && (text[i] == 'k' || text[i] == 'K'))
            {
                multiplier = 1000;
                i++;
            }

            if (double.TryParse(
                    text[start..numberEnd],
                    NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                yield return (int)Math.Round(value * multiplier);
            }
        }
    }

    /// <summary>
    /// Returns the URL of the page the tree is currently examining, as recorded by
    /// fetch-next-candidate or fetch-models-page.
    /// </summary>
    private static string? GetPageUrl(DecisionActionContext context)
    {
        foreach (var urlKey in new[] { "lastFetchedUrl", "modelsPageUrl" })
        {
            if (context.State.Properties.TryGetValue(urlKey, out var urlObj)
                && urlObj is string url
                && !string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the complete Markdown of the page being examined, taken from the batch cache
    /// that fetch-next-candidate populates. Falls back to the PageText evidence, which is the
    /// region-selected excerpt (or the full page when it came from fetch-models-page).
    /// </summary>
    private string? GetPageText(DecisionActionContext context, string? pageUrl)
    {
        if (!string.IsNullOrWhiteSpace(pageUrl))
        {
            var cached = _cache.GetPageFetch(pageUrl);
            if (cached is { Success: true } && !string.IsNullOrWhiteSpace(cached.MarkdownContent))
                return cached.MarkdownContent;
        }

        return FindCurrentPageText(context);
    }

    /// <summary>
    /// Resolves the URI that relative model links are measured against. The redirect target is
    /// preferred because that is the page the Markdown actually came from.
    /// </summary>
    private Uri? ResolvePageUri(string? pageUrl)
    {
        if (string.IsNullOrWhiteSpace(pageUrl))
            return null;

        var cached = _cache.GetPageFetch(pageUrl);
        var effectiveUrl = !string.IsNullOrWhiteSpace(cached?.FinalUrl) ? cached!.FinalUrl : pageUrl;

        return Uri.TryCreate(effectiveUrl, UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>
    /// Builds a short, traceable description of a structural count: where the entries were
    /// found and what they were called.
    /// </summary>
    private static string BuildCatalogQuote(ModelCatalogCountResult catalog)
    {
        var names = catalog.SampleModelNames.Count == 0
            ? string.Empty
            : $": {string.Join(", ", catalog.SampleModelNames)}";

        var quote = $"{catalog.Count} model entries under {catalog.CollectionPath}{names}";

        return quote.Length > MaxQuoteLength ? quote[..MaxQuoteLength] + "…" : quote;
    }

    /// <summary>
    /// Collects search-result snippets and titles as a last-resort evidence source, used only
    /// when no page content is available at all.
    /// </summary>
    private static string? CollectSearchSnippetEvidence(DecisionActionContext context)
    {
        var texts = new List<string>();

        foreach (var item in context.Data.GetAll())
        {
            if (!string.Equals(item.Type, "SearchResult", StringComparison.OrdinalIgnoreCase)
                || item.Metadata is null)
            {
                continue;
            }

            if (item.Metadata.TryGetValue("snippet", out var snippet) && !string.IsNullOrWhiteSpace(snippet))
                texts.Add(snippet);

            if (item.Metadata.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
                texts.Add(title);
        }

        // Return the longest text (most likely to contain the count)
        return texts.OrderByDescending(t => t.Length).FirstOrDefault();
    }

    /// <summary>
    /// Returns the content of the page the tree fetched most recently, or null when no
    /// page content is available. The page is identified by the URL recorded in state by
    /// fetch-next-candidate or fetch-models-page; the newest page is used as a fallback.
    /// </summary>
    private static string? FindCurrentPageText(DecisionActionContext context)
    {
        var pageItems = context.Data
            .GetAll()
            .Where(item => string.Equals(item.Type, "PageText", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(item.Content))
            .ToList();

        if (pageItems.Count == 0)
            return null;

        foreach (var urlKey in new[] { "lastFetchedUrl", "modelsPageUrl" })
        {
            if (context.State.Properties.TryGetValue(urlKey, out var urlObj)
                && urlObj is string url
                && !string.IsNullOrWhiteSpace(url))
            {
                var match = pageItems.LastOrDefault(item =>
                    string.Equals(item.Source, url, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    return match.Content;
            }
        }

        return pageItems[^1].Content;
    }

    private static DecisionActionResult CreateSuccessResult(
        DecisionActionContext context,
        int count,
        string method,
        string? sourceUrl,
        string? quote = null)
    {
        context.State.Properties["modelCount"] = count;
        context.State.Properties["modelCountMethod"] = method;
        if (!string.IsNullOrWhiteSpace(quote))
            context.State.Properties["modelCountQuote"] = quote;
        if (!string.IsNullOrWhiteSpace(sourceUrl))
            context.State.Properties["modelCountSourceUrl"] = sourceUrl;

        var evidenceContent = string.IsNullOrWhiteSpace(quote)
            ? $"Extracted model count: {count} (method: {method})"
            : $"Extracted model count: {count} (method: {method}, quote: \"{quote}\")";

        var evidence = new DecisionData
        {
            Id = $"extracted-count-{context.NodeId}",
            Source = "extraction",
            Type = "ExtractedModelCount",
            Content = evidenceContent,
            CreatedAt = DateTimeOffset.UtcNow,
            ActionId = context.NodeId,
            Metadata = new Dictionary<string, string>
            {
                ["modelCount"] = count.ToString(),
                ["method"] = method,
                ["quote"] = quote ?? string.Empty,
                ["sourceUrl"] = sourceUrl ?? string.Empty
            }
        };

        return new DecisionActionResult(
            new[] { evidence },
            new Dictionary<string, string>
            {
                ["extractResult"] = "success",
                ["modelCount"] = count.ToString(),
                ["method"] = method,
                ["quote"] = quote ?? string.Empty,
                ["sourceUrl"] = sourceUrl ?? string.Empty
            },
            DecisionActionStatus.Success);
    }
}
