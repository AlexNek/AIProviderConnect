using ScraperTool.Models;

using WebTools.NET.Abstractions;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Default <see cref="IPageContentProbe"/>: fetches pages via
/// <see cref="IWebContentFetcher"/> and analyses them with <see cref="IContentAnalyzer"/>.
/// </summary>
public sealed class PageContentProbe : IPageContentProbe
{
    private readonly IWebContentFetcher _fetcher;
    private readonly IContentAnalyzer _contentAnalyzer;

    public PageContentProbe(IWebContentFetcher fetcher, IContentAnalyzer contentAnalyzer)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _contentAnalyzer = contentAnalyzer ?? throw new ArgumentNullException(nameof(contentAnalyzer));
    }

    /// <inheritdoc />
    public async Task<PageContentReadResult> ReadBodyForErrorPageAsync(
        string url,
        string fileName,
        string field,
        IValidationIssueSink sink,
        CancellationToken ct = default)
    {
        var result = await _fetcher.FetchAsync(url, ct: ct);

        if (!result.Success)
            return new PageContentReadResult(null);

        sink.Progress(fileName, field, url, ValidationStage.CheckingContent);

        var hasNotFound = await _contentAnalyzer.HasNotFoundContentAsync(result.Content);

        if (hasNotFound)
        {
            sink.Append(
                fileName,
                field,
                url,
                "UrlNotFound",
                $"Field '{field}' returned HTTP 200 but page shows error/not-found content: {url}");
        }

        // When the field is loginUrl, settle the login-surface question in the same call
        // so the caller never has to analyse the body a second time.
        if (field == ProviderJsonFields.LoginUrl)
        {
            var (verdict, reason) = await _contentAnalyzer.AnalyzeLoginUrlAsync(
                result.Content, result.FinalUrl ?? url);
            return new PageContentReadResult(result.Content, verdict, reason);
        }

        return new PageContentReadResult(result.Content);
    }

    /// <inheritdoc />
    public async Task<(bool Success, bool HasLoginContent)> CheckLoginContentAtUrlAsync(
        string url,
        CancellationToken ct = default)
    {
        var loginResult = await _fetcher.FetchAsync(url, ct: ct);
        if (!loginResult.Success)
            return (false, false);

        var hasLogin = await _contentAnalyzer.HasLoginContentAsync(loginResult.Content);
        return (true, hasLogin);
    }

    /// <inheritdoc />
    public async Task<bool> TrySettleLoginUrlEqualityAsync(
        string loginUrl,
        string fileName,
        IValidationIssueSink sink,
        CancellationToken ct)
    {
        var (loginFetchSuccess, hasLogin) = await CheckLoginContentAtUrlAsync(loginUrl, ct);
        if (!loginFetchSuccess) return false;

        if (!hasLogin)
        {
            sink.FailWith(fileName, ProviderJsonFields.LoginUrl, loginUrl,
                ValidationIssueCodes.LoginUrlSameAsWebsite,
                $"Field 'loginUrl' is identical to 'website' and page has no login/sign-in elements: {loginUrl}",
                "loginUrl equals website — no login/sign-in elements found on page");
            return true;
        }

        // loginUrl equals website and has login elements: emit the pass for loginUrl,
        // but continue to the "URL is reachable" pass for the website field.
        sink.Pass(fileName, ProviderJsonFields.LoginUrl, loginUrl,
            "loginUrl equals website — page has login elements");
        return false;
    }
}
