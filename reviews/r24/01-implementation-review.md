# Refactor 24 — Implementation Review

## Scope

Review of the refactor 24 implementation against the design doc
(`messages-api-options-provider-specific-removal.md`) and the implementation checklist.
Focus: correctness, SOLID/clean-code quality, DRY violations, dead data, missing tests.
Nitpick issues are excluded.

## Overall verdict

The core refactor is **correctly implemented**. The `ProtocolConfiguration` dictionary
was added to `ProviderDefinition` and `AIProviderOptions`; `AnthropicVersion` was removed
from `MessagesApiOptions`; the `anthropic-version` header was removed from
`MessagesApiProvider.ApplyHeaders`; protocol files own their key names as `private const`;
`SeedFromDefinition` copies the dictionary generically; DI registration calls each
protocol's `ApplyProtocolConfiguration`; dead constants were removed from `HeaderNames`
and `MediaTypes`; `CatalogOptions` was deleted; the `KeyValueEditorControl` was extracted;
docs were mostly updated.

However, several issues remain.

## Issue 1 — DRY violation: identical auth-header logic in two providers

**Severity**: Medium
**Files**: `MessagesApiProvider.ApplyHeaders`, `KeyQueryProvider.ConfigureHeaders`

Both providers contain the same six-line auth-header method:

```csharp
// MessagesApiProvider.ApplyHeaders (lines 63-73)
private void ApplyHeaders(HttpRequestMessage request)
{
    var trimmedKey = _options.ApiKey?.Trim();
    if (!string.IsNullOrWhiteSpace(trimmedKey))
    {
        var headerName = _options.CustomAuthHeaderName
            ?? throw new InvalidOperationException(
                $"Provider '{_options.ProviderId}' uses API-key auth but no 'apiKeyHeaderName' is configured in protocolConfiguration.");
        request.Headers.TryAddWithoutValidation(headerName, trimmedKey);
    }
}

// KeyQueryProvider.ConfigureHeaders (lines 66-76) — identical body
```

The logic is: trim key, null-guard, resolve `CustomAuthHeaderName` or throw, add header.
This is a textbook DRY violation — the same behavior implemented in two places.

**Fix**: Extract a shared helper into `AIProviderBase`:

```csharp
protected static void ApplyCustomAuthHeader(HttpRequestMessage request, AIProviderOptions options)
{
    var trimmedKey = options.ApiKey?.Trim();
    if (!string.IsNullOrWhiteSpace(trimmedKey))
    {
        var headerName = options.CustomAuthHeaderName
            ?? throw new InvalidOperationException(
                $"Provider '{options.ProviderId}' uses API-key auth but no 'apiKeyHeaderName' is configured in protocolConfiguration.");
        request.Headers.TryAddWithoutValidation(headerName, trimmedKey);
    }
}
```

Both providers then call `ApplyCustomAuthHeader(request, _options)` instead of
duplicating the logic.

## Issue 2 — Dead data in `gemini.json`: `chatEndpoint` and `modelsEndpoint`

**Severity**: Medium
**File**: `ai-providers/gemini.json`

`gemini.json` contains:

```json
"chatEndpoint": "chat/completions",
"modelsEndpoint": "models"
```

These fields are deserialized onto `ProviderDefinition` but never reach runtime:

- `KeyQueryOptions` is an empty class — it has no `ChatEndpoint` or `ModelsEndpoint`.
- `SeedFromDefinition` does not seed these for `KeyQueryOptions` (the switch falls
  through both arms).
- `KeyQueryProvider` uses hardcoded `EndpointDefaults.KeyQuery.GenerateContent` and
  `EndpointDefaults.Models` patterns.

The design doc rule states: *"Do not leave dead data in JSON files — remove them."*
These fields suggest configurability that does not exist.

**Fix**: Remove `chatEndpoint` and `modelsEndpoint` from `gemini.json`.

## Issue 3 — Dead data in `github-models.json`: `chatEndpoint` and `modelsEndpoint` are silently ignored

**Severity**: Medium
**File**: `ai-providers/github-models.json`, `ModelCatalogProvider.cs`

`github-models.json` contains:

```json
"chatEndpoint": "inference/chat/completions",
"modelsEndpoint": "catalog/models"
```

These are deserialized into `ProviderDefinition` and seeded into
`OpenAICompatibleProviderOptions` (which implements `IChatAndModelsEndpointOptions`).
However, `ModelCatalogProvider` ignores the seeded values:

```csharp
// ModelCatalogProvider.cs lines 25-27
protected override string ChatEndpoint => EndpointDefaults.Catalog.Inference;
protected override string ChatEndpoint => EndpointDefaults.Catalog.ModelList;
```

The provider hardcodes the endpoints from `EndpointDefaults.Catalog`, making the JSON
values silently ignored. If a user changes the JSON values, nothing changes at runtime.
This contradicts the refactor's goal: *"All provider-specific configuration must come
from the provider JSON files."*

**Fix**: Either (a) make `ModelCatalogProvider` read from `_options.ChatEndpoint` /
`_options.ModelsEndpoint` like `OpenAICompatibleProvider` and `HybridGatewayProvider`
do, or (b) remove `chatEndpoint` and `modelsEndpoint` from `github-models.json` and
document that Catalog providers use hardcoded defaults. Option (a) is preferred because
it aligns with the refactor's goal.

## Issue 4 — Stale documentation: `CatalogOptions` reference in features/10

**Severity**: Low
**File**: `features/10/custom-provider-registration.md` line 82

```
- `CatalogOptions`, `KeyQueryOptions`: seed `BaseUrl` only.
```

`CatalogOptions` was deleted in this refactor. The line should read:

```
- `KeyQueryOptions`: seed `BaseUrl` only.
- Catalog uses `OpenAICompatibleProviderOptions` (seeds `BaseUrl`, `ChatEndpoint`, `ModelsEndpoint`).
```

## Issue 5 — Stale documentation: `Organization` reference in wire-protocols.md

**Severity**: Low
**File**: `docs/concepts/wire-protocols.md` lines 64-66

```
- Inference: `POST {BaseUrl}inference/chat/completions`, or
  `orgs/{Organization}/inference/chat/completions` when `Organization` is set
  (OpenAI-compatible request body).
```

`Organization` was a property on the deleted `CatalogOptions`. The alternative path
is no longer possible. The doc should be updated to remove the `Organization` branch:

```
- Inference: `POST {BaseUrl}inference/chat/completions`
  (OpenAI-compatible request body).
```

## Issue 6 — No test coverage for `ApplyProtocolConfiguration` or `ProtocolConfiguration` seeding

**Severity**: High
**Files**: `AIProviderConnectLib.Tests` (missing tests)

The core new mechanism of refactor 24 has zero test coverage:

- No test verifies that `SeedFromDefinition` copies `ProtocolConfiguration` from
  `ProviderDefinition` to `AIProviderOptions`.
- No test verifies that `MessagesApiProtocol.ApplyProtocolConfiguration` correctly
  reads `anthropicVersion` → `DefaultHeaders["anthropic-version"]` and
  `apiKeyHeaderName` → `CustomAuthHeaderName`.
- No test verifies that `KeyQueryWireProtocol.ApplyProtocolConfiguration` correctly
  reads `apiKeyHeaderName` → `CustomAuthHeaderName`.
- No test verifies that `CatalogWireProtocol.ApplyProtocolConfiguration` correctly
  reads `apiVersion` → `DefaultHeaders["X-GitHub-Api-Version"]` and
  `accept` → `DefaultHeaders["Accept"]`.
- No test verifies the null-guard behavior when `ProtocolConfiguration` is null.
- No integration test verifies the end-to-end flow: embedded JSON → provider definition
  → seeded options → applied protocol configuration → HTTP headers on a request.

The existing provider tests (`KeyQueryProviderTests`, `ModelCatalogProviderTests`) set
`CustomAuthHeaderName` directly on the options, bypassing the protocol configuration
mechanism entirely. They verify the provider behavior but not the wiring.

**Fix**: Add tests for:
1. `MessagesApiProtocolTests.ApplyProtocolConfiguration_SetsAnthropicVersionHeader`
2. `MessagesApiProtocolTests.ApplyProtocolConfiguration_SetsCustomAuthHeaderName`
3. `MessagesApiProtocolTests.ApplyProtocolConfiguration_NullDictionary_NoOp`
4. `KeyQueryWireProtocolTests.ApplyProtocolConfiguration_SetsCustomAuthHeaderName`
5. `CatalogWireProtocolTests.ApplyProtocolConfiguration_SetsApiVersionHeader`
6. `CatalogWireProtocolTests.ApplyProtocolConfiguration_SetsAcceptHeader`
7. `AIProviderRegistrationBuilderTests.Seeding_CopiesProtocolConfiguration_FromDefinition`
8. An integration test verifying Anthropic provider sends `anthropic-version` header
   from embedded JSON through the full DI pipeline.

## Summary

| # | Issue | Severity | Category |
|---|-------|----------|----------|
| 1 | DRY: identical auth-header logic in MessagesApiProvider + KeyQueryProvider | Medium | DRY |
| 2 | Dead data: `chatEndpoint`/`modelsEndpoint` in `gemini.json` | Medium | Dead data |
| 3 | Dead data: `chatEndpoint`/`modelsEndpoint` in `github-models.json` ignored by hardcoded provider | Medium | Dead data / Design |
| 4 | Stale doc: `CatalogOptions` reference in `features/10` | Low | Documentation |
| 5 | Stale doc: `Organization` reference in `wire-protocols.md` | Low | Documentation |
| 6 | No test coverage for `ApplyProtocolConfiguration` or `ProtocolConfiguration` seeding | High | Testing |
