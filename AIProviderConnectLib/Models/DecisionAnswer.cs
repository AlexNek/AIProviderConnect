namespace AIProviderConnect.Models;

/// <summary>
/// A typed answer to a single decision question. Exactly one of the typed-answer properties is
/// populated according to <see cref="Kind"/>; when <see cref="Kind"/> is
/// <see cref="EDecisionAnswerKind.Unknown"/>, all typed-answer properties are null.
/// </summary>
public sealed record DecisionAnswer
{
    /// <summary>
    /// Gets the answer kind, selecting which typed-answer property is populated.
    /// </summary>
    public required EDecisionAnswerKind Kind { get; init; }

    /// <summary>
    /// Gets the choice answer, populated when <see cref="Kind"/> is <see cref="EDecisionAnswerKind.Choice"/>.
    /// </summary>
    public ChoiceAnswer? Choice { get; init; }

    /// <summary>
    /// Gets the yes/no answer, populated when <see cref="Kind"/> is <see cref="EDecisionAnswerKind.Noul"/>.
    /// </summary>
    public NoulAnswer? Noul { get; init; }

    /// <summary>
    /// Gets the score answer, populated when <see cref="Kind"/> is <see cref="EDecisionAnswerKind.Score"/>.
    /// </summary>
    public ScoreAnswer? Score { get; init; }
}
