namespace AIProviderConnect.Models;

/// <summary>
/// A single typed question posed to a decision model. The populated criteria shape depends on
/// <see cref="Kind"/>: a <see cref="EDecisionQuestionKind.Choice"/> uses <see cref="Criteria"/>
/// (option → description), a <see cref="EDecisionQuestionKind.Noul"/> may use <see cref="Criteria"/>
/// (<c>true</c>/<c>false</c> descriptions, optional), and a <see cref="EDecisionQuestionKind.Score"/>
/// uses <see cref="Scale"/> (ordered levels). On the wire the score levels are sent under the same
/// <c>criteria</c> key as an ordered array, matching the decisions API. A decision question carries
/// no sampling parameters.
/// </summary>
public sealed record DecisionQuestion
{
    /// <summary>
    /// Gets the kind of question, determining the required criteria shape and returned answer primitive.
    /// </summary>
    public required EDecisionQuestionKind Kind { get; init; }

    /// <summary>
    /// Gets free-text instructions guiding the decision for this question.
    /// </summary>
    public string? Instructions { get; init; }

    /// <summary>
    /// Gets the labelled criteria: option → description for <see cref="EDecisionQuestionKind.Choice"/>,
    /// or <c>true</c>/<c>false</c> descriptions for <see cref="EDecisionQuestionKind.Noul"/>.
    /// Null for <see cref="EDecisionQuestionKind.Score"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Criteria { get; init; }

    /// <summary>
    /// Gets the ordered scale levels for <see cref="EDecisionQuestionKind.Score"/>.
    /// Null for other kinds.
    /// </summary>
    public IReadOnlyList<string>? Scale { get; init; }
}
