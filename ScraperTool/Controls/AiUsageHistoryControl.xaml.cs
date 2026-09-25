using System.Text;
using System.Windows;
using System.Windows.Controls;

using ScraperTool.Data.Entities;

namespace ScraperTool.Controls;

public sealed partial class AiUsageHistoryControl : UserControl
{
    public AiUsageHistoryControl()
    {
        InitializeComponent();
    }

    private void CopyRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Parent is not ContextMenu ctx)
            return;

        var entry = (ctx.PlacementTarget as FrameworkElement)?.DataContext as TokenUsageEntry;
        if (entry is null)
            return;

        var text = $"Date: {entry.CreatedAt:g}\n" +
                   $"Provider: {entry.ProviderName}\n" +
                   $"Model: {entry.ModelName}\n" +
                   $"Operation: {entry.Operation}\n" +
                   $"Prompt: {entry.PromptTokens:N0}\n" +
                   $"Completion: {entry.CompletionTokens:N0}\n" +
                   $"Total: {entry.TotalTokens:N0}\n" +
                   $"Cost: {entry.Cost:C4}\n" +
                   $"Source: {entry.CostSource}";
        Clipboard.SetText(text);
    }

    private void CopyTable_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.AiUsageHistoryViewModel vm)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("Date\tProvider\tModel\tOperation\tPrompt\tCompletion\tTotal\tCost\tSource");
        foreach (var entry in vm.Entries)
        {
            sb.Append(entry.CreatedAt.ToString("g"));
            sb.Append('\t');
            sb.Append(entry.ProviderName);
            sb.Append('\t');
            sb.Append(entry.ModelName);
            sb.Append('\t');
            sb.Append(entry.Operation);
            sb.Append('\t');
            sb.Append(entry.PromptTokens.ToString("N0"));
            sb.Append('\t');
            sb.Append(entry.CompletionTokens.ToString("N0"));
            sb.Append('\t');
            sb.Append(entry.TotalTokens.ToString("N0"));
            sb.Append('\t');
            sb.Append(entry.Cost.ToString("C4"));
            sb.Append('\t');
            sb.Append(entry.CostSource);
            sb.AppendLine();
        }

        Clipboard.SetText(sb.ToString());
    }
}
