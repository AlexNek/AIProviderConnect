using AIProviderConnect.Models;

namespace AIProviderConnect.Abstractions;

/// <summary>
/// Optional runtime capability for decision models — providers that answer typed questions about
/// application state with probabilities rather than generated chat prose.
/// Use <c>is IDecisionProvider</c> to check at the call site, or inspect <see cref="SupportsDecisions"/>.
/// </summary>
public interface IDecisionProvider
{
    /// <summary>
    /// Gets a value indicating whether this provider instance supports decision calls.
    /// Mirrors <see cref="IModelDiscoveryProvider.SupportsModelDiscovery"/>.
    /// </summary>
    bool SupportsDecisions { get; }

    /// <summary>
    /// Gets the mechanism that produced an answer. Defaults to <see cref="EDecisionSource.Native"/> so
    /// existing implementors keep working; a provider whose answer comes from reading a chat
    /// completion's option logprobs reports <see cref="EDecisionSource.Readout"/>, and every decorator
    /// forwards the inner provider's value rather than restating it.
    /// </summary>
    EDecisionSource DecisionSource => EDecisionSource.Native;

    /// <summary>
    /// Sends application state plus one or more typed questions and returns typed probabilistic answers.
    /// </summary>
    /// <param name="request">The decision request containing state and typed questions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decision response with one typed answer per question, keyed by question name.</returns>
    Task<DecisionResponse> DecideAsync(
        DecisionRequest request,
        CancellationToken cancellationToken = default);
}
