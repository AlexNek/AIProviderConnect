using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using ScraperTool.Models;

namespace ScraperTool.Services.Parsing;

/// <summary>
/// Sends a page HTML snippet to the AI model and asks it to determine
/// the correct XPath selectors for model names, input prices, and output prices.
/// </summary>
public sealed class AiLayoutAnalyzer : IAiLayoutAnalyzer
{
    private readonly IAIProviderFactory _providerFactory;

    private readonly AppSettings _settings;

    private bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.ApiKey)
        && !string.IsNullOrWhiteSpace(_settings.SelectedProviderId);

    public AiLayoutAnalyzer(IAIProviderFactory providerFactory, AppSettings settings)
    {
        _providerFactory = providerFactory;
        _settings = settings;
    }

    public async Task<LayoutAnalysis?> AnalyzeAsync(
        string htmlSnippet,
        string providerId,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return null;

        // Truncate to avoid token limits — first 6KB is usually enough to see structure
        var snippet = htmlSnippet.Length > 6000 ? htmlSnippet[..6000] : htmlSnippet;

        var systemPrompt = """
                           You are a web scraping expert. Analyze the given HTML snippet from an AI provider's pricing page.
                           Determine the XPath selectors needed to extract:
                           1. Each pricing row/item (the repeating container)
                           2. The AI model name within each row
                           3. The input/prompt price (per million tokens) within each row
                           4. The output/completion price (per million tokens) within each row

                           Respond ONLY with a JSON object (no markdown, no explanation):
                           {
                             "rowSelector": "XPath for each pricing row",
                             "modelNameSelector": "XPath for model name within a row",
                             "inputPriceSelector": "XPath for input price within a row",
                             "outputPriceSelector": "XPath for output price within a row",
                             "confidence": 0.0 to 1.0,
                             "layoutType": "table" or "div" or "list",
                             "reasoning": "Brief explanation of the page structure"
                           }

                           Important:
                           - Use relative XPath (starting with . or .//) for selectors within a row
                           - The rowSelector should be absolute from the document root (starting with //)
                           - If you can't determine a selector confidently, set confidence below 0.5
                           - Prices are typically per 1M tokens and shown as dollar amounts
                           """;

        var userPrompt = $"Provider: {providerId}\n\nHTML snippet:\n{snippet}";

        var response = await SendPromptAsync(systemPrompt, userPrompt, ct);
        if (string.IsNullOrWhiteSpace(response))
            return null;

        return ParseResponse(response);
    }

    private static LayoutAnalysis? ParseResponse(string json)
    {
        try
        {
            // Strip markdown code fences if AI wraps in ```json ... ```
            json = json.Trim();
            if (json.StartsWith("```"))
            {
                var start = json.IndexOf('{');
                var end = json.LastIndexOf('}');
                if (start >= 0 && end > start)
                    json = json[start..(end + 1)];
            }

            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new LayoutAnalysis
                       {
                           RowSelector =
                               root.GetProperty("rowSelector").GetString() ?? string.Empty,
                           ModelNameSelector =
                               root.GetProperty("modelNameSelector").GetString() ?? string.Empty,
                           InputPriceSelector =
                               root.GetProperty("inputPriceSelector").GetString() ?? string.Empty,
                           OutputPriceSelector =
                               root.GetProperty("outputPriceSelector").GetString() ?? string.Empty,
                           Confidence =
                               root.TryGetProperty("confidence", out var conf)
                                   ? conf.GetDouble()
                                   : 0.5,
                           LayoutType =
                               root.TryGetProperty("layoutType", out var lt)
                                   ? lt.GetString() ?? "unknown"
                                   : "unknown",
                           Reasoning = root.TryGetProperty("reasoning", out var r)
                                           ? r.GetString() ?? string.Empty
                                           : string.Empty,
                       };
        }
        catch (Exception)
        {
            // JSON parse or unexpected shape — return null gracefully.
            return null;
        }
    }

    private async Task<string?> SendPromptAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct)
    {
        var provider = _providerFactory.GetProvider(_settings.SelectedProviderId ?? string.Empty);
        var request = new ChatCompletionRequest
                          {
                              Model = string.Empty,
                              Messages =
                                  [
                                      new ChatMessage
                                          {
                                              Role = EChatRole.System, Content = systemPrompt
                                          },
                                      new ChatMessage { Role = EChatRole.User, Content = userPrompt }
                                  ],
                              Temperature = 0.1f
                          };
        var response = await provider.ChatAsync(request, ct);
        return response?.Content;
    }
}
