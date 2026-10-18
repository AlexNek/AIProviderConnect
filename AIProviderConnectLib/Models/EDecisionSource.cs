namespace AIProviderConnect.Models;

/// <summary>
/// Describes the mechanism that produced a <see cref="DecisionResponse"/> — not the provenance of the
/// checkpoint that answered. A native decisions host and an open reproduction of the decisions format
/// both report <see cref="Native"/>; a chat read-out reports <see cref="Readout"/>. Which model
/// actually answered travels in <see cref="DecisionResponse.Model"/>.
/// </summary>
public enum EDecisionSource
{
    /// <summary>
    /// The answer came from a host speaking the decisions wire.
    /// </summary>
    Native = 0,

    /// <summary>
    /// The answer was read out of a chat completion's option-token logprobs and renormalised.
    /// </summary>
    Readout = 1
}
