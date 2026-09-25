using Microsoft.Extensions.Logging;

using ScraperTool.Models;

using WebTools.NET.Abstractions;
using WebTools.NET.Browsing;
using WebTools.NET.Models;
using WebTools.NET.Search;

namespace ScraperTool.Services;

public sealed class BrowserEngineSwitch : IWebContentFetcher,
                                          IWebSearchProvider,
                                          IBrowserInteraction
{
    private const bool CloakBrowserHeadless = true;

    private const bool PlaywrightHeadless = false;

    private readonly object _lock = new();

    private readonly ILoggerFactory _loggerFactory;

    private readonly AppSettings _settings;

    private CloakBrowserContentFetcher? _cbFetcher;

    private CloakBrowserSearchProvider? _cbSearch;

    private CloakBrowserSession? _cbSession;

    private EBrowserEngine _lastEngine;

    private PlaywrightContentFetcher? _pwFetcher;

    private PlaywrightSearchProvider? _pwSearch;

    private PlaywrightSession? _pwSession;

    public BrowserEngineSwitch(AppSettings settings, ILoggerFactory loggerFactory)
    {
        _settings = settings;
        _loggerFactory = loggerFactory;
        _lastEngine = ResolveEngine();
    }

    public Task<UrlCheckResult> CheckReachabilityAsync(string url, CancellationToken ct = default)
    {
        var fetcher = GetFetcher();
        return fetcher.CheckReachabilityAsync(url, ct);
    }

    public Task ClickAsync(string selector, CancellationToken ct = default)
    {
        var session = GetSession();
        return session.ClickAsync(selector, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_pwFetcher is not null)
            await _pwFetcher.DisposeAsync().ConfigureAwait(false);
        if (_cbFetcher is not null)
            await _cbFetcher.DisposeAsync().ConfigureAwait(false);
        if (_pwSearch is not null)
            await _pwSearch.DisposeAsync().ConfigureAwait(false);
        if (_cbSearch is not null)
            await _cbSearch.DisposeAsync().ConfigureAwait(false);
        if (_pwSession is not null)
            await _pwSession.DisposeAsync().ConfigureAwait(false);
        if (_cbSession is not null)
            await _cbSession.DisposeAsync().ConfigureAwait(false);
        _pwFetcher = null;
        _cbFetcher = null;
        _pwSearch = null;
        _cbSearch = null;
        _pwSession = null;
        _cbSession = null;
    }

    public Task<WebContent> FetchAsync(string url, int? maxContentLength = null, CancellationToken ct = default)
    {
        var fetcher = GetFetcher();
        return fetcher.FetchAsync(url, maxContentLength, ct);
    }

    public Task<WebContent> FetchAsAsync(
        string url,
        EContentFormat format,
        int? maxContentLength = null,
        ESanitizeLevel sanitizeLevel = ESanitizeLevel.Strict,
        CancellationToken ct = default)
    {
        var fetcher = GetFetcher();
        return fetcher.FetchAsAsync(url, format, maxContentLength, sanitizeLevel, ct);
    }

    public Task FillAsync(string selector, string value, CancellationToken ct = default)
    {
        var session = GetSession();
        return session.FillAsync(selector, value, ct);
    }

    public Task<string> GetContentAsync(CancellationToken ct = default)
    {
        var session = GetSession();
        return session.GetContentAsync(ct);
    }

    public Task<string> GetCurrentUrlAsync(CancellationToken ct = default)
    {
        var session = GetSession();
        return session.GetCurrentUrlAsync(ct);
    }

    public Task<string> GetHtmlAsync(CancellationToken ct = default)
    {
        var session = GetSession();
        return session.GetHtmlAsync(ct);
    }

    public Task NavigateAsync(string url, CancellationToken ct = default)
    {
        var session = GetSession();
        return session.NavigateAsync(url, ct);
    }

    public Task<SearchResult> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken ct = default)
    {
        var search = GetSearchProvider();
        return search.SearchAsync(query, maxResults, ct);
    }

    private CloakBrowserContentFetcher CreateCloakFetcher() => new(headless: CloakBrowserHeadless);

    private CloakBrowserSearchProvider CreateCloakSearch() =>
        new(
            _loggerFactory.CreateLogger<CloakBrowserSearchProvider>(),
            headless: CloakBrowserHeadless);

    private PlaywrightContentFetcher CreatePlaywrightFetcher() => new(headless: PlaywrightHeadless);

    private PlaywrightSearchProvider CreatePlaywrightSearch() =>
        new(_loggerFactory.CreateLogger<PlaywrightSearchProvider>(), headless: PlaywrightHeadless);

    private IWebContentFetcher GetFetcher()
    {
        var engine = SyncEngine();
        return engine == EBrowserEngine.CloakBrowser
                   ? _cbFetcher ??= CreateCloakFetcher()
                   : _pwFetcher ??= CreatePlaywrightFetcher();
    }

    private IWebSearchProvider GetSearchProvider()
    {
        var engine = SyncEngine();
        return engine == EBrowserEngine.CloakBrowser
                   ? _cbSearch ??= CreateCloakSearch()
                   : _pwSearch ??= CreatePlaywrightSearch();
    }

    private IBrowserInteraction GetSession()
    {
        var engine = SyncEngine();
        return engine == EBrowserEngine.CloakBrowser
                   ? _cbSession ??= new CloakBrowserSession()
                   : _pwSession ??= new PlaywrightSession();
    }

    private static Task NullAndDisposeOld(
        ref PlaywrightContentFetcher? fetcher,
        ref PlaywrightSearchProvider? search,
        ref PlaywrightSession? session)
    {
        return ExchangeAndDisposeAll(
            ref fetcher, ref search, ref session);
    }

    private static Task NullAndDisposeOld(
        ref CloakBrowserContentFetcher? fetcher,
        ref CloakBrowserSearchProvider? search,
        ref CloakBrowserSession? session)
    {
        return ExchangeAndDisposeAll(
            ref fetcher, ref search, ref session);
    }

    private static Task ExchangeAndDisposeAll<T1, T2, T3>(
        ref T1? field1, ref T2? field2, ref T3? field3)
        where T1 : class, IAsyncDisposable
        where T2 : class, IAsyncDisposable
        where T3 : class, IAsyncDisposable
    {
        var tasks = new List<Task>(3);
        var f = Interlocked.Exchange(ref field1, null);
        if (f is not null)
            tasks.Add(f.DisposeAsync().AsTask());
        var s = Interlocked.Exchange(ref field2, null);
        if (s is not null)
            tasks.Add(s.DisposeAsync().AsTask());
        var sess = Interlocked.Exchange(ref field3, null);
        if (sess is not null)
            tasks.Add(sess.DisposeAsync().AsTask());
        return tasks.Count > 0 ? Task.WhenAll(tasks) : Task.CompletedTask;
    }

    private EBrowserEngine ResolveEngine() => _settings.BrowserEngine;

    private EBrowserEngine SyncEngine()
    {
        var current = ResolveEngine();
        if (current == _lastEngine)
            return current;

        Task disposeTask = Task.CompletedTask;
        lock (_lock)
        {
            if (current == _lastEngine)
                return current;

            if (current == EBrowserEngine.CloakBrowser)
            {
                disposeTask = NullAndDisposeOld(ref _pwFetcher, ref _pwSearch, ref _pwSession);
            }
            else
            {
                disposeTask = NullAndDisposeOld(ref _cbFetcher, ref _cbSearch, ref _cbSession);
            }

            _lastEngine = current;
        }

        if (!disposeTask.IsCompleted)
            _ = disposeTask.ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);

        return current;
    }

    Task<bool> IBrowserContent.CheckReachabilityAsync(string url, CancellationToken ct)
    {
        var session = GetSession();
        return session.CheckReachabilityAsync(url, ct);
    }

    public Task<string> GetTitleAsync(CancellationToken ct = default)
    {
        var session = GetSession();
        return session.GetTitleAsync(ct);
    }
}
