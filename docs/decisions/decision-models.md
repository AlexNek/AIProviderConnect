# Decision Models

A **decision model** answers typed questions about your application state with
probabilities instead of prose. You send a snapshot of state plus one or more
questions, and the model returns a typed answer per question — a selected
option, a yes/no probability, or a score against a scale — each with its own
confidence and probability distribution.

Decisions are a capability of **every** provider, not a separate provider
family. Any provider that can complete a chat turn answers `DecideAsync` by
default, through a read-out over its own chat surface: the question is rendered
as one chat turn, the option tokens' logprobs are read from the response, and
the distribution is renormalised over the declared options. A provider whose
definition declares a native decisions surface keeps that surface instead, and
the transport is chosen from declared data rather than from code. The capability
is exposed through the same optional-interface shape as streaming, model
discovery, and embeddings:

```csharp
public interface IDecisionProvider
{
    bool SupportsDecisions { get; }

    EDecisionSource DecisionSource { get; }

    Task<DecisionResponse> DecideAsync(
        DecisionRequest request,
        CancellationToken cancellationToken = default);
}
```

`DecisionSource` reports the mechanism that produced the answer:
`EDecisionSource.Readout` for a chat-surface read-out, `EDecisionSource.Native`
for a provider holding a decisions wire endpoint. It lets a caller tell where a
probability came from without knowing which host it is talking to.

Only a `Decision`-rooted provider lacks a chat surface, and only there does
`ChatAsync` throw `AiException(AiErrorCodes.ChatNotSupported)`.

## Capability discovery

Every provider derived from `AIProviderBase` implements `IDecisionProvider`, so
the capability check answers yes without any declaration:

```csharp
if (provider is IDecisionProvider decision && decision.SupportsDecisions)
{
    var response = await decision.DecideAsync(request, ct);
}
```

`SupportsDecisions` reports `false` when the read-out is switched off
(`DecisionReadoutEnabled = false`) on a provider with no native surface. A
native decisions provider reports `true` regardless of the read-out switch,
because its own wire is unaffected.

There is also a **model-level** signal: a model that answers with decisions is
tagged `EModelCapability.Decision`, so it is discoverable *without a call*
through the normal model list. Because a decisions surface and a model catalog
are different endpoints, a `Decision`-rooted provider does not implement model
discovery itself; tag decision models through `OverrideModels` on your
discovery-capable gateway (see
[Model Discovery](../model-discovery/model-discovery.md)).

## Any provider can decide

### A shipped provider, declaring nothing

Select any chat-capable entry from the catalog and pass the model id in the
request — no definition, no `endpoints` entry, no registration callback:

```csharp
services.AddSingleton(new HttpClient());
services.AddAiProviders(b => b
    .Configure<OpenAICompatibleProviderOptions>("huggingface", o =>
    {
        o.ApiKey = "<your-key>";
        o.DefaultModel = "some/chat-model";
    }));

var provider = serviceProvider.GetRequiredKeyedService<IAIProvider>("huggingface");
var decision = (IDecisionProvider)provider;   // every chat-capable provider is one

var response = await decision.DecideAsync(new DecisionRequest
{
    // Any chat model id the account can reach; nothing has to be registered first.
    Model = "another/chat-model",
    State = "The user asked to delete their account.",
    Questions = new Dictionary<string, DecisionQuestion>
    {
        ["route"] = new()
        {
            Kind = EDecisionQuestionKind.Choice,
            Instructions = "Which path should handle this request?",
            Criteria = new Dictionary<string, string>
            {
                ["billing"] = "Handle by the billing agent",
                ["support"] = "Handle by a support agent",
            },
        },
    },
}, ct);
```

### A loopback server, still declaring nothing

A local OpenAI-compatible server needs one thing the hosted example does not —
an address. A loopback host is allowed over plain `http` and derives "no API
key" from that address, so the configuration stays minimal:

```csharp
services.AddAiProviders(b => b
    .Configure<OpenAICompatibleProviderOptions>("lmstudio", o =>
    {
        o.BaseUrl = "http://127.0.0.1:1234/v1/";
        // Derived from the loopback address; set it only to be explicit.
        o.RequiresApiKey = false;
    }));
```

The same registration answers `DecideAsync` for any model the server has
loaded — the model is a request parameter, so a loader change needs no
configuration change.

### Declaring a native decisions surface

Two shapes cover the decisions hosts that ship their own server:

```csharp
// A host that serves the decisions wire at its root.
var systemOne = new ProviderDefinition
{
    Id = "local-systemone",
    DisplayName = "Local decisions host",
    Protocol = EProviderProtocol.Decision,
    BaseUrl = "http://127.0.0.1:8090/",
    RequiresApiKey = false,
};

// A host that serves /api/decide with no /v1/ segment: the path is declared
// relative to the base URL, and no prefix is assumed.
var decideSurface = new ProviderDefinition
{
    Id = "local-decide",
    DisplayName = "Local decide host",
    Protocol = EProviderProtocol.Decision,
    BaseUrl = "http://127.0.0.1:8080/",
    RequiresApiKey = false,
    Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["decisions"] = new EndpointDefinition { Path = "api/decide" },
    },
};

services.AddAiProviders(b => b
    .Add(systemOne)
    .Add(decideSurface)
    .Configure<DecisionProviderOptions>("local-systemone", o => o.DefaultModel = "local-model")
    .Configure<DecisionProviderOptions>("local-decide", o => o.DefaultModel = "local-model"));
```

Both ids register as native providers (`DecisionSource` is `Native`); the second
posts to `http://127.0.0.1:8080/api/decide`. When you configure an existing id
rather than authoring a definition, the typed
`Configure<DecisionProviderOptions>(..., o => o.DecisionsEndpoint = "api/decide")`
is the code equivalent of the `endpoints` entry.

A chat-capable host that also serves decisions declares the surface on the chat
definition instead, as in
[Single-id registration via `endpoints`](#single-id-registration-via-endpoints).

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

## Tuning the read-out

Every read-out member is optional: the defaults are what a provider with no
configuration uses. In a definition's JSON the same values are spelled as
`protocolConfiguration` keys; in code you set the typed member through
`Configure<TOptions>` and never need the key name.

| Typed member | Definition key | Default | When unset |
| --- | --- | --- | --- |
| `DecisionReadoutEnabled` | — | `true` | Every chat-capable provider answers |
| `DecisionReadoutPromptTemplate` | `decisionReadoutPromptTemplate` | `null` | The built-in prompt is rendered from state, instructions, and criteria |
| `DecisionReadoutOptionTokenStyle` | `decisionReadoutOptionTokenStyle` | `uppercaseLetter` | Options render as `A.`, `B.`, …; `lowercaseLetter` gives `a.`, `b.`; `levelIndex` gives `0.`, `1.` |
| `DecisionReadoutTopLogprobs` | `decisionReadoutTopLogprobs` | `20` | 20 alternatives are requested; this is also the read-out's option ceiling |
| `DecisionReadoutBoolTokens` | `decisionReadoutNoulTokens` | `yes,no` | A Noul question reads the `yes` and `no` tokens |
| `DecisionReadoutTemperature` | `decisionReadoutTemperature` | `0` | The read-out call samples at temperature 0 |
| `DecisionsMaxOptions` | `decisionsMaxOptions` | `null` | No ceiling beyond the wire's 255-option limit |

```csharp
services.AddAiProviders(b => b
    .Configure<OpenAICompatibleProviderOptions>("lmstudio", o =>
    {
        o.DecisionReadoutOptionTokenStyle = "lowercaseLetter";
        o.DecisionReadoutTopLogprobs = 20;
        o.DecisionReadoutPromptTemplate =
            "State:\n{state}\n\nQuestion: {instructions}\n\nOptions:\n{options}\n\n" +
            "Answer with one option:";
    })
    .Configure<DecisionProviderOptions>("local-decide", o =>
    {
        // A host whose server caps options below the wire limit.
        o.DecisionsMaxOptions = 20;
    }));
```

A `DecisionReadoutPromptTemplate` uses `{state}` for the state snapshot,
`{instructions}` for the question's instructions, and `{options}` for one line
per option. The option lines already use the configured token style, so
`{options}` is enough for a template that only wraps the list.

Two ceilings refuse a Choice question before any request leaves the process,
with `ai/configuration-error` naming the provider: `DecisionsMaxOptions` is the
host's declared ceiling and applies to both transports, while
`DecisionReadoutTopLogprobs` is the read-out's own ceiling — an option that
cannot appear among the requested alternatives cannot be read. Noul and Score
questions are unaffected by either ceiling.

!!! note "The template is an override, never a precondition"
    A checkpoint whose prompt convention this library has never seen still
    answers through the built-in prompt: the model id is a request parameter,
    and no per-model prompt text is required for support. Per-model text is
    tuning, not registration.

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
as OpenRouter does. All other protocol parsers leave `Cost` unset, and a
read-out leaves it `null` because chat completions carry no decisions cost
field.

## Error codes

| Code | Meaning |
| --- | --- |
| `ai/chat-not-supported` | `ChatAsync` was called on a `Decision`-rooted provider, which has no chat surface |
| `ai/decision-not-supported` | A decision call was requested on a provider whose read-out is switched off (`DecisionReadoutEnabled = false`) and that has no native decisions surface |
| `ai/configuration-error` | Null state, empty questions, an unsupported state shape, a question that violates its per-kind limits, or a question whose options exceed `DecisionsMaxOptions` / `DecisionReadoutTopLogprobs` |
| `ai/decision-readout-unavailable` | The chat response carried no `logprobs` block — the host's API does not expose token probabilities |
| `ai/decision-readout-unreadable` | None of the declared option tokens appeared among the reported distributions |
| `ai/invalid-request` | The effective model resolved empty (no request model, no override, no `DefaultModel`) |

Rate limits, cancellation, retry, and network-error classification behave
exactly as they do for chat — see [Error Handling](../concepts/error-handling.md).

## When to use a decision model

A decision model is a cheap, fast decision layer inside an agent or application
loop — model routing, tool-call risk gating, auto-mode safety checks, parallel
multi-question classification about one state. It is used **alongside** a
generative LLM, not instead of one: the generative model produces content, the
decision model judges it.

With a native decisions host, multiple questions over one state are evaluated
in parallel at low marginal cost, so a single call can answer "is this safe?",
"which tool?", and "how urgent?" together. A read-out answers the same set one
chat turn per question.

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

### Read-out probabilities are sampled, not calibrated

A read-out answer comes from the sampled token distribution at the answer
position: the option tokens the model reported there, renormalised with softmax
over the declared options. It is not the decisions host's calibrated output, and
it inherits whatever the chat model does with the prompt. An option absent from
the reported alternatives contributes zero; a response with no `logprobs` block
at all fails with `ai/decision-readout-unavailable` rather than fabricating a
uniform distribution.

The read-out issues one chat turn per question, in enumeration order, and
aborts on the first failure. Usage is summed across those calls and `Cost` is
`null`, because a chat response carries no decisions cost field.

Not every chat API carries token probabilities. Of the shipped chat protocols,
`OpenAICompatible` and `HybridGateway` (`logprobs` / `top_logprobs`) and
`KeyQuery` (`generationConfig.responseLogprobs`) carry them; `MessagesApi`
(`AnthropicCompatible`) has no logprob field, so a read-out against it fails
with `ai/decision-readout-unavailable`.

Calibration does not travel with the format. A checkpoint run behind a local
server speaks the same wire as a hosted one, but it is not the hosted model: the
library passes the host's probabilities through untouched, and calibration,
option ceilings, and prompt fit are the operator's responsibility. A threshold
tuned against one host is not evidence about another.

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

The dedicated-id pattern above remains the choice when the decision host is a
different service. A chat provider that must not expose decisions keeps its
single id and sets `DecisionReadoutEnabled = false` instead.

## When your host is out of scope

If a host ships something other than the decisions wire — its own path, its own
request body, its own answer shape — that is a transport of its own, not a
read-out variant. The ladder from declaring nothing to authoring a provider is
described in
[Writing your own transport](../concepts/wire-protocols.md#writing-your-own-transport).

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
