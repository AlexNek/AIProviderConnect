using System.Net.Http;

using HtmlAgilityPack;

using ScraperTool.Models;
using ScraperTool.Services.Parsing;

using WebTools.NET.Abstractions;

namespace ScraperTool.Services;

/// <summary>
/// Orchestrates price scraping:
/// 1. Fetches HTML (static or Playwright-rendered)
/// 2. Delegates parsing to <see cref="CompositePricingParser"/> (table → div → AI-guided)
/// 3. Falls back to Playwright text extraction if HTML parsing fails
/// 4. All results are validated before returning
/// </summary>
public sealed class PriceScraperService
{
    private readonly IWebContentFetcher? _browserFetcher;

    private readonly HttpClient _http;

    private readonly CompositePricingParser _parser;

    public PriceScraperService(
        HttpClient http,
        IWebContentFetcher? browserFetcher = null,
        IAiLayoutAnalyzer? aiAnalyzer = null)
    {
        _http = http;
        _browserFetcher = browserFetcher;
        _parser = new CompositePricingParser(aiAnalyzer);
    }

    public async Task<IReadOnlyList<ProviderPriceResult>> ScrapeAsync(
        ScraperConfiguration config,
        CancellationToken ct = default)
    {
        var result = await ScrapeWithDiagnosticsAsync(config, ct);
        return result.Prices;
    }

    public async Task<ScrapeResult> ScrapeWithDiagnosticsAsync(
        ScraperConfiguration config,
        CancellationToken ct = default)
    {
        return await ScrapeInternalAsync(config, preRenderedHtml: null, ct);
    }

    private async Task<ScrapeResult> FallbackOrEmpty(
        ScraperConfiguration config,
        string html,
        string? preRenderedHtml,
        string diagnostic,
        CancellationToken ct)
    {
        if (preRenderedHtml is null && _browserFetcher is not null)
        {
            var browserResult = await TryBrowserFallbackAsync(config, html, diagnostic, ct);
            if (browserResult is not null)
                return browserResult;
        }

        return ScrapeResult.Empty(diagnostic);
    }

    private async Task<ScrapeResult> ScrapeInternalAsync(
        ScraperConfiguration config,
        string? preRenderedHtml,
        CancellationToken ct)
    {
        string html;
        string fetchMethod;

        if (preRenderedHtml is not null)
        {
            html = preRenderedHtml;
            fetchMethod = "Playwright (browser-rendered)";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(config.ApiPricingUrl))
                return ScrapeResult.Empty("No ApiPricingUrl configured — cannot fetch pricing page.");

            html = await _http.GetStringAsync(config.ApiPricingUrl, ct);
            fetchMethod = "HttpClient (static HTML)";
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var root = string.IsNullOrWhiteSpace(config.TableXPath)
                       ? doc.DocumentNode
                       : doc.DocumentNode.SelectSingleNode(config.TableXPath);

        if (root is null)
        {
            var diagnostic =
                $"[{fetchMethod}] TableXPath '{config.TableXPath}' matched no node ({html.Length:N0} chars).";
            return await FallbackOrEmpty(config, html, preRenderedHtml, diagnostic, ct);
        }

        // Step 1: Try standard parsers (table → div)
        var parseResult = _parser.Parse(root, config);
        if (parseResult.Success)
            return new ScrapeResult(
                parseResult.Prices.ToList(),
                $"[{fetchMethod}] {parseResult.Diagnostic}");

        // Step 2: Try AI-guided parsing
        var aiResult = await _parser.ParseWithAiAsync(root, html, config, ct);
        if (aiResult.Success)
            return new ScrapeResult(
                aiResult.Prices.ToList(),
                $"[{fetchMethod}] {aiResult.Diagnostic}");

        // Step 3: Fallback to Playwright
        var fallbackDiag =
            $"[{fetchMethod}] {parseResult.Diagnostic} {aiResult.Diagnostic} Page size: {html.Length:N0} chars.";
        return await FallbackOrEmpty(config, html, preRenderedHtml, fallbackDiag, ct);
    }

    private async Task<ScrapeResult?> TryBrowserFallbackAsync(
        ScraperConfiguration config,
        string staticHtml,
        string staticDiagnostic,
        CancellationToken ct)
    {
        if (_browserFetcher is null)
            return null;

        if (string.IsNullOrWhiteSpace(config.ApiPricingUrl))
            return null;

        var webContent = await _browserFetcher.FetchAsync(config.ApiPricingUrl, ct: ct);

        if (!webContent.Success || string.IsNullOrWhiteSpace(webContent.Content))
        {
            var reason = webContent.ErrorMessage ?? "empty content";
            return ScrapeResult.Empty($"{staticDiagnostic} Playwright fallback failed: {reason}.");
        }

        // Try text content parser (validated by CompositePricingParser)
        var textResult = _parser.ParseText(webContent.Content, config);
        if (textResult.Success)
        {
            return new ScrapeResult(
                textResult.Prices.ToList(),
                $"{staticDiagnostic} Playwright text fallback: {textResult.Diagnostic}");
        }

        return ScrapeResult.Empty(
            $"{staticDiagnostic} Playwright fetched {webContent.Content.Length:N0} chars but no prices extracted. "
            +
            $"{textResult.Diagnostic}");
    }
}
