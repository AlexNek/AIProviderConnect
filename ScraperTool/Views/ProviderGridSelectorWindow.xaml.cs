using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using ScraperTool.Models;

namespace ScraperTool.Views;

public sealed partial class ProviderGridSelectorWindow : Window
{
    public ProviderSelectionItem? SelectedItem { get; private set; }

    public ProviderGridSelectorWindow()
    {
        InitializeComponent();
    }

    private void CommitSelection()
    {
        SelectedItem = ProviderGrid.SelectedItem as ProviderSelectionItem;
        if (SelectedItem is not null)
            DialogResult = true;
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        CommitSelection();
    }

    private void OnSelect(object sender, RoutedEventArgs e)
    {
        CommitSelection();
    }

    private void OnWebsiteClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (sender is not TextBlock { Text: { Length: > 0 } url })
            return;

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
