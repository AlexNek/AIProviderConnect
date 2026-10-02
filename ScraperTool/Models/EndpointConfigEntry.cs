using System;
using System.Collections.Generic;

using AIProviderConnect.Constants;
using AIProviderConnect.Models;

using CommunityToolkit.Mvvm.ComponentModel;

namespace ScraperTool.Models;

/// <summary>
/// Observable per-operation endpoint override row for editing the
/// <c>endpoints</c> block in the UI. Carries the option lists its combos bind
/// to: the operation vocabulary and the protocol aliases (with an empty
/// "inherit" choice first).
/// </summary>
public sealed partial class EndpointConfigEntry : ObservableObject
{
    /// <summary>
    /// The fixed operation vocabulary of the endpoints block, with an empty
    /// "not yet selected" choice first so new rows prompt the user to pick.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownOperationsStatic = BuildOperationChoices();

    /// <summary>
    /// Display string for the "inherit the definition's protocol" choice.
    /// </summary>
    public const string InheritProtocolDisplay = "\u2014";

    /// <summary>
    /// Protocol choices for the row: a visible "inherit" choice followed by the
    /// JSON aliases of every provider protocol.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownProtocolsStatic = BuildProtocolChoices();

    /// <summary>
    /// Instance accessor for WPF binding (static fields are not directly bindable).
    /// </summary>
    public IReadOnlyList<string> KnownOperations => KnownOperationsStatic;

    /// <summary>
    /// Instance accessor for WPF binding (static fields are not directly bindable).
    /// </summary>
    public IReadOnlyList<string> KnownProtocols => KnownProtocolsStatic;

    /// <summary>
    /// Operations whose wire format is fixed by the provider's root protocol class.
    /// A per-endpoint protocol override on these operations is meaningless because
    /// the provider class always serves them with its own protocol.
    /// </summary>
    private static readonly HashSet<string> NonOverridableOperations =
        new(StringComparer.OrdinalIgnoreCase)
        {
            EndpointOperations.Chat,
            EndpointOperations.Models,
            EndpointOperations.Messages,
            EndpointOperations.Embeddings,
        };

    /// <summary>
    /// Maps each known operation to its library default endpoint path so the editor
    /// can show the default as read-only reference next to the editable override.
    /// </summary>
    private static readonly Dictionary<string, string> OperationDefaults =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [EndpointOperations.Chat] = EndpointDefaults.ChatCompletions,
            [EndpointOperations.Models] = EndpointDefaults.Models,
            [EndpointOperations.Messages] = EndpointDefaults.Messages,
            [EndpointOperations.Embeddings] = EndpointDefaults.Embeddings,
            [EndpointOperations.Decisions] = EndpointDefaults.Decisions,
        };

    [ObservableProperty]
    private string _operation = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    /// <summary>
    /// Base-URL override; empty means the definition's common base URL.
    /// </summary>
    [ObservableProperty]
    private string _baseUrl = string.Empty;

    /// <summary>
    /// Protocol override; <see cref="InheritProtocolDisplay"/> means inherit the
    /// definition's root protocol.
    /// </summary>
    [ObservableProperty]
    private string _protocol = InheritProtocolDisplay;

    /// <summary>
    /// True when the row was auto-seeded from a legacy flat field and should not
    /// be removed via the delete button.
    /// </summary>
    public bool IsSeeded { get; init; }

    /// <summary>
    /// True when the row was auto-seeded with a library default value (not from the
    /// definition's <c>endpoints</c> dictionary). The save path skips these entries
    /// when they carry no meaningful override, keeping the JSON free of no-op rows.
    /// </summary>
    public bool IsAutoSeededDefault { get; init; }

    /// <summary>
    /// True when the selected operation allows a per-endpoint protocol override.
    /// Chat, models, messages, and embeddings are served by the provider's root
    /// protocol class and cannot switch to a different wire protocol per endpoint.
    /// Only decisions may override the protocol.
    /// </summary>
    public bool IsProtocolOverrideAllowed =>
        !string.IsNullOrWhiteSpace(Operation) && !NonOverridableOperations.Contains(Operation);

    /// <summary>
    /// The library default endpoint path for the selected operation, or empty when
    /// no operation is selected. Shown read-only in the editor so users see the
    /// built-in default next to their override.
    /// </summary>
    public string DefaultPath =>
        !string.IsNullOrWhiteSpace(Operation) && OperationDefaults.TryGetValue(Operation, out var d)
            ? d
            : string.Empty;

    partial void OnOperationChanged(string value)
    {
        OnPropertyChanged(nameof(IsProtocolOverrideAllowed));
        OnPropertyChanged(nameof(DefaultPath));
        if (!string.IsNullOrWhiteSpace(value) && NonOverridableOperations.Contains(value))
        {
            Protocol = InheritProtocolDisplay;
        }
    }

    private static IReadOnlyList<string> BuildProtocolChoices()
    {
        var choices = new List<string> { InheritProtocolDisplay };
        foreach (var protocol in System.Enum.GetValues<EProviderProtocol>())
        {
            choices.Add(ProviderProtocolMapper.ToJson(protocol));
        }

        return choices;
    }

    private static IReadOnlyList<string> BuildOperationChoices()
    {
        return
        [
            string.Empty,
            EndpointOperations.Chat,
            EndpointOperations.Models,
            EndpointOperations.Messages,
            EndpointOperations.Embeddings,
            EndpointOperations.Decisions
        ];
    }
}
