using System.Windows;
using System.Windows.Threading;

using ScraperTool.ViewModels;
using ScraperTool.Views;

using Serilog;

namespace ScraperTool.Services.Validation;

/// <summary>
/// WPF implementation of <see cref="IManualBrowserVerifier"/>. Shows a modeless
/// window (not ShowDialog) to avoid the nested modal message loop that
/// interferes with Playwright/CloakBrowser operations.
/// </summary>
public sealed class ManualBrowserVerifier : IManualBrowserVerifier
{
    /// <inheritdoc />
    public async Task<EManualVerificationResult> TryVerifyAsync(
        string url,
        string field,
        string fileName,
        CancellationToken ct)
    {
        if (Application.Current is null)
            return EManualVerificationResult.UserCancelled;

        var tcs = new TaskCompletionSource<EManualVerificationResult>();
        ManualVerificationWindow? dialog = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var viewModel = new ManualVerificationViewModel(url, field, fileName);
            dialog = new ManualVerificationWindow(viewModel);

            // The ViewModel's finally block disposes the browser and then fires
            // RequestClose. The Closed handler just captures the result.
            dialog.Closed += (_, _) =>
            {
                Log.Debug("[ManualVerify] Closed: result={Result}", viewModel.Result);
                tcs.TrySetResult(viewModel.Result);
            };

            ct.Register(() =>
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    dialog.Close();
                });
            });

            Log.Debug("[ManualVerify] Showing window (modeless)");
            dialog.Show();
        }, DispatcherPriority.Normal);

        var result = await tcs.Task.WaitAsync(ct);
        Log.Debug("[ManualVerify] TryVerifyAsync returning {Result}", result);
        return result;
    }
}
