namespace AIProviderConnect.Models;

/// <summary>
/// A candidate token at a generation position with its natural-log probability, as reported by an
/// OpenAI-compatible <c>top_logprobs</c> entry.
/// </summary>
/// <param name="Token">The candidate token text.</param>
/// <param name="Logprob">The natural logarithm of the candidate's probability.</param>
public sealed record ChatTopLogprob(string Token, double Logprob);
