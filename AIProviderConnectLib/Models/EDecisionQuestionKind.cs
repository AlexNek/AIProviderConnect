namespace AIProviderConnect.Models;

/// <summary>
/// The kind of a decision question, determining which criteria shape is required and
/// which answer primitive is returned.
/// </summary>
public enum EDecisionQuestionKind
{
    /// <summary>
    /// Selects one option from a labelled set. Requires <see cref="DecisionQuestion.Criteria"/>
    /// with 1–255 options.
    /// </summary>
    Choice,

    /// <summary>
    /// A yes/no question. <see cref="DecisionQuestion.Criteria"/> is optional and, when supplied,
    /// describes the <c>true</c>/<c>false</c> cases.
    /// </summary>
    Noul,

    /// <summary>
    /// Scores against an ordered scale. Requires <see cref="DecisionQuestion.Scale"/> with 2–10 levels.
    /// </summary>
    Score
}
