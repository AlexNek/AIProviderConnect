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
    /// The fixed operation vocabulary of the endpoints block.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownOperations =
    [
        EndpointOperations.Chat,
        EndpointOperations.Models,
        EndpointOperations.Messages,
        EndpointOperations.Embeddings,
        EndpointOperations.Decisions
    ];

    /// <summary>
    /// Protocol choices for the row: an empty "inherit" choice followed by the
    /// JSON aliases of every provider protocol.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownProtocols = BuildProtocolChoices();

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
    /// Protocol override; empty means inherit the definition's root protocol.
    /// </summary>
    [ObservableProperty]
    private string _protocol = string.Empty;

    private static IReadOnlyList<string> BuildProtocolChoices()
    {
        var choices = new List<string> { string.Empty };
        foreach (var protocol in System.Enum.GetValues<EProviderProtocol>())
        {
            choices.Add(ProviderProtocolMapper.ToJson(protocol));
        }

        return choices;
    }
}
