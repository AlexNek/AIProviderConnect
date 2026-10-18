using System.Globalization;
using System.Text;
using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;

using Microsoft.Extensions.Logging;

namespace AIProviderConnect.Providers;

/// <summary>
/// The single read-out implementation: renders a typed question into one chat turn, reads the option
/// tokens' logprobs from the provider's own <see cref="IAIProvider.ChatAsync"/>, and renormalises
/// them over the declared options. It contains no switch on the wire protocol, no per-host formula,
/// and no per-model name — every host reaches it through the same chat call, and a protocol whose API
/// carries no logprobs surfaces as <see cref="AiErrorCodes.DecisionReadoutUnavailable"/> rather than a
/// fabricated distribution.
/// </summary>
internal sealed class ChatReadoutDecision
{
    private const string StatePlaceholder = "{state}";
    private const string InstructionsPlaceholder = "{instructions}";
    private const string OptionsPlaceholder = "{options}";

    private readonly IAIProvider _provider;
    private readonly AIProviderOptions _options;
    private readonly string _providerId;
    private readonly ILogger _logger;

    internal ChatReadoutDecision(
        IAIProvider provider,
        AIProviderOptions options,
        string providerId,
        ILogger logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _providerId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Issues one chat completion per question in enumeration order and returns one typed answer per
    /// question. The first failure aborts the whole call — no partial answers map is returned — and
    /// usage is summed across the calls with <see cref="UsageInfo.Cost"/> left null.
    /// </summary>
    internal async Task<DecisionResponse> ExecuteAsync(
        DecisionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        AIProviderBase.ValidateDeclaredLimits(request, _options, _providerId);
        ValidateReadoutCeiling(request);

        var answers = new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal);
        var promptTokens = 0;
        var completionTokens = 0;
        var totalTokens = 0;
        string? responseId = null;
        string? responseModel = null;

        foreach (var (name, question) in request.Questions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var optionTokens = ResolveOptionTokens(question);
            var prompt = BuildPrompt(request, question);

            var chatResponse = await _provider.ChatAsync(
                new ChatCompletionRequest
                {
                    Model = request.Model,
                    Messages = [new ChatMessage { Role = EChatRole.User, Content = prompt }],
                    Temperature = _options.DecisionReadoutTemperature,
                    Logprobs = true,
                    TopLogprobs = _options.DecisionReadoutTopLogprobs
                },
                cancellationToken).ConfigureAwait(false);

            responseId ??= chatResponse.Id;
            responseModel ??= chatResponse.Model;
            promptTokens += chatResponse.Usage.PromptTokens;
            completionTokens += chatResponse.Usage.CompletionTokens;
            totalTokens += chatResponse.Usage.TotalTokens;

            var probabilities = ReadDistribution(chatResponse, optionTokens);
            answers[name] = MapAnswer(question, probabilities);
        }

        _logger.LogDebug(
            "Provider '{ProviderId}': read-out answered {Count} question(s) over the chat surface",
            _providerId, answers.Count);

        return new DecisionResponse
        {
            Id = responseId ?? string.Empty,
            Model = responseModel ?? string.Empty,
            Provider = string.Empty,
            Answers = answers,
            Usage = new UsageInfo
            {
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = totalTokens,
                Cost = null
            }
        };
    }

    // The read-out's own ceiling is the requested top_logprobs count: an option that cannot appear
    // in the response cannot be read, so a partial distribution is refused before any request leaves
    // rather than silently answering from the options that happened to fit. A host may declare a
    // lower ceiling through DecisionsMaxOptions; this check is never relaxed by one.
    private void ValidateReadoutCeiling(DecisionRequest request)
    {
        var ceiling = _options.DecisionReadoutTopLogprobs;

        foreach (var (name, question) in request.Questions)
        {
            if (question.Kind != EDecisionQuestionKind.Choice)
                continue;

            var optionCount = question.Criteria?.Count ?? 0;
            if (optionCount <= ceiling)
                continue;

            throw new AiException(
                AiErrorCodes.ConfigurationError,
                $"Provider '{_providerId}': question '{name}' declares {optionCount} options, above " +
                $"the read-out's top_logprobs ceiling of {ceiling}; the surplus options cannot appear " +
                "in the response.");
        }
    }

    // Renders the built-in prompt from the request data, or the declared template when the host
    // supplies one. The template is an override, never a precondition: a model with no declared
    // prompt is still answered.
    private string BuildPrompt(DecisionRequest request, DecisionQuestion question)
    {
        var optionLines = BuildOptionLines(question);
        var state = RenderState(request.State);

        if (!string.IsNullOrEmpty(_options.DecisionReadoutPromptTemplate))
        {
            return _options.DecisionReadoutPromptTemplate!
                .Replace(StatePlaceholder, state, StringComparison.Ordinal)
                .Replace(InstructionsPlaceholder, question.Instructions ?? string.Empty, StringComparison.Ordinal)
                .Replace(OptionsPlaceholder, string.Join("\n", optionLines), StringComparison.Ordinal);
        }

        var lines = new List<string> { $"State: {state}" };

        if (!string.IsNullOrWhiteSpace(question.Instructions))
        {
            lines.Add(string.Empty);
            lines.Add(question.Instructions!);
        }

        if (optionLines.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(optionLines);
        }

        lines.Add(string.Empty);
        lines.Add(BuildAnswerCue(question));
        return string.Join("\n", lines);
    }

    private List<string> BuildOptionLines(DecisionQuestion question)
    {
        switch (question.Kind)
        {
            case EDecisionQuestionKind.Choice:
            {
                var labels = question.Criteria?.Keys.ToList() ?? [];
                var lines = new List<string>(labels.Count);
                for (var index = 0; index < labels.Count; index++)
                {
                    var description = question.Criteria![labels[index]];
                    lines.Add(string.IsNullOrWhiteSpace(description)
                        ? $"{OptionPrefix(index)}. {labels[index]}"
                        : $"{OptionPrefix(index)}. {labels[index]}: {description}");
                }

                return lines;
            }

            case EDecisionQuestionKind.Score:
            {
                var levels = question.Scale ?? [];
                var lines = new List<string>(levels.Count);
                for (var index = 0; index < levels.Count; index++)
                {
                    lines.Add($"{OptionPrefix(index)}. {levels[index]}");
                }

                return lines;
            }

            case EDecisionQuestionKind.Noul:
            {
                var (yesToken, noToken) = ResolveNoulTokens();
                var yesDescription = FindNoulDescription(question, yesToken);
                var noDescription = FindNoulDescription(question, noToken);
                if (string.IsNullOrWhiteSpace(yesDescription) && string.IsNullOrWhiteSpace(noDescription))
                    return [];

                var lines = new List<string> { yesToken };
                if (!string.IsNullOrWhiteSpace(yesDescription))
                    lines[0] = $"{yesToken}: {yesDescription}";

                var noLine = noToken;
                if (!string.IsNullOrWhiteSpace(noDescription))
                    noLine = $"{noToken}: {noDescription}";
                lines.Add(noLine);
                return lines;
            }

            default:
                return [];
        }
    }

    private string BuildAnswerCue(DecisionQuestion question)
    {
        if (question.Kind != EDecisionQuestionKind.Noul)
            return "Answer:";

        var (yesToken, noToken) = ResolveNoulTokens();
        return $"Answer {yesToken} or {noToken}:";
    }

    // The tokens whose logprobs carry the distribution for this question. A Score question maps
    // token i to level index i; a Noul question reads the configured yes/no tokens themselves.
    private IReadOnlyList<string> ResolveOptionTokens(DecisionQuestion question)
    {
        switch (question.Kind)
        {
            case EDecisionQuestionKind.Choice:
            {
                var labels = question.Criteria?.Keys.ToList() ?? [];
                var tokens = new List<string>(labels.Count);
                for (var index = 0; index < labels.Count; index++)
                {
                    tokens.Add(OptionPrefix(index));
                }

                return tokens;
            }

            case EDecisionQuestionKind.Score:
            {
                var levels = question.Scale ?? [];
                var tokens = new List<string>(levels.Count);
                for (var index = 0; index < levels.Count; index++)
                {
                    tokens.Add(OptionPrefix(index));
                }

                return tokens;
            }

            case EDecisionQuestionKind.Noul:
            {
                var (yesToken, noToken) = ResolveNoulTokens();
                return [yesToken, noToken];
            }

            default:
                return [];
        }
    }

    private (string Yes, string No) ResolveNoulTokens()
    {
        var tokens = _options.DecisionReadoutBoolTokens
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 2 ? (tokens[0], tokens[1]) : ("yes", "no");
    }

    private static string? FindNoulDescription(DecisionQuestion question, string token)
    {
        if (question.Criteria is null)
            return null;

        foreach (var (key, description) in question.Criteria)
        {
            if (string.Equals(key, token, StringComparison.OrdinalIgnoreCase))
                return description;
        }

        // The decisions wire models a Noul description under the true/false keys; map the configured
        // yes/no tokens onto them when no entry carries the token itself.
        var boolKey = string.Equals(token, "yes", StringComparison.OrdinalIgnoreCase) ? "true"
            : string.Equals(token, "no", StringComparison.OrdinalIgnoreCase) ? "false"
            : null;

        if (boolKey is not null && question.Criteria.TryGetValue(boolKey, out var mapped))
            return mapped;

        return null;
    }

    // Reads the first generated token and its alternatives. A declared option absent from the
    // reported distributions contributes zero; when none of them appears at all, no distribution
    // over the declared options exists and the call is refused rather than guessed.
    private double[] ReadDistribution(ChatCompletionResponse response, IReadOnlyList<string> optionTokens)
    {
        if (response.Logprobs is null)
        {
            throw new AiException(
                AiErrorCodes.DecisionReadoutUnavailable,
                $"Provider '{_providerId}': the chat response carried no logprobs block, so token " +
                "probabilities cannot be read. The host's chat API does not expose them.");
        }

        var logprobs = new double[optionTokens.Count];
        var found = new bool[optionTokens.Count];

        if (response.Logprobs.Content.Count > 0)
        {
            var answer = response.Logprobs.Content[0];
            Consider(answer.Token, answer.Logprob);
            foreach (var top in answer.TopLogprobs)
                Consider(top.Token, top.Logprob);
        }

        if (!found.Any(static value => value))
        {
            throw new AiException(
                AiErrorCodes.DecisionReadoutUnreadable,
                $"Provider '{_providerId}': none of the declared option tokens appears in the chat " +
                "response's logprobs, so no probability distribution over them can be computed.");
        }

        var max = double.NegativeInfinity;
        for (var index = 0; index < logprobs.Length; index++)
        {
            if (found[index])
                max = Math.Max(max, logprobs[index]);
        }

        var sum = 0d;
        var probabilities = new double[optionTokens.Count];
        for (var index = 0; index < logprobs.Length; index++)
        {
            if (!found[index])
                continue;

            probabilities[index] = Math.Exp(logprobs[index] - max);
            sum += probabilities[index];
        }

        for (var index = 0; index < probabilities.Length; index++)
            probabilities[index] /= sum;

        return probabilities;

        void Consider(string token, double logprob)
        {
            var normalized = NormalizeToken(token);
            for (var index = 0; index < optionTokens.Count; index++)
            {
                if (!string.Equals(normalized, NormalizeToken(optionTokens[index]), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!found[index] || logprob > logprobs[index])
                {
                    logprobs[index] = logprob;
                    found[index] = true;
                }
            }
        }
    }

    private static DecisionAnswer MapAnswer(DecisionQuestion question, double[] probabilities)
    {
        switch (question.Kind)
        {
            case EDecisionQuestionKind.Choice:
            {
                var labels = question.Criteria!.Keys.ToList();
                var dictionary = new Dictionary<string, double>(StringComparer.Ordinal);
                var best = 0;
                for (var index = 0; index < labels.Count; index++)
                {
                    dictionary[labels[index]] = probabilities[index];
                    if (probabilities[index] > probabilities[best])
                        best = index;
                }

                return new DecisionAnswer
                {
                    Kind = EDecisionAnswerKind.Choice,
                    Choice = new ChoiceAnswer(labels[best], probabilities[best], dictionary)
                };
            }

            case EDecisionQuestionKind.Noul:
            {
                var yes = probabilities[0];
                var no = probabilities[1];
                var denominator = yes + no;
                return new DecisionAnswer
                {
                    Kind = EDecisionAnswerKind.Noul,
                    Noul = new NoulAnswer(denominator > 0 ? yes / denominator : 0d)
                };
            }

            case EDecisionQuestionKind.Score:
            {
                var levels = question.Scale ?? [];
                var dictionary = new Dictionary<string, double>(StringComparer.Ordinal);
                var score = 0d;
                var best = 0d;
                for (var index = 0; index < levels.Count; index++)
                {
                    dictionary[index.ToString(CultureInfo.InvariantCulture)] = probabilities[index];
                    score += index * probabilities[index];
                    best = Math.Max(best, probabilities[index]);
                }

                return new DecisionAnswer
                {
                    Kind = EDecisionAnswerKind.Score,
                    Score = new ScoreAnswer(score, best, dictionary, levels)
                };
            }

            default:
                return new DecisionAnswer { Kind = EDecisionAnswerKind.Unknown };
        }
    }

    private static string RenderState(object? state) => state switch
    {
        null => string.Empty,
        string text => text,
        _ => JsonSerializer.Serialize(state)
    };

    // A provider may report "A", "A.", " A" or "a"; the declared option is matched on the token
    // itself, so surrounding whitespace and a trailing period never decide a read.
    private static string NormalizeToken(string token) =>
        token.Trim().TrimEnd('.').Trim();

    // Column-style label: A..Z, then AA, AB, … so an option count above 26 still gets a stable token.
    private string OptionPrefix(int index)
    {
        if (string.Equals(_options.DecisionReadoutOptionTokenStyle, "levelIndex", StringComparison.OrdinalIgnoreCase))
            return index.ToString(CultureInfo.InvariantCulture);

        var label = LetterLabel(index);
        return string.Equals(_options.DecisionReadoutOptionTokenStyle, "lowercaseLetter", StringComparison.OrdinalIgnoreCase)
            ? label.ToLowerInvariant()
            : label;
    }

    private static string LetterLabel(int index)
    {
        var value = index + 1;
        var builder = new StringBuilder();
        while (value > 0)
        {
            value--;
            builder.Insert(0, (char)('A' + value % 26));
            value /= 26;
        }

        return builder.ToString();
    }
}
