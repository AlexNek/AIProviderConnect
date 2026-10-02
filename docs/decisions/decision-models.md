# Decision Models

A **decision model** does not chat. It answers typed questions about your
application state with probabilities. You send a snapshot of state plus one or
more questions, and the model returns a typed answer per question — a selected
option, a yes/no probability, or a score against a scale — each with its own
confidence and probability distribution.

Decisions are an optional provider capability exposed through a separate
interface, exactly like streaming, model discovery, and embeddings:

```csharp
public interface IDecisionProvider
{
    bool SupportsDecisions { get; }

    Task<DecisionResponse> DecideAsync(
        DecisionRequest request,
        CancellationToken cancellationToken = default);
}
```

A decision provider is **not** a chat provider: calling `ChatAsync` on one
throws `AiException(AiErrorCodes.ChatNotSupported)`.

## Capability discovery

There are two ways to discover decision support, one per instance and one per
model:

- **Provider-level** — check the runtime capability at the call site:

    ```csharp
    if (provider is IDecisionProvider decision && decision.SupportsDecisions)
    {
        var response = await decision.DecideAsync(request, ct);
    }
    ```

- **Model-level** — a model that answers with decisions is tagged
  `EModelCapability.Decision`, so it is discoverable *without a call* through
  the normal model list. Because a decisions surface and a model catalog are
  different endpoints, `DecisionProvider` does not implement model discovery
  itself; tag decision models through `OverrideModels` on your discovery-capable
  gateway (see [Model Discovery](../model-discovery/model-discovery.md)).

## The three primitives

Each question declares a `Kind`; the answer carries the matching typed payload.

| Kind (`EDecisionQuestionKind`) | Criteria shape | Answer (`EDecisionAnswerKind`) | Answer payload |
| --- | --- | --- | --- |
| `Choice` | `Criteria`: option → description, 1–255 options | `Choice` | `ChoiceAnswer(Selected, Confidence, Probabilities)` |
| `Noul` (yes/no) | `Criteria` optional (`true`/`false` descriptions) | `Noul` | `NoulAnswer(ProbabilityOfYes)` |
| `Score` | `Scale`: ordered levels, 2–10 | `Score` | `ScoreAnswer(Score, Confidence, LevelProbabilities, Legend)` |

You set the score levels on `DecisionQuestion.Scale`; on the wire they are sent
under the same `criteria` key the API expects. A `Score` answer's `Score` is a
continuous value over the zero-based level indices (for example `1.99` across a
three-level scale), and `Legend` returns the level labels in index order.

The request is validated against these limits **before** it is sent — an
over-limit or malformed request throws `AiException(AiErrorCodes.ConfigurationError)`
rather than surfacing as a bare HTTP 400. A decision request carries no sampling
parameters (temperature, top-p, and the like are meaningless to a decision call).

An answer type the library does not recognize parses to
`EDecisionAnswerKind.Unknown` without failing the whole response, so a primitive
added upstream later never breaks an existing consumer.

## A worked example

The following triages a support ticket with all three primitives in one call:

```csharp
var request = new DecisionRequest
{
    // Empty Model falls back to the provider's DefaultModel.
    Model = "typesafe/jev-1.13",
    State = "Customer was double-charged for their annual plan and wants a refund.",
    Questions = new Dictionary<string, DecisionQuestion>
    {
        ["priority"] = new()
        {
            Kind = EDecisionQuestionKind.Choice,
            Instructions = "How urgently should this ticket be handled?",
            Criteria = new Dictionary<string, string>
            {
                ["low"]    = "Can wait several days",
                ["normal"] = "Handle within a day",
                ["high"]   = "Handle within the hour",
            },
        },
        ["is_billing"] = new()
        {
            Kind = EDecisionQuestionKind.Noul,
        },
        ["severity"] = new()
        {
            Kind = EDecisionQuestionKind.Score,
            Scale = ["minor", "moderate", "major", "critical"],
        },
    },
};

var response = await decisionProvider.DecideAsync(request, ct);

// Typed branching over the answers.
foreach (var (name, answer) in response.Answers)
{
    switch (answer.Kind)
    {
        case EDecisionAnswerKind.Choice:
            var choice = answer.Choice!;
            Console.WriteLine($"{name}: {choice.Selected} ({choice.Confidence:P0})");
            break;

        case EDecisionAnswerKind.Noul:
            Console.WriteLine($"{name}: P(yes) = {answer.Noul!.ProbabilityOfYes:P0}");
            break;

        case EDecisionAnswerKind.Score:
            var score = answer.Score!;
            // Score is continuous over the level indices; round to name the nearest level.
            var nearest = score.Legend[(int)Math.Round(score.Score)];
            Console.WriteLine($"{name}: {score.Score:F2} → {nearest} (confidence {score.Confidence:P0})");
            break;

        case EDecisionAnswerKind.Unknown:
            // A primitive added upstream after this library version — skip safely.
            break;
    }
}
```

## State shapes

`DecisionRequest.State` is serialized to the wire as-is and must be one of the
three documented shapes:

- a plain text string,
- a JSON object — `IReadOnlyDictionary<string, object?>`, or
- an array of text — `IReadOnlyList<string>`.

A null `State`, a null/empty `Questions` map, or any other state shape throws
`AiException(AiErrorCodes.ConfigurationError)`.

## Usage and cost

`DecisionResponse.Usage` is the same `UsageInfo` record used everywhere else,
extended with a nullable `Cost` (USD):

```csharp
Console.WriteLine($"Tokens: {response.Usage.TotalTokens}");
if (response.Usage.Cost is { } cost)
    Console.WriteLine($"Cost: ${cost}");
```

The decisions wire carries no total-tokens key, so `TotalTokens` is computed as
`PromptTokens + CompletionTokens`. `Cost` is `null` when the response omits a
cost field — the first-party TypeSafe host reports no cost, while a router such
as OpenRouter does. All other protocol parsers leave `Cost` unset.

## Error codes

| Code | Meaning |
| --- | --- |
| `ai/chat-not-supported` | `ChatAsync` was called on a decision provider |
| `ai/decision-not-supported` | A decision call was requested on a provider that does not support decisions |
| `ai/configuration-error` | Null state, empty questions, an unsupported state shape, or a question that violates its per-kind limits |
| `ai/invalid-request` | The effective model resolved empty (no request model, no override, no `DefaultModel`) |

Rate limits, cancellation, retry, and network-error classification behave
exactly as they do for chat — see [Error Handling](../concepts/error-handling.md).

## When to use a decision model

A decision model is a cheap, fast decision layer inside an agent or application
loop — model routing, tool-call risk gating, auto-mode safety checks, parallel
multi-question classification about one state. It is used **alongside** a
generative LLM, not instead of one: the generative model produces content, the
decision model judges it.

Multiple questions over one state are evaluated in parallel at low marginal
cost, so a single call can answer "is this safe?", "which tool?", and "how
urgent?" together.

### Reading answers safely

Confidence is **per question** and not comparable across question kinds — a
question and its negation need not sum to 1. Tune each threshold per kind.
Pin a versioned model id (e.g. `jev-1.13.0`) once a threshold is tuned and
rely on `DecisionResponse.Model` reporting the versioned id that answered.

The context budget is host/model-specific (e.g. 32k state + longest question,
64k state + all questions on jev-1.13) and an over-budget call fails as a host
error — the library does not pre-count tokens.

Jev treats state as data, not as hostile input, so a decision-based guard
belongs alongside deterministic checks. Low-confidence or unanswerable
questions should escalate to a chat model (fallback).

## Example: Jev via OpenRouter

The **Jev** decision model (TypeSafe's *System One*, `typesafe/jev-1.13`) is a
supported worked example. It is reached through OpenRouter's decisions endpoint
and wired entirely through the normal builder path — no library edit and no
embedded catalog entry are needed, so switching hosts later (or moving to a
future TypeSafe-direct endpoint) is a configuration change only.

```csharp
var jev = new ProviderDefinition
{
    Id = "openrouter-decisions",
    DisplayName = "Jev (via OpenRouter)",
    Protocol = EProviderProtocol.Decision,
    // OpenRouter's API root carries no /v1 segment: the endpoint is appended
    // via new Uri(base, "alpha/decisions").
    BaseUrl = "https://openrouter.ai/api/",
};

services.AddSingleton(new HttpClient());
services.AddAiProviders(b => b
    .Add(jev)
    .Configure<DecisionProviderOptions>("openrouter-decisions", o =>
    {
        o.ApiKey = "<your-openrouter-key>";
        o.DefaultModel = "typesafe/jev-1.13";
    })
    // Make the decision model discoverable via the model list.
    .OverrideModels("openrouter-decisions",
    [
        new ModelOverride
        {
            Id = "typesafe/jev-1.13",
            DisplayName = "Jev (System One)",
            Capabilities = EModelCapability.Decision,
        },
    ]));
```

Resolve it as a keyed provider and call `DecideAsync`:

```csharp
var provider = serviceProvider.GetRequiredKeyedService<IAIProvider>("openrouter-decisions");
var jevDecision = (IDecisionProvider)provider;
var response = await jevDecision.DecideAsync(request, ct);
```

!!! note "The decisions endpoint is configurable"
    `DecisionProviderOptions.DecisionsEndpoint` defaults to `alpha/decisions`
    and is resolved against `BaseUrl`. For a host whose decisions surface lives
    on a different origin than the provider `BaseUrl`, set
    `DecisionsBaseUrl` to a full URL. Both can also be supplied through the
    definition's `protocolConfiguration` (`decisionsEndpoint`, `decisionsBaseUrl`).

## Example: Jev first-party API

The same `DecisionProvider` works against the Jev first-party API — only the
base URL and endpoint differ. This proves the design is host-neutral:

```csharp
var jevDirect = new ProviderDefinition
{
    Id = "jev-direct",
    DisplayName = "Jev (first-party)",
    Protocol = EProviderProtocol.Decision,
    BaseUrl = "https://thejevai.com/v1/",
};

services.AddSingleton(new HttpClient());
services.AddAiProviders(b => b
    .Add(jevDirect)
    .Configure<DecisionProviderOptions>("jev-direct", o =>
    {
        o.ApiKey = "<your-thejevai-key>";
        o.DefaultModel = "jev-latest";
        // The first-party surface lives at /systemone, not alpha/decisions.
        o.DecisionsEndpoint = "systemone";
    })
    .OverrideModels("jev-direct",
    [
        new ModelOverride
        {
            Id = "jev-latest",
            DisplayName = "Jev (System One)",
            Capabilities = EModelCapability.Decision,
        },
    ]));
```

The only differences from the OpenRouter example are the `BaseUrl`, the
`DecisionsEndpoint`, and the model id — the same `IDecisionProvider`, the
same request/response shapes, the same typed answers.

## Single-id registration via `endpoints`

When a chat-capable host also serves decisions, the definition can carry the
decisions surface in its `endpoints` block instead of splitting into a
dedicated `*-decisions` id. Jev via OpenRouter now needs one id:

```csharp
var openrouter = new ProviderDefinition
{
    Id = "openrouter",
    DisplayName = "OpenRouter",
    Protocol = EProviderProtocol.OpenAICompatible,
    BaseUrl = "https://openrouter.ai/api/v1/",
    Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        // The decisions surface sits on /api/, not under /api/v1/ — hence the
        // baseUrl override; the protocol override is what selects the combined
        // provider class at registration time.
        ["decisions"] = new EndpointDefinition
        {
            Path = "alpha/decisions",
            BaseUrl = "https://openrouter.ai/api/",
            Protocol = EProviderProtocol.Decision,
        },
    },
};

services.AddSingleton(new HttpClient());
services.AddAiProviders(b => b
    .Add(openrouter)
    .Configure<OpenAICompatibleProviderOptions>("openrouter", o =>
    {
        o.ApiKey = "<your-openrouter-key>";
        o.DefaultModel = "openai/gpt-4o-mini";
    }));
```

The one registered id answers every capability:

```csharp
var provider = serviceProvider.GetRequiredKeyedService<IAIProvider>("openrouter");
var chat = await ((IStreamingChatProvider)provider).ChatAsync(chatRequest, ct);
var decision = await ((IDecisionProvider)provider).DecideAsync(request, ct);
```

The dedicated-id pattern above remains the choice when the decision host is
a different service, or when a chat provider must not expose decisions.

## References

- [OpenRouter Jev hub](https://openrouter.ai/docs/guides/community/jev)
- [Jev tutorial](https://openrouter.ai/docs/guides/community/jev-tutorial)
- [Decisions API reference](https://openrouter.ai/docs/api/api-reference/alphadecisions/submit-a-decisions-questions-and-answers-request)
- [Jev model page](https://openrouter.ai/typesafe/jev-1.13)
- [TypeSafe docs](https://docs.typesafe.ai) (System One, Primitives, Confidence)
- [LangChain — Building a Harness with Jev](https://www.langchain.com/blog/building-a-harness-with-jev) — third-party prior art; its provider-agnostic `TypeSafeClassifier` mirrors this library's host-neutral `IDecisionProvider`
- [Pydantic AI TypeSafe integration](https://pydantic.dev/docs/ai/models/typesafe/) — a second host-neutral `DecisionModel`, and the source for the 255-option / 10-level hard limits and the no-sampling-knob behavior
- [Hugging Face — How to use the Jev AI model](https://huggingface.co/blog/sora-2/how-to-use-the-jev-ai-model-a-step-by-step-develop) — developer walkthrough for the first-party API

This host is an example only. Any decisions-compatible host plugs in the same
way — change the `BaseUrl` and `DecisionsEndpoint`, keep the same
`IDecisionProvider` contract.
