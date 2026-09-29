using System.Text.Json;

using AIProviderConnect.Constants;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;

namespace AIProviderConnect.Protocols;

/// <summary>
/// Static wire mapping for the decisions protocol. Builds the request body and parses the response,
/// isolating every decisions-specific JSON key in <see cref="DecisionsPropertyNames"/>. Validates the
/// request against the host's documented hard limits before sending so an over-limit or malformed
/// request surfaces as <see cref="AiErrorCodes.ConfigurationError"/> rather than a bare HTTP 400.
/// </summary>
public static class DecisionsWireProtocol
{
    private const string DecisionsEndpointKey = "decisionsEndpoint";
    private const string DecisionsBaseUrlKey = "decisionsBaseUrl";

    private const int MaxChoiceOptions = 255;
    private const int MinScoreLevels = 2;
    private const int MaxScoreLevels = 10;

    /// <summary>
    /// Applies protocol-specific configuration from the options' ProtocolConfiguration dictionary to
    /// the typed <see cref="DecisionProviderOptions"/>. Key names are defined and used only here.
    /// </summary>
    public static void ApplyProtocolConfiguration(DecisionProviderOptions options)
    {
        if (options.ProtocolConfiguration is null)
            return;

        if (options.ProtocolConfiguration.TryGetValue(DecisionsEndpointKey, out var endpoint)
            && !string.IsNullOrWhiteSpace(endpoint))
        {
            options.DecisionsEndpoint = endpoint;
        }

        if (options.ProtocolConfiguration.TryGetValue(DecisionsBaseUrlKey, out var baseUrl)
            && !string.IsNullOrWhiteSpace(baseUrl))
        {
            options.DecisionsBaseUrl = baseUrl;
        }
    }

    /// <summary>
    /// Builds the decisions request body: <c>{ model, state, questions }</c>. Validates that
    /// <see cref="DecisionRequest.State"/> is non-null and one of the documented shapes, that
    /// <see cref="DecisionRequest.Questions"/> is non-empty, and that each question satisfies its
    /// per-kind limits. Throws <see cref="AiException"/> with
    /// <see cref="AiErrorCodes.ConfigurationError"/> on any violation.
    /// </summary>
    public static object MapRequest(DecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.State is null)
            throw new AiException(
                AiErrorCodes.ConfigurationError,
                "Decision request state must not be null.");

        ValidateStateShape(request.State);

        if (request.Questions is null || request.Questions.Count == 0)
            throw new AiException(
                AiErrorCodes.ConfigurationError,
                "Decision request must contain at least one question.");

        var questions = new Dictionary<string, object?>();
        foreach (var (name, question) in request.Questions)
        {
            questions[name] = MapQuestion(name, question);
        }

        return new Dictionary<string, object?>
        {
            [DecisionsPropertyNames.Model] = request.Model,
            [DecisionsPropertyNames.State] = request.State,
            [DecisionsPropertyNames.Questions] = questions
        };
    }

    private static void ValidateStateShape(object state)
    {
        var isDocumentedShape = state is string
            || state is IReadOnlyDictionary<string, object?>
            || state is IReadOnlyList<string>;

        if (!isDocumentedShape)
            throw new AiException(
                AiErrorCodes.ConfigurationError,
                "Decision request state must be a text string, a JSON object " +
                "(IReadOnlyDictionary<string, object?>), or an array of text (IReadOnlyList<string>).");
    }

    private static object MapQuestion(string name, DecisionQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var body = new Dictionary<string, object?>
        {
            [DecisionsPropertyNames.Type] = MapQuestionType(question.Kind)
        };

        if (!string.IsNullOrWhiteSpace(question.Instructions))
            body[DecisionsPropertyNames.Instructions] = question.Instructions;

        switch (question.Kind)
        {
            case EDecisionQuestionKind.Choice:
                if (question.Criteria is null || question.Criteria.Count is < 1 or > MaxChoiceOptions)
                    throw new AiException(
                        AiErrorCodes.ConfigurationError,
                        $"Decision question '{name}' (Choice) requires Criteria with 1–{MaxChoiceOptions} options.");
                body[DecisionsPropertyNames.Criteria] = question.Criteria;
                break;

            case EDecisionQuestionKind.Score:
                // Score levels travel under the same "criteria" key as an ordered array.
                if (question.Scale is null || question.Scale.Count is < MinScoreLevels or > MaxScoreLevels)
                    throw new AiException(
                        AiErrorCodes.ConfigurationError,
                        $"Decision question '{name}' (Score) requires Scale with {MinScoreLevels}–{MaxScoreLevels} levels.");
                body[DecisionsPropertyNames.Criteria] = question.Scale;
                break;

            case EDecisionQuestionKind.Noul:
                if (question.Criteria is { Count: > 0 })
                    body[DecisionsPropertyNames.Criteria] = question.Criteria;
                break;

            default:
                throw new AiException(
                    AiErrorCodes.ConfigurationError,
                    $"Decision question '{name}' has an unsupported kind '{question.Kind}'.");
        }

        return body;
    }

    private static string MapQuestionType(EDecisionQuestionKind kind) => kind switch
    {
        EDecisionQuestionKind.Choice => DecisionsPropertyNames.Choice,
        EDecisionQuestionKind.Noul => DecisionsPropertyNames.Noul,
        EDecisionQuestionKind.Score => DecisionsPropertyNames.Score,
        _ => throw new AiException(
            AiErrorCodes.ConfigurationError,
            $"Unsupported decision question kind '{kind}'.")
    };

    /// <summary>
    /// Parses the decisions response: <c>answers</c> (dispatching on each answer's <c>type</c>),
    /// <c>id</c>, <c>model</c>, <c>provider</c>, and <c>usage</c> including cost. Unknown answer types
    /// parse to <see cref="EDecisionAnswerKind.Unknown"/> without failing the whole response.
    /// </summary>
    public static DecisionResponse ParseResponse(JsonElement json)
    {
        var answers = new Dictionary<string, DecisionAnswer>();
        if (json.TryGetProperty(DecisionsPropertyNames.Answers, out var answersNode)
            && answersNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in answersNode.EnumerateObject())
            {
                answers[property.Name] = ParseAnswer(property.Value);
            }
        }

        return new DecisionResponse
        {
            Id = GetString(json, DecisionsPropertyNames.Id),
            Model = GetString(json, DecisionsPropertyNames.Model),
            Provider = GetString(json, DecisionsPropertyNames.Provider),
            Answers = answers,
            Usage = ParseUsage(json)
        };
    }

    private static DecisionAnswer ParseAnswer(JsonElement element)
    {
        var type = element.TryGetProperty(DecisionsPropertyNames.Type, out var typeEl)
            && typeEl.ValueKind == JsonValueKind.String
                ? typeEl.GetString()
                : null;

        return type switch
        {
            DecisionsPropertyNames.Choice => new DecisionAnswer
            {
                Kind = EDecisionAnswerKind.Choice,
                Choice = ParseChoiceAnswer(element)
            },
            DecisionsPropertyNames.Noul => new DecisionAnswer
            {
                Kind = EDecisionAnswerKind.Noul,
                Noul = ParseNoulAnswer(element)
            },
            DecisionsPropertyNames.Score => new DecisionAnswer
            {
                Kind = EDecisionAnswerKind.Score,
                Score = ParseScoreAnswer(element)
            },
            _ => new DecisionAnswer { Kind = EDecisionAnswerKind.Unknown }
        };
    }

    // Choice answer: the selected option is under the "choice" key.
    private static ChoiceAnswer ParseChoiceAnswer(JsonElement element) => new(
        GetString(element, DecisionsPropertyNames.Choice),
        GetDouble(element, DecisionsPropertyNames.Confidence),
        ParseProbabilities(element));

    // Noul answer: the probability of "yes" is the value of the "noul" key.
    private static NoulAnswer ParseNoulAnswer(JsonElement element) =>
        new(GetDouble(element, DecisionsPropertyNames.Noul));

    // Score answer: "score" is a continuous value over the level indices; "legend" is an
    // index -> label object; "probabilities" is keyed by level index.
    private static ScoreAnswer ParseScoreAnswer(JsonElement element) => new(
        GetDouble(element, DecisionsPropertyNames.Score),
        GetDouble(element, DecisionsPropertyNames.Confidence),
        ParseProbabilities(element),
        ParseLegend(element));

    private static IReadOnlyDictionary<string, double> ParseProbabilities(JsonElement element)
    {
        var result = new Dictionary<string, double>();
        if (element.TryGetProperty(DecisionsPropertyNames.Probabilities, out var node)
            && node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number)
                    result[property.Name] = property.Value.GetDouble();
            }
        }

        return result;
    }

    // Legend is an index -> label object (e.g. {"0":"low","1":"high"}); return the labels in
    // ascending numeric-index order so the caller receives an ordered scale.
    private static IReadOnlyList<string> ParseLegend(JsonElement element)
    {
        if (!element.TryGetProperty(DecisionsPropertyNames.Legend, out var node)
            || node.ValueKind != JsonValueKind.Object)
            return [];

        return node.EnumerateObject()
            .Select(p => (Index: int.TryParse(p.Name, out var i) ? i : int.MaxValue,
                          Label: p.Value.ValueKind == JsonValueKind.String
                              ? p.Value.GetString() ?? string.Empty
                              : string.Empty))
            .OrderBy(x => x.Index)
            .Select(x => x.Label)
            .ToList();
    }

    // The decisions wire reports input_tokens/output_tokens and carries no total key, so total is
    // computed as input + output. Cost is null when the response omits it.
    private static UsageInfo ParseUsage(JsonElement json)
    {
        if (!json.TryGetProperty(DecisionsPropertyNames.Usage, out var usageNode)
            || usageNode.ValueKind != JsonValueKind.Object)
            return new UsageInfo();

        var input = usageNode.TryGetProperty(DecisionsPropertyNames.InputTokens, out var inputEl)
            && inputEl.ValueKind == JsonValueKind.Number
                ? inputEl.GetInt32()
                : 0;
        var output = usageNode.TryGetProperty(DecisionsPropertyNames.OutputTokens, out var outputEl)
            && outputEl.ValueKind == JsonValueKind.Number
                ? outputEl.GetInt32()
                : 0;

        decimal? cost = usageNode.TryGetProperty(DecisionsPropertyNames.Cost, out var costEl)
            && costEl.ValueKind == JsonValueKind.Number
                ? costEl.GetDecimal()
                : null;

        return new UsageInfo
        {
            PromptTokens = input,
            CompletionTokens = output,
            TotalTokens = input + output,
            Cost = cost
        };
    }

    private static string GetString(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static double GetDouble(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0d;
}
