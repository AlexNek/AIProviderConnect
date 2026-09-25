using System.Collections;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using ScraperTool.Models;

namespace ScraperTool.Controls;

public sealed partial class SuggestionDataGridControl : UserControl
{
    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(
            nameof(AccentBrush),
            typeof(Brush),
            typeof(SuggestionDataGridControl),
            new PropertyMetadata(null));

    public Brush? AccentBrush
    {
        get => (Brush?)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public static readonly DependencyProperty AltRowBrushProperty =
        DependencyProperty.Register(
            nameof(AltRowBrush),
            typeof(Brush),
            typeof(SuggestionDataGridControl),
            new PropertyMetadata(null));

    public Brush? AltRowBrush
    {
        get => (Brush?)GetValue(AltRowBrushProperty);
        set => SetValue(AltRowBrushProperty, value);
    }

    public static readonly DependencyProperty ApplyCommandProperty =
        DependencyProperty.Register(
            nameof(ApplyCommand),
            typeof(ICommand),
            typeof(SuggestionDataGridControl),
            new PropertyMetadata(null));

    public ICommand? ApplyCommand
    {
        get => (ICommand?)GetValue(ApplyCommandProperty);
        set => SetValue(ApplyCommandProperty, value);
    }

    public static readonly DependencyProperty DeselectAllCommandProperty =
        DependencyProperty.Register(
            nameof(DeselectAllCommand),
            typeof(ICommand),
            typeof(SuggestionDataGridControl),
            new PropertyMetadata(null));

    public ICommand? DeselectAllCommand
    {
        get => (ICommand?)GetValue(DeselectAllCommandProperty);
        set => SetValue(DeselectAllCommandProperty, value);
    }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IList),
            typeof(SuggestionDataGridControl),
            new PropertyMetadata(null));

    public IList? ItemsSource
    {
        get => (IList?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectAllCommandProperty =
        DependencyProperty.Register(
            nameof(SelectAllCommand),
            typeof(ICommand),
            typeof(SuggestionDataGridControl),
            new PropertyMetadata(null));

    public ICommand? SelectAllCommand
    {
        get => (ICommand?)GetValue(SelectAllCommandProperty);
        set => SetValue(SelectAllCommandProperty, value);
    }

    public SuggestionDataGridControl()
    {
        InitializeComponent();
    }

    private static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        return s.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsSource is null)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Provider\tField\tCurrent value\tSuggested value\tReason");
        foreach (var item in ItemsSource)
        {
            if (item is not AiSuggestion s)
            {
                continue;
            }

            var current = Clean(s.CurrentValue);
            var suggested = Clean(s.SuggestedValue);
            sb.AppendLine($"{s.DisplayName}\t{s.Field}\t{current}\t{suggested}\t{s.Reason}");
        }

        Clipboard.SetText(sb.ToString());
    }

    private void CopyRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Parent is not ContextMenu ctx)
        {
            return;
        }

        var suggestion = (ctx.PlacementTarget as FrameworkElement)?.DataContext as AiSuggestion;
        if (suggestion is null)
        {
            return;
        }

        var text = $"Provider: {suggestion.DisplayName}{Environment.NewLine}" +
                   $"Field: {suggestion.Field}{Environment.NewLine}" +
                   $"Current value: {suggestion.CurrentValue}{Environment.NewLine}" +
                   $"Suggested value: {suggestion.SuggestedValue}{Environment.NewLine}" +
                   $"Reason: {suggestion.Reason}";
        Clipboard.SetText(text);
    }
}
