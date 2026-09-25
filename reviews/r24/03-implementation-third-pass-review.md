# Refactor 24 — Third-Pass Implementation Review

## Scope

Third review pass of the refactor 24 implementation. This pass re-verifies every
finding from the two previous reviews (`01-implementation-review.md` and
`02-implementation-second-pass-review.md`) against the current codebase state, and
searches for issues both prior passes missed.

All previous findings remain **unresolved**. One new documentation finding is added.

## Previous-finding verification

| # | Finding | Status | Evidence |
|---|---------|--------|----------|
| R1-1 | DRY: identical auth-header logic in `MessagesApiProvider.ApplyHeaders` (lines 63-73) and `KeyQueryProvider.ConfigureHeaders` (lines 66-76) | **Open** | Both methods are still character-for-character identical: trim key, null-guard, resolve `CustomAuthHeaderName` or throw, add header. |
| R1-2 | Dead data: `chatEndpoint` and `modelsEndpoint` in `gemini.json` (lines 19, 24) | **Open** | `KeyQueryOptions` is an empty class; `SeedFromDefinition` does not seed these for KeyQuery; `KeyQueryProvider` hardcodes `EndpointDefaults.KeyQuery.GenerateContent`. |
| R1-3 | `ModelCatalogProvider` hardcodes `EndpointDefaults.Catalog.*` endpoints (lines 25-27), ignoring `chatEndpoint`/`modelsEndpoint` seeded from `github-models.json` into `OpenAICompatibleProviderOptions` | **Open** | `ModelCatalogProvider.ChatEndpoint => EndpointDefaults.Catalog.Inference` instead of reading from options. Compare with `HybridGatewayProvider` and `OpenAICompatibleProvider` which correctly read from `_options`. |
| R1-4 | Stale doc: `CatalogOptions` reference in `features/10/custom-provider-registration.md` line 82 | **Open** | Line still reads: `CatalogOptions`, `KeyQueryOptions`: seed `BaseUrl` only. |
| R1-5 | Stale doc: `Organization` reference in `docs/concepts/wire-protocols.md` line 65 | **Open** | Line still reads: `orgs/{Organization}/inference/chat/completions` when `Organization` is set. |
| R1-6 | No test coverage for `ApplyProtocolConfiguration` or `ProtocolConfiguration` seeding | **Open** | Grep for `ApplyProtocolConfiguration` and `ProtocolConfiguration` across `AIProviderConnectLib.Tests` returns zero matches. |
| R2-F1 | `KeyValueEditorControl.xaml` line 29: Remove button binding `DataContext.RemoveCommand` resolves to ViewModel (which has `RemoveProtocolConfigEntryCommand`, not `RemoveCommand`) | **Open** | Add button (line 37) correctly binds via `RelativeSource AncestorType=UserControl`; Remove button does not follow the same pattern. |
| R2-F2 | Five DI registration tests fail (pre-existing from refactor 23) | **Open** (pre-existing) | Not caused by refactor 24; carried forward. |
| R2-F3 | No `MessagesApiProviderTests` class exists | **Open** | `Providers/` test folder still has no dedicated test class for `MessagesApiProvider`. |

## New finding

### N1 — Stale doc: `AuthType` and `Organization` listed as "do not seed" in features/10

**Severity**: Low
**File**: `features/10/custom-provider-registration.md` line 83

```
- Do not seed `ApiKey`, `Enabled`, `DefaultModel`, `DefaultHeaders`, `DefaultMaxTokens`, `AuthType`, `Organization` — these have no catalog source and remain consumer-supplied.
```

Both `AuthType` and `Organization` no longer exist anywhere in the library code:
- `Organization` was a property on the deleted `CatalogOptions`.
- `AuthType` has been removed from the options hierarchy entirely.

Listing deleted properties in a "do not seed" statement is misleading — it suggests
these are live properties that are intentionally excluded from seeding, rather than
properties that no longer exist.

**Fix**: Remove `AuthType` and `Organization` from the list:

```
- Do not seed `ApiKey`, `Enabled`, `DefaultModel`, `DefaultHeaders`, `DefaultMaxTokens` — these have no catalog source and remain consumer-supplied.
```

## Issues not flagged (verified correct)

| Aspect | Verdict |
|--------|---------|
| DI ordering: `SeedFromDefinition` runs before `ApplyProtocolConfiguration` | Correct — `Configure` delegates are additive and execute in registration order. |
| `CatalogWireProtocol.ApplyProtocolConfiguration` accepts `AIProviderOptions` (base type) | Correct — only needs `DefaultHeaders` and `ProtocolConfiguration`, both on the base. DI passes `OpenAICompatibleProviderOptions` (subtype), which is safe. |
| `ProtocolConfiguration` property on `ProviderDefinition` and `AIProviderOptions` | Correctly typed as `IReadOnlyDictionary<string, string>?` with proper JSON attribute. |
| `CustomAuthHeaderName` on `AIProviderOptions` | Correctly placed on the base class; protocol files write, providers read. |
| `AnthropicVersion` fully removed from `MessagesApiOptions` | No references remain outside the protocol file's `private const`. |
| Dead constants removed from `HeaderNames` and `MediaTypes` | Only `Accept` and image media types remain. |
| `CatalogOptions` class deleted | No references in code or tests. |
| `OrgInference` constant removed from `EndpointDefaults` | No references remain. |
| Protocol files own key names as `private const` | Verified: `MessagesApiProtocol`, `KeyQueryWireProtocol`, `CatalogWireProtocol` each define keys internally. |
| `anthropic.json`, `gemini.json`, `github-models.json` use `protocolConfiguration` correctly | Verified — provider-specific values are in the dictionary, not as top-level properties. |
| `opencode-zen.json` and `opencode-go.json` dead fields removed | `anthropicModelPrefixes`, `messagesEndpoint`, `authenticationStyle`, `apiKeyHeaderName`, `apiKeyPrefix`, `apiVersion` are all gone. |
| Library builds with zero warnings | Verified. |

## Consolidated issue summary

All issues from both prior reviews remain open. One new low-severity documentation
issue was found.

| # | Issue | Severity | Category | Origin |
|---|-------|----------|----------|--------|
| 1 | DRY: identical auth-header logic in `MessagesApiProvider` + `KeyQueryProvider` | Medium | DRY | R1 |
| 2 | Dead data: `chatEndpoint`/`modelsEndpoint` in `gemini.json` | Medium | Dead data | R1 |
| 3 | `ModelCatalogProvider` hardcodes endpoints, ignoring seeded JSON values | Medium | Design | R1 |
| 4 | Stale doc: `CatalogOptions` in `features/10` line 82 | Low | Documentation | R1 |
| 5 | Stale doc: `Organization` in `wire-protocols.md` line 65 | Low | Documentation | R1 |
| 6 | No test coverage for `ApplyProtocolConfiguration` / `ProtocolConfiguration` seeding | High | Testing | R1 |
| 7 | `KeyValueEditorControl` Remove button binding targets non-existent property | Medium | Bug (XAML) | R2 |
| 8 | Five DI registration tests fail (pre-existing from refactor 23) | Medium | Testing (pre-existing) | R2 |
| 9 | No `MessagesApiProviderTests` class exists | Low | Testing gap | R2 |
| N1 | Stale doc: `AuthType` and `Organization` in `features/10` line 83 | Low | Documentation | New |
