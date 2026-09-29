namespace AIProviderConnect.Models;

/// <summary>
/// A decision answer for a <see cref="EDecisionQuestionKind.Noul"/> (yes/no) question.
/// </summary>
/// <param name="ProbabilityOfYes">The probability of the "yes" outcome, in the range 0..1.</param>
public sealed record NoulAnswer(double ProbabilityOfYes);
