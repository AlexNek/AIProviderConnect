namespace AIProviderConnect.Constants;

/// <summary>
/// Wire property names for the decisions protocol. All decisions-specific JSON keys live here,
/// isolated from the generic models so the core carries no host-specific literals. The names match
/// the OpenRouter/TypeSafe decisions API (POST /api/alpha/decisions).
/// </summary>
public static class DecisionsPropertyNames
{
    // Request
    public const string Model = "model";
    public const string State = "state";
    public const string Questions = "questions";
    public const string Type = "type";
    public const string Instructions = "instructions";

    // Criteria carries the labelled options (Choice/Noul map) and the ordered levels (Score array).
    public const string Criteria = "criteria";

    // Question / answer type discriminators
    public const string Choice = "choice";
    public const string Noul = "noul";
    public const string Score = "score";

    // Response
    public const string Id = "id";
    public const string Provider = "provider";
    public const string Answers = "answers";

    // Answer payloads. The selected value shares the question's type key:
    //   choice answer -> "choice", noul answer -> "noul", score answer -> "score".
    public const string Confidence = "confidence";
    public const string Probabilities = "probabilities";
    public const string Legend = "legend";

    // Usage
    public const string Usage = "usage";
    public const string InputTokens = "input_tokens";
    public const string OutputTokens = "output_tokens";
    public const string Cost = "cost";
}
