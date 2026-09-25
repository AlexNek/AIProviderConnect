using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScraperTool.Models;

/// <summary>
/// Observable key-value pair for editing protocol configuration entries in the UI.
/// Carries the list of known keys for the active protocol so the DataTemplate
/// can render a ComboBox (when keys are known) or a free-text TextBox (otherwise).
/// </summary>
public sealed partial class ProtocolConfigEntry : ObservableObject
{
    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> _knownKeys = [];

    [ObservableProperty]
    private bool _hasKnownKeys;

    partial void OnKnownKeysChanged(IReadOnlyList<string> value)
    {
        HasKnownKeys = value.Count > 0;
    }
}
