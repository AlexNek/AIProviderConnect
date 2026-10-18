# Wire Protocols

AIProviderConnect does not have one class per provider. It has a small set of
**protocol implementations** covering the supported wire protocols, and each
catalog provider is dispatched to one of them based on its `protocol` field.
The four chat-family implementations below share the same chat/streaming/
discovery transport; `DecisionProvider` serves the decisions wire natively.
On top of that, **decisions are a capability of every provider**: one whose
definition declares no native decisions surface answers `DecideAsync` through a
read-out over its own chat surface. The transport is chosen from declared data,
and the read-out is the default.

## Protocol Mapping

The catalog JSON stores a protocol string, mapped by
`ProviderProtocolMapper` (case-insensitive) to `EProviderProtocol`:

| JSON value | `EProviderProtocol` | Implementation | Catalog entries |
| --- | --- | --- | --- |
| `OpenAICompatible` | `OpenAICompatible` | `OpenAICompatibleProvider` | openai, ollama, groq, mistral, xai, deepseek, openrouter, and more |
| `AnthropicCompatible` | `MessagesApi` | `MessagesApiProvider` | anthropic |
| `GeminiCompatible` | `KeyQuery` | `KeyQueryProvider` | gemini |
| `GitHubModelsCompatible` | `Catalog` | `ModelCatalogProvider` | *(no embedded entry — protocol exists for consumer-supplied providers)* |
| `HybridGateway` | `HybridGateway` | `OpenAICompatibleProvider` | opencode-go, opencode-zen |
| `decision` | `Decision` | `DecisionProvider` | *(no embedded entry — protocol exists for consumer-supplied decision providers)* |
| `decisionReadout` | `DecisionReadout` | *(no separate class — the root provider's read-out)* | *(no embedded entry — an `endpoints` override declaring the read-out on a chat-capable root)* |

Unknown or empty protocol strings throw `JsonException`.
`EProviderProtocol.Native` exists in the enum but no catalog entry maps to
it today. `DecisionReadout` is only meaningful as a `decisions` endpoint
override: it is an explicit declaration of the read-out, not the switch that
enables decisions.

## Per-Operation Endpoint Overrides

A provider definition may carry an optional `endpoints` block that overrides
individual operations without changing the wire protocol:

```json
"endpoints": {
  "decisions": {
    "path": "alpha/decisions",
    "baseUrl": "https://openrouter.ai/api/",
    "protocol": "decision"
  }
}
```

- `path` — the relative path, always resolved against the effective base URL.
- `baseUrl` — an override used **only** when the surface genuinely sits on a
  different root than the definition's common base (OpenRouter serves chat
  under `/api/v1/` but decisions under `/api/`).
- `protocol` — an override for operations served with a different wire
  protocol than the definition's root protocol (e.g. `decision` under an
  `OpenAICompatible` provider). Keys are matched case-insensitively; the known
  operation keys are `chat`, `models`, `messages`, `embeddings`, `decisions`.
- `protocol: "decisionReadout"` on a `decisions` entry declares the read-out as
  the decisions surface of a chat-capable root. It is accepted only where the
  root has a chat surface, and it changes no runtime behaviour — the read-out
  is already the default. See [Decisions read-out](#decisions-read-out).

The resolution precedence for each operation is:

1. the option defaults,
2. the legacy flat definition field (`chatEndpoint`, `modelsEndpoint`,
   `messagesEndpoint`),
3. the `endpoints` entry (`path`, and for `decisions` and `embeddings` also `baseUrl`),
4. a consumer's `Configure<TOptions>(providerId, ...)` call.

Each later step wins over the earlier ones. An operation that lives in the
`endpoints` block owns its wire path: the editor and manifest serializer do not
write the legacy flat member for it, so a migrated manifest carries each
operation exactly once. Registration-time validation
rejects an entry that is an absolute URL in `path` (use `baseUrl` for a
surface on another root), a non-HTTPS `baseUrl` on a routable host (a loopback
address may use `http`), an unknown operation key, an entry that changes
nothing, a `decisionReadout` entry whose root has no chat surface, and a
`decisions` override the root protocol cannot serve.

When an `OpenAICompatible` or `HybridGateway` provider declares a `decisions`
entry with `protocol: "decision"`, registration selects the combined
`OpenAICompatibleDecisionProvider` — one provider id that serves chat,
streaming, model discovery, embeddings, **and** decisions over its own
transport (see [Dependency Injection](../getting-started/dependency-injection.md)).

## OpenAICompatibleProvider

Speaks the OpenAI chat completions dialect (`OpenAICompatibleWireProtocol`):

- `POST {BaseUrl}{ChatEndpoint}` (default `chat/completions`) with
  `Authorization: Bearer {ApiKey}` and optional `DefaultHeaders`.
- `POST {BaseUrl}{EmbeddingsEndpoint}` (default `embeddings`) for embeddings
  via `IEmbeddingProvider` — see [Embeddings](../chat/embeddings.md).
- `GET {BaseUrl}{ModelsEndpoint}` (default `models`) for discovery.
- Streaming requests set `stream: true` and parse server-sent events.
- Extra options: `ChatEndpoint`, `ModelsEndpoint`, `EmbeddingsEndpoint`,
  `DefaultEmbeddingModel`.

## MessagesApiProvider

Speaks the Anthropic Messages dialect (`MessagesApiProtocol`):

- `POST {BaseUrl}{MessagesEndpoint}` (default `messages`).
- Authentication via a configurable API-key header (`CustomAuthHeaderName`,
  sourced from `protocolConfiguration.apiKeyHeaderName`). The Anthropic provider
  also sends an `anthropic-version` header sourced from the provider JSON
  manifest's `protocolConfiguration.anthropicVersion` via
  `MessagesApiProtocol.ApplyProtocolConfiguration`.
- The Messages API requires `max_tokens`; callers must set
  `ChatCompletionRequest.MaxTokens` explicitly. The API rejects requests
  that omit it with a clear error.
- `EChatRole.System` messages are folded into the top-level `system` field.

## KeyQueryProvider

Speaks the Google-style model-scoped dialect:

- Authentication uses a configurable API-key header (`CustomAuthHeaderName`,
  sourced from `protocolConfiguration.apiKeyHeaderName`); the key is not placed in the URL.
- `BaseUrl` is normalized to include a trailing slash for every operation.
- `POST {BaseUrl}models/{model}:generateContent`
- Streaming: `POST {BaseUrl}models/{model}:streamGenerateContent?alt=sse`
- Discovery: `GET {BaseUrl}models`; model names are stripped
  of the `models/` prefix.
- Request body uses `contents`/`parts` with a separate `systemInstruction`
  for system messages.

## ModelCatalogProvider

Speaks a GitHub-Models-style catalog dialect:

- Inference: `POST {BaseUrl}chat/completions`
  (OpenAI-compatible request body).
- Discovery: `GET {BaseUrl}models`.
- Headers: `Authorization: Bearer {ApiKey}`,
  `Accept: application/vnd.github+json`, and `X-GitHub-Api-Version`
  sourced from the provider JSON manifest's `protocolConfiguration.apiVersion`
  via `CatalogWireProtocol.ApplyProtocolConfiguration`.

## HybridGateway protocol

Same OpenAI-compatible wire format as `OpenAICompatibleProvider`, for
gateways that multiplex multiple backends. Uses Bearer authentication.
The `HybridGateway` protocol is served by `OpenAICompatibleProvider`
internally; there is no separate provider class.

## DecisionProvider

Speaks the decisions dialect (`DecisionsWireProtocol`) natively. It is the only
provider with no chat surface:

- `POST {BaseUrl}{DecisionsEndpoint}` (default `alpha/decisions`) with
  `Authorization: Bearer {ApiKey}`, sending `{ model, state, questions }`.
- `DecisionsBaseUrl` overrides the origin when the decisions surface lives
  elsewhere than the provider `BaseUrl`.
- `ChatAsync` throws `ai/chat-not-supported`, and the provider does not
  implement model discovery. It reports `DecisionSource` `Native`.
- The request is validated against per-question limits before sending; an
  over-limit request throws `ai/configuration-error`.

See [Decision Models](../decisions/decision-models.md).

## Decisions read-out

Every provider that completes a chat turn answers `DecideAsync` without any
declaration: the question is rendered into one chat turn, the option tokens'
logprobs are read from the response, and the distribution is renormalised over
the declared options. The read-out therefore needs a chat surface; a
`Decision`-rooted provider has none and serves the decisions wire instead.

- One chat call per question, in enumeration order; the first failure aborts.
- Sends `logprobs: true` and `top_logprobs` with the chat call.
  `DecisionReadoutTopLogprobs` (default 20) is both the requested count and the
  read-out's option ceiling.
- Reports `DecisionSource` `Readout`; a native decisions provider reports
  `Native`, and the model-override decorators forward whichever value the inner
  provider reports.
- A chat API with no logprob field fails with
  `ai/decision-readout-unavailable` rather than inventing a distribution.
- Switch it off per provider with `DecisionReadoutEnabled = false`; the call
  then fails with `ai/decision-not-supported`.

See [Decision Models](../decisions/decision-models.md#any-provider-can-decide)
for configuration, and [Tuning the read-out](../decisions/decision-models.md#tuning-the-read-out)
for the per-provider overrides.

## Extending with OpenAICompatibleProviderBase

`OpenAICompatibleProviderBase` is a `public abstract` class that consolidates
the OpenAI-compatible chat/stream/discovery transport. Subclasses only need
to supply `ChatEndpoint`, `ModelsEndpoint`, and optionally override
`ConfigureHeaders` and `ParseModels`. It is the intended extension point for
third-party providers that speak the OpenAI wire format with bespoke auth or
endpoint selection.

!!! note "Per-call credentials use the two-argument `ConfigureHeaders`"
    `ConfigureHeaders` has two overloads. A call that carries a per-request API
    key invokes `ConfigureHeaders(request, apiKey)` with the effective key; the
    one-argument form is used only when the call supplies no key override, and
    it delegates to the two-argument form with `Options.ApiKey`. A subclass that
    adds bespoke headers must therefore override
    `ConfigureHeaders(request, apiKey)` — overriding only the one-argument form
    would be bypassed for credential-carrying calls. See
    [Runtime Credentials](runtime-credentials.md).

## Custom Headers

Every protocol sends the headers in `AIProviderOptions.DefaultHeaders` with
each request. Protocol-specific headers (such as `anthropic-version` for
MessagesApi or `X-GitHub-Api-Version` for Catalog) are populated
automatically from the provider manifest's `protocolConfiguration`. To add
headers that the library does not set itself, configure `DefaultHeaders` at
runtime:

```csharp
// Anthropic multi-workspace API key
services.Configure<MessagesApiOptions>("anthropic", options =>
{
    options.DefaultHeaders["anthropic-workspace-id"] = "wrkspc_...";
});
```

This works for any provider and any header. The values are merged with the
protocol-managed headers already in `DefaultHeaders`, so existing entries
(such as `anthropic-version`) are preserved.

## Common Behavior

Every implementation:

- Extends `AIProviderBase` and implements `IAIProvider`. The four chat-family
  providers also implement `IStreamingChatProvider` and
  `IModelDiscoveryProvider`, and every provider implements `IDecisionProvider`:
  chat-family providers through the read-out, `DecisionProvider` through the
  native wire.
- Runs the same pre-flight checks (enabled, base URL, API key) before any
  HTTP call — see [Error Handling](error-handling.md).
- Resolves its `ProviderDefinition` from the catalog by `ProviderId` at
  construction time; an unknown ID throws `InvalidOperationException`.

## Writing your own transport

A host that speaks neither the chat wire nor the decisions wire is served at
one of three tiers; the cheapest tier that fits is the right one:

1. **Nothing** — if the host is chat-compatible, select it (or register a
   definition with `OpenAICompatible`) and let the read-out answer decisions.
   No type is written.
2. **Declared data** — if the host serves the decisions wire on its own path or
   origin, declare it: `Protocol = Decision` for a decisions-only service, or a
   `decisions` entry with `protocol: "decision"` under a chat-capable
   definition. Base URL, path, and key requirement are definition data — see
   [Decision Models](../decisions/decision-models.md#any-provider-can-decide).
3. **An authored provider** — if the request or response body differs, write a
   class deriving `AIProviderBase`, register its definition with
   `Protocol = Native`, and register the class with
   `AddProvider<TProvider>(id, sp => new TProvider(...))`.

An authored class is built from the same public and `protected` surface the
shipped providers use:

- The `protected` members of `AIProviderBase` — `Definition`, `Options`,
  `ProviderId`, `HttpClient`, `Logger`, `ResolveCredentialsAsync`,
  `ResolveSurfaceBaseUrl`, `SendDecisionAndParseAsync`, and the rest of the
  shared transport and header helpers.
- The `public` wire maps — `DecisionsWireProtocol.MapRequest` and
  `ParseResponse` (and their chat, embeddings, and catalog counterparts) when
  the host's body matches the documented shape.
- `public static SeedFromDefinition(AIProviderOptions, ProviderDefinition)` —
  seeds base URL, endpoint paths, the declared `requiresApiKey` value, the
  read-out members, and `protocolConfiguration` exactly as the built-in
  registration does. A `Native` id is skipped by the automatic registration
  loop, so the provider's own factory is the one place the definition can be
  applied.
