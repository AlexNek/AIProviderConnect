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

    private string _nameFilterText = string.Empty;

    private string _descriptionFilterText = string.Empty;

    private string? _initialSelectionId;

    private string _sourceLabel = string.Empty;

    private string? _modalityFilterToken;

    private EModelCapability? _capabilityFilterFlag;

    public ObservableCollection<ModelSelectionItem> AllModels { get; } = [];

    public ObservableCollection<ModelSelectionItem> FilteredModels { get; } = [];

    /// <summary>
    /// The "Filter by name or owner" box. It is allowed to hold a pasted model id (`openai/gpt-4o`),
    /// which is why the searched text is split into <see cref="NameQuery"/> and
    /// <see cref="OwnerQuery"/> rather than matched against the joined id.
    /// </summary>
    public string NameFilterText
    {
        get => _nameFilterText;
        set
        {
            _nameFilterText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NameQuery));
            OnPropertyChanged(nameof(OwnerQuery));
            ApplyFilter();
        }
    }

    /// <summary>The "Filter by description" box; feeds the Description column's highlight.</summary>
    public string DescriptionFilterText
    {
        get => _descriptionFilterText;
        set
        {
            _descriptionFilterText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DescriptionQuery));
            ApplyFilter();
        }
    }

    // Half of the name/owner box that belongs to the Name column. Empty when the box is empty or the
    // query was a bare "owner/", so the column is then neither filtered nor painted.
    public string NameQuery
    {
        get
        {
            var q = TrimmedNameFilter;
            var slash = q.LastIndexOf('/');
            return slash < 0 ? q : q[(slash + 1)..];
        }
    }

    // Half of the name/owner box that belongs to the Owner column.
    public string OwnerQuery
    {
        get
        {
            var q = TrimmedNameFilter;
            var slash = q.LastIndexOf('/');
            return slash < 0 ? q : q[..slash];
        }
    }

    public string DescriptionQuery => _descriptionFilterText.Trim();

    private string TrimmedNameFilter => _nameFilterText.Trim();

    public ModelSelectionItem? SelectedItem { get; private set; }

    public ModelGridSelectorWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public void LoadModels(
        IEnumerable<ModelSelectionItem> models,
        string? initialSelectionId = null,
        string? sourceLabel = null,
        EModelCapability? requiredCapability = null)
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

        // Set the capability selector to the caller's default; the filter is not enforced
        // until Feature 17 populates AIModel.Capabilities (see UpdateCapabilityFilterAvailability).
        if (requiredCapability.HasValue)
        {
            CapabilityFilter.SelectedIndex = requiredCapability.Value switch
            {
                EModelCapability.TextGeneration => 1,
                EModelCapability.Embedding => 2,
                EModelCapability.Decision => 3,
                _ => 0
            };
        }

        UpdateModelCount();
    }

    // States how many rows the grid is showing and which provider they came from, directly above the
    // list, so a filtered or partial list is visible instead of something the user has to trust.
    private void UpdateModelCount()
    {
        if (ModelCountLabel is null)
            return;

        var total = AllModels.Count;
        var shown = FilteredModels.Count;
        var counts = shown == total ? $"{total} models" : $"{shown} of {total} models shown";
        ModelCountLabel.Text = _sourceLabel.Length > 0 ? $"{_sourceLabel} — {counts}" : counts;
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
        var nameQuery = NameQuery;
        var ownerQuery = OwnerQuery;
        var descriptionQuery = DescriptionQuery;

        // A query holding a slash is a whole model id, so both halves have to match; otherwise the box
        // is "name or owner" and either column is enough.
        var isPastedId = TrimmedNameFilter.Contains('/');

        FilteredModels.Clear();
        foreach (var m in AllModels)
        {
            // Each box reads only the columns it paints: the name/owner box sees the Name and Owner
            // cells, the description box sees the Description cell, and modality and capability have
            // their own dropdowns. No filter can therefore match text that is absent from the row.
            var matchesName = nameQuery.Length == 0 ||
                              m.Name.Contains(nameQuery, StringComparison.OrdinalIgnoreCase);

            var matchesOwner = ownerQuery.Length == 0 ||
                               (m.OwnedBy?.Contains(ownerQuery, StringComparison.OrdinalIgnoreCase)
                                ?? false);

            var matchesIdentity = isPastedId
                ? matchesName && matchesOwner
                : matchesName || matchesOwner;

            var matchesDescription = descriptionQuery.Length == 0 ||
                                     (m.Description?.Contains(
                                          descriptionQuery, StringComparison.OrdinalIgnoreCase)
                                      ?? false);

            var matchesModality = string.IsNullOrWhiteSpace(_modalityFilterToken) ||
                                  (m.Modalities?.Contains(
                                       _modalityFilterToken,
                                       StringComparison.OrdinalIgnoreCase) ?? false);

            // A capability filter acts only on data the grid displays — if no model reports
            // capabilities, the filter is disabled and every row passes.
            var hasAnyCapabilities = AllModels.Any(x => x.Capabilities is not null);
            var matchesCapability = !_capabilityFilterFlag.HasValue
                                    || !hasAnyCapabilities
                                    || (m.Capabilities.HasValue
                                        && m.Capabilities.Value.HasFlag(_capabilityFilterFlag.Value));

            if (matchesIdentity && matchesDescription && matchesModality && matchesCapability)
                FilteredModels.Add(m);
        }

        UpdateModelCount();
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
                    "Chat" => EModelCapability.TextGeneration,
                    "Embedding" => EModelCapability.Embedding,
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

    // Capability flags are provider-reported data. When no model reports capabilities, the
    // filter would match a column nobody can see and hide every row without an on-screen reason.
    private void UpdateCapabilityFilterAvailability()
    {
        var reported = AllModels.Count(m => m.Capabilities is not null);

        if (reported == 0)
        {
            // No model in the loaded set reports capabilities — reset the selector to "All"
            // so the user sees the full list and the dropdown is ready when capabilities arrive.
            CapabilityFilter.SelectedIndex = 0;
            _capabilityFilterFlag = null;
            CapabilityFilter.IsEnabled = false;
            CapabilityHint.Text = "These models report no capabilities, so capability filtering is unavailable.";
            CapabilityHint.Visibility = Visibility.Visible;
            return;
        }

        CapabilityFilter.IsEnabled = true;

        if (reported < AllModels.Count)
        {
            CapabilityFilter.SelectedIndex = 0;
            _capabilityFilterFlag = null;
            CapabilityHint.Text = "Only part of the list reports capabilities — unreported models remain selectable.";
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
