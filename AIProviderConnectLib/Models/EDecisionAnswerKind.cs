namespace AIProviderConnect.Models;

/// <summary>
/// The kind of a decision answer primitive. <see cref="Unknown"/> provides forward compatibility
/// for primitives added upstream after this library version.
/// </summary>
public enum EDecisionAnswerKind
{
    /// <summary>An answer selecting one option from a set.</summary>
    Choice,

    /// <summary>A yes/no probability answer.</summary>
    Noul,

    /// <summary>A score against an ordered scale.</summary>
    Score,

    /// <summary>An answer whose type is not recognized by this library version.</summary>
    Unknown
}
