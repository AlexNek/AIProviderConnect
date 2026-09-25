using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using ScraperTool.Models;

namespace ScraperTool.Views;

public sealed partial class ModelGridSelectorWindow : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _filterText = string.Empty;

    private string? _initialSelectionId;

    private string? _modalityFilterToken;

    public ObservableCollection<ModelSelectionItem> AllModels { get; } = [];

    public ObservableCollection<ModelSelectionItem> FilteredModels { get; } = [];

    public string FilterText
    {
        get => _filterText;
        set
        {
            _filterText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public ModelSelectionItem? SelectedItem { get; private set; }

    public ModelGridSelectorWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public void LoadModels(
        IEnumerable<ModelSelectionItem> models,
        string? initialSelectionId = null)
    {
        AllModels.Clear();
        FilteredModels.Clear();
        foreach (var m in models)
        {
            AllModels.Add(m);
            FilteredModels.Add(m);
        }

        _initialSelectionId = initialSelectionId;
    }

    private void ApplyDefaultPriceSort()
    {
        // Sort by prompt price first, then completion price, then id.
        var sorted = FilteredModels
            .OrderBy(m => ParsePrice(m.PromptPrice))
            .ThenBy(m => ParsePrice(m.CompletionPrice))
            .ThenBy(m => m.Id)
            .ToList();

        FilteredModels.Clear();
        foreach (var m in sorted)
            FilteredModels.Add(m);
    }

    private void ApplyFilter()
    {
        var q = _filterText.Trim();
        FilteredModels.Clear();
        foreach (var m in AllModels)
        {
            var matchesText = q.Length == 0 ||
                              m.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                              (m.Description?.Contains(q, StringComparison.OrdinalIgnoreCase)
                               ?? false) ||
                              (m.OwnedBy?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                              ||
                              (m.Modalities?.Contains(q, StringComparison.OrdinalIgnoreCase)
                               ?? false);

            var matchesModality = string.IsNullOrWhiteSpace(_modalityFilterToken) ||
                                  (m.Modalities?.Contains(
                                       _modalityFilterToken,
                                       StringComparison.OrdinalIgnoreCase) ?? false);

            if (matchesText && matchesModality)
                FilteredModels.Add(m);
        }
    }

    private void ApplyInitialSelection()
    {
        if (string.IsNullOrEmpty(_initialSelectionId))
            return;

        var match = FilteredModels.FirstOrDefault(m =>
            string.Equals(m.Id, _initialSelectionId, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            ModelGrid.SelectedItem = match;
            ModelGrid.ScrollIntoView(match);
        }

        _initialSelectionId = null;
    }

    private void CommitSelection()
    {
        SelectedItem = ModelGrid.SelectedItem as ModelSelectionItem;
        if (SelectedItem is not null)
            DialogResult = true;
    }

    private void OnCopyCellValue(object sender, RoutedEventArgs e)
    {
        if (ModelGrid.CurrentCell.Item is not ModelSelectionItem row) return;

        var col = ModelGrid.CurrentCell.Column;
        var value = col.Header?.ToString() switch
            {
                "Model ID" => row.Id,
                "Modalities" => row.Modalities,
                "Context" => row.ContextWindow,
                "Prompt, $/1M" => row.PromptPrice,
                "Completion, $/1M" => row.CompletionPrice,
                "Description" => row.Description ?? string.Empty,
                _ => string.Empty
            };

        if (!string.IsNullOrEmpty(value))
            Clipboard.SetText(value);
    }

    private void OnCopySelectedRow(object sender, RoutedEventArgs e)
    {
        if (ModelGrid.SelectedItem is not ModelSelectionItem row) return;

        var text =
            $"{row.Id}\t{row.Modalities}\t{row.ContextWindow}\t{row.PromptPrice}\t{row.CompletionPrice}\t{row.Description}";
        Clipboard.SetText(text);
    }

    private void OnModalitySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModalityFilter.SelectedItem is ComboBoxItem item)
        {
            var content = item.Content?.ToString();
            _modalityFilterToken = content switch
                {
                    "All" => null,
                    "📝 text" => "📝",
                    "🖼 image" => "🖼",
                    "🔊 audio" => "🔊",
                    "🎬 video" => "🎬",
                    "📄 file" => "📄",
                    "💻 code" => "💻",
                    _ => null
                };
            ApplyFilter();
        }
    }

    private void OnModelGridLoaded(object sender, RoutedEventArgs e)
    {
        // Ensure stable default sort by prompt price
        ModelGrid?.Items?.SortDescriptions.Clear();

        // If ItemsSource is an ObservableCollection, WPF sorting needs a view.
        // We'll re-apply sorting by replacing the collection in code-behind.
        ApplyDefaultPriceSort();
        ApplyInitialSelection();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e) => CommitSelection();

    private void OnSelect(object sender, RoutedEventArgs e) => CommitSelection();

    private static decimal ParsePrice(string? price)
    {
        if (string.IsNullOrWhiteSpace(price))
            return 0;
        var cleaned = price.TrimStart('$', ' ', '\t');
        decimal.TryParse(cleaned, out var p);
        return p;
    }
}
