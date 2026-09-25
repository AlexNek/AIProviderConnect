using System.Windows;
using System.Windows.Threading;

using ScraperTool.ViewModels;

using Serilog;

namespace ScraperTool.Views;

/// <summary>
/// Thin WPF shell for the manual browser verification window. All logic lives
/// in <see cref="ManualVerificationViewModel"/>; the View only wires DataContext
/// and subscribes to <see cref="ManualVerificationViewModel.RequestClose"/>.
/// </summary>
public sealed partial class ManualVerificationWindow : Window
{
    public ManualVerificationWindow(ManualVerificationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += OnViewModelRequestClose;
        Closed += (_, _) => viewModel.RequestClose -= OnViewModelRequestClose;
    }

    private void OnViewModelRequestClose(object? sender, EventArgs e)
    {
        // RequestClose may fire from a background thread (auto-poll).
        // Window.Close must run on the UI thread.
        Dispatcher.BeginInvoke(() =>
        {
            Log.Debug("[ManualVerify] Closing window");
            Close();
        });
    }
}
