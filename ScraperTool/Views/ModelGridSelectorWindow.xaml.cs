using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using AIProviderConnect.Models;

using ScraperTool.Models;

namespace ScraperTool.Views;

public sealed partial class ModelGridSelectorWindow : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _filterText = string.Empty;

    private string? _initialSelectionId;

    private string _sourceLabel = string.Empty;

    private string? _modalityFilterToken;

    private EModelCapability? _capabilityFilterFlag;

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
        string? initialSelectionId = null,
        string? sourceLabel = null)
    {
        AllModels.Clear();
        FilteredModels.Clear();
        foreach (var m in models)
        {
            AllModels.Add(m);
            FilteredModels.Add(m);
        }

        _initialSelectionId = initialSelectionId;
        _sourceLabel = sourceLabel ?? string.Empty;
        UpdateHeader();
    }

    // Names the provider and states the row count, so a short list is recognisable as a short list
    // instead of being mistaken for the provider's whole catalog.
    private void UpdateHeader()
    {
        if (HeaderLabel is null)
            return;

        var source = _sourceLabel.Length > 0 ? $" — {_sourceLabel}" : string.Empty;
        var total = AllModels.Count;
        HeaderLabel.Text = FilteredModels.Count == total
            ? $"Select a model{source} · {total} models"
            : $"Select a model{source} · {FilteredModels.Count} of {total} models";
    }

    private void ApplyDefaultPriceSort()
    {
        // Sort by prompt price first, then completion price, then model name — the name, not the full
        // id, so equal-priced models from one owner are not interleaved by their owner prefix.
        var sorted = FilteredModels
            .OrderBy(m => ParsePrice(m.PromptPrice))
            .ThenBy(m => ParsePrice(m.CompletionPrice))
            .ThenBy(m => m.Name)
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
            // Every field probed here is on screen: name in the Name column, owner in the Owner column,
            // modality words in the Modalities tooltip, and the Description column. The id is searched
            // too, and it is the two visible parts joined, so a match is always explainable.
            var matchesText = q.Length == 0 ||
                              m.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                              (m.Description?.Contains(q, StringComparison.OrdinalIgnoreCase)
                               ?? false) ||
                              (m.OwnedBy?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                              ||
                              m.ModalityWords.Contains(q, StringComparison.OrdinalIgnoreCase);

            var matchesModality = string.IsNullOrWhiteSpace(_modalityFilterToken) ||
                                  (m.Modalities?.Contains(
                                       _modalityFilterToken,
                                       StringComparison.OrdinalIgnoreCase) ?? false);

            var matchesCapability = !_capabilityFilterFlag.HasValue ||
                                    m.Capabilities.HasFlag(_capabilityFilterFlag.Value);

            if (matchesText && matchesModality && matchesCapability)
                FilteredModels.Add(m);
        }

        UpdateHeader();
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
                "Name" => row.Id,
                "Owner" => row.OwnedBy ?? string.Empty,
                "Modalities" => row.Modalities,
                "Capabilities" => row.CapabilitiesText,
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
            $"{row.Id}\t{row.OwnedBy}\t{row.Modalities}\t{row.ContextWindow}\t{row.PromptPrice}\t{row.CompletionPrice}\t{row.Description}";
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

    private void OnCapabilitySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CapabilityFilter.SelectedItem is ComboBoxItem item)
        {
            var content = item.Content?.ToString();
            _capabilityFilterFlag = content switch
                {
                    "All" => null,
                    "Text Generation" => EModelCapability.TextGeneration,
                    "Structured Output" => EModelCapability.StructuredOutput,
                    "Tool Calling" => EModelCapability.ToolCalling,
                    "Embedding" => EModelCapability.Embedding,
                    "Reranker" => EModelCapability.Reranker,
                    "Image Recognition" => EModelCapability.ImageRecognition,
                    "Image Generation" => EModelCapability.ImageGeneration,
                    "Audio Recognition" => EModelCapability.AudioRecognition,
                    "Text to Speech" => EModelCapability.TextToSpeech,
                    "Audio Generation" => EModelCapability.AudioGeneration,
                    "Video Transcription" => EModelCapability.VideoTranscription,
                    "Video Recognition" => EModelCapability.VideoRecognition,
                    "Video Generation" => EModelCapability.VideoGeneration,
                    "Decision" => EModelCapability.Decision,
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
        UpdateCapabilityFilterAvailability();
        ApplyInitialSelection();
    }

    // Capability flags are provider-reported data. Model discovery leaves them unset today, so the
    // filter would match a column nobody can see and hide every row without an on-screen reason.
    private void UpdateCapabilityFilterAvailability()
    {
        var reported = AllModels.Count(m => m.Capabilities != EModelCapability.None);
        CapabilityFilter.IsEnabled = reported > 0;

        if (AllModels.Count > 0 && reported < AllModels.Count)
        {
            CapabilityHint.Text = reported == 0
                ? "These models report no capabilities, so capability filtering is unavailable."
                : "Only part of the list reports capabilities — rows without them are excluded.";
            CapabilityHint.Visibility = Visibility.Visible;
            return;
        }

        CapabilityHint.Visibility = Visibility.Collapsed;
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
