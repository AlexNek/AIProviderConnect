namespace AIProviderConnect.Models;

/// <summary>
/// One generated token with its natural-log probability and the highest-probability alternatives the
/// provider reported at the same position. The chosen token itself is included when the provider
/// lists it among the alternatives, so a reader can consider both sources.
/// </summary>
/// <param name="Token">The generated token text.</param>
/// <param name="Logprob">The natural logarithm of the generated token's probability.</param>
/// <param name="TopLogprobs">The alternative tokens reported at this position, highest first.</param>
public sealed record ChatTokenLogprob(
    string Token,
    double Logprob,
    IReadOnlyList<ChatTopLogprob> TopLogprobs);
