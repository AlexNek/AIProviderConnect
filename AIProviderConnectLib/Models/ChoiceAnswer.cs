namespace AIProviderConnect.Models;

/// <summary>
/// A decision answer for a <see cref="EDecisionQuestionKind.Choice"/> question.
/// </summary>
/// <param name="Selected">The selected option label.</param>
/// <param name="Confidence">The confidence in the selection, in the range 0..1.</param>
/// <param name="Probabilities">The probability assigned to each option, keyed by option label.</param>
public sealed record ChoiceAnswer(
    string Selected,
    double Confidence,
    IReadOnlyDictionary<string, double> Probabilities);
