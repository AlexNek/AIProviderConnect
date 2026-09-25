using System.Windows;

using CloakBrowser;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Playwright;

using ScraperTool.Services.Validation;

using Serilog;

namespace ScraperTool.ViewModels;

/// <summary>
/// ViewModel for the manual browser verification dialog. Owns the browser
/// lifecycle (launch, auto-poll, dispose) and exposes bindable state for the
/// View to render.
/// </summary>
public sealed partial class ManualVerificationViewModel : ObservableObject, IDisposable
{
    private const int ChallengeWaitMs = 30_000;
    private const int PollIntervalMs = 2_000;

    private readonly string _url;

    private CancellationTokenSource? _cts;
    private CloakBrowserHandle? _handle;
    private IBrowserContext? _context;
    private int _autoVerified;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public string Field { get; }

    public string Url => _url;

    public string WindowTitle { get; }

    public EManualVerificationResult Result { get; private set; } = EManualVerificationResult.UserCancelled;

    /// <summary>
    /// Raised when the ViewModel requests the dialog close.
    /// </summary>
    public event EventHandler? RequestClose;

    public ManualVerificationViewModel(string url, string field, string fileName)
    {
        _url = url;
        Field = field;
        WindowTitle = $"Manual Verification — {fileName}";
    }

    [RelayCommand(CanExecute = nameof(CanExecuteOpenBrowser))]
    private async Task OpenBrowserAsync()
    {
        StatusText = "Launching browser...";
        _cts = new CancellationTokenSource();

        // Do NOT pass the cancellation token to Task.Run — the finally block
        // must ALWAYS run to dispose the browser and fire RequestClose.
        await Task.Run(async () =>
        {
            CloakBrowserHandle? localHandle = null;

            try
            {
                localHandle = await CloakLauncher.LaunchAsync(
                    new LaunchOptions { Headless = false }).ConfigureAwait(false);
                _handle = localHandle;

                _context = await localHandle.RawBrowser.NewContextAsync().ConfigureAwait(false);
                var page = await _context.NewPageAsync().ConfigureAwait(false);

                await page.GotoAsync(_url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60_000
                }).ConfigureAwait(false);

                UpdateStatus("Browser opened. Solve the Cloudflare challenge — verification is automatic.");

                await AutoPollChallengeAsync(page, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("[ManualVerify] Browser task cancelled");
            }
            catch (Exception ex)
            {
                Log.Debug("[ManualVerify] Browser task failed: {Error}", ex.Message);
                UpdateStatus($"Browser error: {ex.Message}");
            }
            finally
            {
                Log.Debug("[ManualVerify] Finally: disposing browser");

                if (localHandle is not null)
                {
                    try
                    {
                        await localHandle.DisposeAsync().AsTask()
                            .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                        Log.Debug("[ManualVerify] Handle disposed OK");
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("[ManualVerify] Handle dispose FAILED: {Error}", ex.Message);
                    }
                }

                _handle = null;
                _context = null;

                Log.Debug("[ManualVerify] Finally: firing RequestClose");
                RequestClose?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private bool CanExecuteOpenBrowser() => _handle is null;

    [RelayCommand]
    private void Cancel()
    {
        Log.Debug("[ManualVerify] Cancel called");
        _cts?.Cancel();
        if (Volatile.Read(ref _autoVerified) == 0)
            Result = EManualVerificationResult.UserCancelled;
    }

    private async Task AutoPollChallengeAsync(IPage page, CancellationToken ct)
    {
        Log.Debug("[ManualVerify] AutoPoll START");
        var deadline = Environment.TickCount64 + ChallengeWaitMs;

        while (Environment.TickCount64 < deadline)
        {
            await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false);

            if (Volatile.Read(ref _autoVerified) == 1 || _handle is null)
                return;

            try
            {
                if (_handle is not null && !_handle.RawBrowser.IsConnected)
                {
                    Log.Debug("[ManualVerify] AutoPoll: browser disconnected (user closed it)");
                    if (Interlocked.Exchange(ref _autoVerified, 1) == 0)
                        Result = EManualVerificationResult.UserCancelled;
                    return;
                }

                var challenged = await IsBotChallengePageAsync(page).ConfigureAwait(false);
                if (!challenged)
                {
                    if (Interlocked.Exchange(ref _autoVerified, 1) == 1)
                        return;

                    Log.Debug("[ManualVerify] AutoPoll: challenge CLEARED");
                    Result = EManualVerificationResult.Verified;
                    UpdateStatus("Challenge cleared — URL verified.");
                    return;
                }
            }
            catch (PlaywrightException ex)
            {
                Log.Debug("[ManualVerify] AutoPoll: PlaywrightException: {Error}", ex.Message);
                if (Interlocked.Exchange(ref _autoVerified, 1) == 0)
                    Result = EManualVerificationResult.UserCancelled;
                return;
            }
        }

        if (Volatile.Read(ref _autoVerified) == 0)
        {
            Log.Debug("[ManualVerify] AutoPoll: TIMEOUT");
            UpdateStatus("Challenge did not clear in 30s. Click 'Cancel' to close.");
        }
    }

    private void UpdateStatus(string text)
    {
        Application.Current?.Dispatcher.BeginInvoke(() => StatusText = text);
    }

    private static async Task<bool> IsBotChallengePageAsync(IPage page)
    {
        try
        {
            var title = await page.TitleAsync().ConfigureAwait(false);
            var isChallenge = title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
                              title.Contains("Checking your browser", StringComparison.OrdinalIgnoreCase) ||
                              title.Contains("Attention Required", StringComparison.OrdinalIgnoreCase);

            // Check for Cloudflare's challenge form — this element ONLY exists
            // on the challenge page itself, never on real pages after bypass.
            if (!isChallenge)
            {
                var challengeForm = await page.QuerySelectorAsync("form#challenge-form").ConfigureAwait(false);
                isChallenge = challengeForm is not null;
            }

            Log.Debug("[ManualVerify] IsBotChallengePage: title='{Title}', isChallenge={IsChallenge}",
                title, isChallenge);
            return isChallenge;
        }
        catch (PlaywrightException ex)
        {
            Log.Debug("[ManualVerify] IsBotChallengePage: PlaywrightException: {Error}", ex.Message);
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
