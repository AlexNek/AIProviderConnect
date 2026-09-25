using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;

using ScraperTool.Models;
using ScraperTool.ViewModels;

namespace ScraperTool.Controls;

public sealed partial class ScrapePanelControl : UserControl
{
    public ScrapePanelControl()
    {
        InitializeComponent();
    }

    private void OnCopyAllAsTsv(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.Items.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine("Provider\tModel\tPrompt price\tCompletion price\tUnit\tSource");

        foreach (var item in ResultsGrid.Items)
        {
            if (item is ProviderPriceResult row)
            {
                sb.AppendLine(
                    $"{row.ProviderId}\t{row.ModelDisplayName}\t{row.PromptPrice:N8}\t{row.CompletionPrice:N8}\t{row.PriceUnit}\t{row.Source}");
            }
        }

        Clipboard.SetText(sb.ToString());
    }

    private void OnCopyCellValue(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.CurrentCell.Item is not ProviderPriceResult row) return;

        var col = ResultsGrid.CurrentCell.Column;
        var value = col.Header?.ToString() switch
            {
                "Provider" => row.ProviderId,
                "Model" => row.ModelDisplayName,
                "Prompt price, $" => row.PromptPrice?.ToString("N8") ?? string.Empty,
                "Completion price, $" => row.CompletionPrice?.ToString("N8") ?? string.Empty,
                "Unit" => row.PriceUnit.ToString(),
                "Source" => row.Source ?? string.Empty,
                _ => string.Empty
            };

        if (!string.IsNullOrEmpty(value))
            Clipboard.SetText(value);
    }

    private void OnCopySelectedRows(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItems.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine("Provider\tModel\tPrompt price\tCompletion price\tUnit\tSource");

        foreach (var item in ResultsGrid.SelectedItems)
        {
            if (item is ProviderPriceResult row)
            {
                sb.AppendLine(
                    $"{row.ProviderId}\t{row.ModelDisplayName}\t{row.PromptPrice:N8}\t{row.CompletionPrice:N8}\t{row.PriceUnit}\t{row.Source}");
            }
        }

        Clipboard.SetText(sb.ToString());
    }

    private async void OnDeleteRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ProviderPriceResult item }) return;
        if (DataContext is not ScrapePanelViewModel vm) return;

        var result = MessageBox.Show(
            $"Delete \"{item.ModelDisplayName}\" from provider \"{item.ProviderId}\" from the database?",
            "Confirm Deletion",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        await vm.DeleteSelectedCommand.ExecuteAsync(item);
    }

    private void OnSourceLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ProviderPriceResult item }) return;

        // Source might be a URL directly, or "DB (date)" — in that case use ApiPricingUrl
        var url = item.Source?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true
                      ? item.Source
                      : item.ApiPricingUrl;

        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Silently ignore if browser can't be opened
        }
    }
}
