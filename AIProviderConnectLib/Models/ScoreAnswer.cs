namespace AIProviderConnect.Models;

/// <summary>
/// A decision answer for a <see cref="EDecisionQuestionKind.Score"/> question. The decisions API
/// returns <paramref name="Score"/> as a continuous value over the zero-based level indices
/// (for example <c>1.99</c> across a three-level scale), plus the per-level probability distribution
/// and a legend mapping each level index to its label.
/// </summary>
/// <param name="Score">The continuous score over the zero-based level indices.</param>
/// <param name="Confidence">The confidence in the score, in the range 0..1.</param>
/// <param name="LevelProbabilities">The probability per level, keyed by the level index as a string.</param>
/// <param name="Legend">The scale levels in order, index 0 first.</param>
public sealed record ScoreAnswer(
    double Score,
    double Confidence,
    IReadOnlyDictionary<string, double> LevelProbabilities,
    IReadOnlyList<string> Legend);
