using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ScraperTool.Controls;

/// <summary>
/// Paints every occurrence of a search fragment inside a <see cref="TextBlock"/> so a filtered grid
/// row can show which of its cells made it match, instead of the row appearing for a reason the
/// view never displays. The text is supplied through <see cref="SourceProperty"/> rather than
/// <see cref="TextBlock.Text"/>, because rebuilding the inlines and binding the text would fight
/// over the same content.
/// </summary>
public static class HighlightText
{
    // Soft yellow, readable on both the white and the alternating row background.
    private static readonly Brush MatchBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xA8));

    static HighlightText()
    {
        MatchBrush.Freeze();
    }

    /// <summary>The text to display, with matched fragments emphasised.</summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.RegisterAttached(
            "Source",
            typeof(string),
            typeof(HighlightText),
            new PropertyMetadata(string.Empty, OnPresentationChanged));

    /// <summary>The fragment to emphasise; blank or absent means the text is shown as-is.</summary>
    public static readonly DependencyProperty QueryProperty =
        DependencyProperty.RegisterAttached(
            "Query",
            typeof(string),
            typeof(HighlightText),
            new PropertyMetadata(string.Empty, OnPresentationChanged));

    /// <summary>Text shown before <see cref="SourceProperty"/> and never emphasised, e.g. modality icons.</summary>
    public static readonly DependencyProperty PrefixProperty =
        DependencyProperty.RegisterAttached(
            "Prefix",
            typeof(string),
            typeof(HighlightText),
            new PropertyMetadata(string.Empty, OnPresentationChanged));

    public static string? GetSource(TextBlock element) => (string?)element.GetValue(SourceProperty);

    public static void SetSource(TextBlock element, string? value) => element.SetValue(SourceProperty, value);

    public static string? GetQuery(TextBlock element) => (string?)element.GetValue(QueryProperty);

    public static void SetQuery(TextBlock element, string? value) => element.SetValue(QueryProperty, value);

    public static string? GetPrefix(TextBlock element) => (string?)element.GetValue(PrefixProperty);

    public static void SetPrefix(TextBlock element, string? value) => element.SetValue(PrefixProperty, value);

    private static void OnPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
            return;

        var source = GetSource(textBlock) ?? string.Empty;
        var prefix = GetPrefix(textBlock)?.Trim() ?? string.Empty;
        var query = GetQuery(textBlock)?.Trim() ?? string.Empty;

        textBlock.Inlines.Clear();

        if (prefix.Length > 0)
            textBlock.Inlines.Add(new Run(source.Length > 0 ? prefix + " " : prefix));

        if (source.Length == 0)
            return;

        if (query.Length == 0 || !source.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            textBlock.Inlines.Add(new Run(source));
            return;
        }

        var index = 0;
        while (index < source.Length)
        {
            var hit = source.IndexOf(query, index, StringComparison.OrdinalIgnoreCase);
            if (hit < 0)
            {
                textBlock.Inlines.Add(new Run(source[index..]));
                break;
            }

            if (hit > index)
                textBlock.Inlines.Add(new Run(source[index..hit]));

            textBlock.Inlines.Add(new Run(source.Substring(hit, query.Length)) { Background = MatchBrush });
            index = hit + query.Length;
        }
    }
}
