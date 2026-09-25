# Refactor 24 — Fourth-Pass Implementation Review

## Scope

Fourth review pass of the refactor 24 implementation
(`refactors/refactor24/messages-api-options-provider-specific-removal.md`), verifying
the current codebase state, re-checking every open finding from passes 01–03, searching
for issues the prior passes missed, and identifying the root cause of the five failing
`AIProviderRegistrationBuilderTests`.

Evidence gathered in this pass:

- Read: `MessagesApiProtocol.cs`, `KeyQueryWireProtocol.cs`, `CatalogWireProtocol.cs`,
  `AIProviderOptions.cs`, `MessagesApiOptions.cs`, `KeyQueryOptions.cs`,
  `ProviderDefinition.cs`, `MessagesApiProvider.cs`, `KeyQueryProvider.cs`,
  `ModelCatalogProvider.cs`, `AIProviderBase.cs`,
  `AIProviderServiceCollectionExtensions.cs`, `AIProviderRegistrationBuilder.cs`,
  `CustomProviderDefinitionValidator.cs`, `HeaderNames.cs`, `MediaTypes.cs`,
  `EndpointDefaults.cs`, `KeyValueEditorControl.xaml(.cs)`,
  `ProviderManualEditorViewModel.cs`, `ProviderManifestSerializer.cs`,
  `anthropic.json`, `gemini.json`, `github-models.json`, `opencode-zen.json`,
  `opencode-go.json`.
- Grep: `AnthropicVersion|CatalogOptions|XApiKey|XGoogApiKey|CatalogVendorJson|OrgInference|
  authenticationStyle|apiKeyPrefix|anthropicModelPrefixes|HeaderNames.(MessagesApiVersion|CatalogApiVersion)`
  and `apiKeyHeaderName|anthropicVersion|anthropic-version|CustomAuthHeaderName|
  ApplyProtocolConfiguration|ProtocolConfiguration` across the solution.
- Commands: `dotnet build AIProviderConnectLib.csproj` — success, no warnings.
  `dotnet test AIProviderConnectLib.Tests` — **232 passed, 5 failed** (all in
  `AIProviderRegistrationBuilderTests`).

## Verdict

The mechanical body of the refactor is correctly in place: all plan phases 1–10 have
landed. However, **all nine open findings from passes 01–03 remain unresolved**, and
this pass adds four new findings. Two of the new findings are direct violations of the
plan's own "Do not" rules — protocol-specific knowledge still lives inside provider
classes.

## Plan compliance (verified present)

| Rule | Status |
|------|--------|
| `ProtocolConfiguration` on `ProviderDefinition` + `AIProviderOptions`, generic copy in `SeedFromDefinition` (L145) | ✅ |
| `AnthropicVersion` removed from `MessagesApiOptions` | ✅ no references outside the protocol file's `private const` |
| `anthropic-version` header line removed from `MessagesApiProvider.ApplyHeaders` | ✅ |
| `ApplyProtocolConfiguration` in `MessagesApiProtocol` / `KeyQueryWireProtocol` / `CatalogWireProtocol`, keys as `private const` | ✅ |
| DI invokes each protocol's apply after seeding (`RegisterProvider` L198/210/217; Configure delegates run in registration order, consumer builder callbacks last) | ✅ |
| `HeaderNames` reduced to `Accept`; provider-specific header/media-type constants deleted | ✅ |
| `CatalogOptions` deleted; `ModelCatalogProvider` uses `OpenAICompatibleProviderOptions`; `ConfigureHeaders` override removed; `OrgInference` removed | ✅ |
| JSON: `anthropic.json`/`gemini.json`/`github-models.json` carry `protocolConfiguration`; opencode dead fields removed | ✅ |
| `KeyValueEditorControl` extracted and used in `ProviderManualEditorControl.xaml` (L282) | ⚠️ present but Remove button is non-functional — see Issue 5 |

## New findings

### 1 — Provider classes carry protocol-specific key-name literals (violates plan "Do not" rules)

**Severity**: Medium-High
**Files**: `AIProviderConnectLib/Providers/MessagesApiProvider.cs` (L68–70),
`AIProviderConnectLib/Providers/KeyQueryProvider.cs` (L71–73)

The plan states: *"Do not put provider-specific key name literals in common code — key
names are defined as private constants inside protocol files and used only there"* and
*"Do not give providers protocol-specific knowledge — providers read only generic
properties."* Both providers nevertheless contain the identical message:

```csharp
$"Provider '{_options.ProviderId}' uses API-key auth but no 'apiKeyHeaderName' is configured in protocolConfiguration."
```

`apiKeyHeaderName` and `protocolConfiguration` are protocol-file/JSON vocabulary that
must not appear in provider code. The literal currently exists in exactly one other
place each — as `private const` in `MessagesApiProtocol`/`KeyQueryWireProtocol` and as
the `SeedFromDefinition` copy line — so the providers are the only violators.

**Fix**: fold this into Issue 2's base-class helper and make the message reference only
the generic property: `Provider '{id}' uses API-key auth but options.CustomAuthHeaderName is not set.`

### 2 — DRY: identical auth-header block and byte-identical error string in two providers (carried, R1-1)

**Severity**: Medium
**Files**: `MessagesApiProvider.ApplyHeaders` (L63–73), `KeyQueryProvider.ConfigureHeaders` (L66–76)

The two methods are character-for-character identical apart from the name
(`trim → null-guard → resolve CustomAuthHeaderName ?? throw → TryAddWithoutValidation`).
The error string is duplicated verbatim. Extract one `protected` helper on
`AIProviderBase` (e.g. `SetApiKeyHeader(HttpRequestMessage request)`, which already has
`Options`) and have both providers call it; the near-identical method names
(`ApplyHeaders` vs `ConfigureHeaders`) for the same role also unify.

### 3 — `CustomAuthHeaderName` XML doc contradicts implemented behavior (new)

**Severity**: Medium
**File**: `AIProviderConnectLib/Options/AIProviderOptions.cs` (L46–51)

The doc comment says: *"When null, the provider uses its protocol-default header
name."* The implementation does the opposite — both providers **throw**
`InvalidOperationException` when it is null and a key is present. Consequences:

- Consumers that construct `KeyQueryOptions`/`MessagesApiOptions` manually (outside the
  catalog DI path) silently changed behavior: previously the header name came from a
  hardcoded default (`x-goog-api-key` / `x-api-key`), now every request throws. The
  plan's Compatibility section does not list this breaking change.
- The throw occurs at first request, not at registration; `CustomProviderDefinitionValidator`
  does not check that MessagesApi/KeyQuery custom definitions carry an auth header name,
  so misconfiguration surfaces late and only when a key happens to be non-empty.

**Fix**: make doc and behavior agree (delete the fallback sentence, or implement a
validation-time check). If the required-config behavior is intended (it is consistent
with the plan's goal), extend `CustomProviderDefinitionValidator` so custom definitions
using `MessagesApi`/`KeyQuery` protocols fail fast at registration when
`protocolConfiguration` lacks a non-empty `apiKeyHeaderName` — note the key name is
acceptable there only as a validation message, or delegate probing to the protocol file.

### 4 — `KeyQueryProvider` class doc still names the Gemini-specific header (new)

**Severity**: Low
**File**: `AIProviderConnectLib/Providers/KeyQueryProvider.cs` (L13)

*"Provider for API services that authenticate via the x-goog-api-key header"* — the
provider no longer knows any header name; the value is per-provider JSON data. Reword
to "authenticate via a configurable API-key header (`CustomAuthHeaderName`)" style.

### 5 — Five DI tests fail: keyed-only registration vs non-keyed test assertion (root cause identified)

**Severity**: High (test suite red)
**Files**: `AIProviderConnectLib.Tests/DependencyInjection/AIProviderRegistrationBuilderTests.cs`
(L45, L146, L178, L242, L267), `AIProviderServiceCollectionExtensions.RegisterInstance` (L171)

Confirmed still failing in this pass (232 passed / 5 failed). This pass identifies the
root cause the prior passes did not: all five tests resolve providers via the
**non-keyed** `provider.GetServices<IAIProvider>()`, but `RegisterInstance` registers
providers **only** as `AddKeyedSingleton<IAIProvider>(providerId, ...)`, so the
enumeration is always empty. This is fallout from the refactor-23 keyed-services
migration, not from refactor 24 — but it is the sole reason the library test suite
cannot go green, and it sits in the exact DI file this refactor modifies. Either add a
non-keyed aggregate registration per provider id (so `GetServices<IAIProvider>()`
enumerates all providers) or move the tests to `GetRequiredKeyedService<IAIProvider>(id)`.

### 6 — `gemini.json` still carries dead endpoint fields; `ModelCatalogProvider` ignores its seeded endpoints (carried, R1-2/R1-3)

**Severity**: Medium (violates the plan's Goal: "No dead data in JSON")

- `gemini.json` keeps `"chatEndpoint": "chat/completions"` and `"modelsEndpoint": "models"`.
  `KeyQueryOptions` has no endpoint properties, `SeedFromDefinition` has no KeyQuery arm,
  and `KeyQueryProvider` hardcodes `EndpointDefaults.KeyQuery.*` / `EndpointDefaults.Models`
  — zero readers, same category as the fields Phase 8 deleted elsewhere.
- Symmetrically, `github-models.json`'s `chatEndpoint`/`modelsEndpoint` **are** seeded into
  `OpenAICompatibleProviderOptions` (Catalog registers that options type, and
  `IChatAndModelsEndpointOptions` arm matches) but `ModelCatalogProvider` overrides both
  endpoints with `EndpointDefaults.Catalog.*` constants (L25–27) — the seeded values are
  silently discarded. Either read `Options` endpoints like `OpenAICompatibleProvider` does
  or the manifest values are dead.

## Carried findings still open (re-verified this pass)

| # | Finding | Evidence in current code |
|---|---------|--------------------------|
| R1-6 | **No unit tests for the refactor's core mechanism** — zero test hits for `ApplyProtocolConfiguration`/`ProtocolConfiguration` in `AIProviderConnectLib.Tests`; no `MessagesApiProviderTests` class; no `CatalogWireProtocolTests` file. The three protocol key-interpretation methods, the seeding copy, the `DefaultHeaders["anthropic-version"]` pipeline, and the null-`CustomAuthHeaderName` throw are all untested. | `Glob AIProviderConnectLib.Tests/**/*.cs` + greps above; test run shows only pre-existing Map/Parse tests |
| R2-F1 | **`KeyValueEditorControl` Remove button binding broken** — `KeyValueEditorControl.xaml` L29 binds `DataContext.RemoveCommand` via `AncestorType=ItemsControl`, which resolves to the ViewModel (has `RemoveProtocolConfigEntryCommand`, no `RemoveCommand`); the control's own `RemoveCommand` DP is never bound, so the ✕ button silently does nothing. Checklist Phase 9 claims it wired — it is not. Add button (L37) shows the correct `AncestorType=UserControl` pattern. | current XAML |
| R1-4/N1 | **Stale docs**: `features/10/custom-provider-registration.md` L82 references deleted `CatalogOptions`; L83 lists non-existent `AuthType`/`Organization`; `docs/concepts/wire-protocols.md` L65 still documents the `orgs/{Organization}/inference/...` alternative path. | Select-String output above |
| R1-5 | `wire-protocols.md` MessagesApi/KeyQuery auth bullets still describe `x-api-key`/`x-goog-api-key` as fixed implementation behavior and never mention that the header name is required `protocolConfiguration.apiKeyHeaderName` (or `CustomAuthHeaderName` on manual options) — the exact knowledge this refactor moved to JSON. | wire-protocols.md L35–60 above |

## Additional observations (verified, low severity)

- `ProviderManualEditorViewModel.BuildDefinition` (L532–534): duplicate key rows in the
  editor make `ToDictionary` throw — Save fails with the opaque framework message
  "An item with the same key has already been added" (caught, but unhelpful); rows with
  a key and a blank value are serialized into the manifest as empty strings, i.e. new
  dead data the protocol code ignores. Deduplicate/case-fold keys and drop blank rows.
- Dangling comment in `SeedFromDefinition` L147: *"KeyQueryOptions: seed BaseUrl only"*
  is no longer true (KeyQueryOptions also receives the `ProtocolConfiguration` copy).
- `ModelCatalogProvider` class doc (L12–13): *"with custom API versioning headers"*
  describes behavior that moved out of the provider in this refactor.
- `KeyQueryWireProtocol.ApplyProtocolConfiguration` uses `?.TryGetValue(...) == true`
  while the other two protocol files use an `is null` guard then `TryGetValue` — same
  outcome, pick one shape for the sibling methods.

## Issues verified correct this pass

| Aspect | Verdict |
|--------|---------|
| DI ordering (ProviderId → Seed → ApplyProtocolConfiguration → consumer builder callbacks) | Correct; consumer `Configure` wins |
| `CatalogWireProtocol.ApplyProtocolConfiguration(AIProviderOptions)` base parameter | Safe; only base members used, DI passes the concrete subtype |
| `BuildRequest` applies `options.DefaultHeaders` (AIProviderBase L116–117), so `anthropic-version`/`X-GitHub-Api-Version`/`Accept` reach requests | Verified |
| `CustomAuthHeaderName` placement (write from protocol file, read from provider) | Correct per plan |
| Manual `AddProvider` path skips seeding and apply | Consistent with its design (the manual path never seeded either); consumer owns its options |
| opencode JSON files, `HeaderNames`/`MediaTypes`/`EndpointDefaults` cleanup | Verified clean |
| One type per file, library build warning-free | Verified |

## Consolidated issue summary

| # | Issue | Severity | Category | Origin |
|---|-------|----------|----------|--------|
| 1 | Provider error messages contain protocol key literals `apiKeyHeaderName`/`protocolConfiguration` (plan "Do not" violation) | Med-High | SOLID (ISP) / plan conformance | New |
| 2 | Byte-identical auth-header block + error string duplicated in two providers | Medium | DRY | R1-1 |
| 3 | `CustomAuthHeaderName` doc contradicts throw-on-null behavior; late failure; compat gap | Medium | Docs / correctness | New |
| 4 | `KeyQueryProvider` class doc names provider-specific header | Low | Docs | New |
| 5 | 5 DI tests fail — tests use non-keyed `GetServices<IAIProvider>()`, registration is keyed-only | High | Testing | R2-F3, root-caused |
| 6 | Dead `chatEndpoint`/`modelsEndpoint` in `gemini.json`; Catalog endpoints overridden/ignored | Medium | Dead data | R1-2/R1-3 |
| 7 | No tests for `ApplyProtocolConfiguration` / `ProtocolConfiguration` pipeline | High | Testing | R1-6 |
| 8 | `KeyValueEditorControl` Remove button binding targets non-existent `RemoveCommand` | Medium | Bug (XAML) | R2-F1 |
| 9 | Stale `CatalogOptions`/`AuthType`/`Organization` refs in features/10 and wire-protocols.md | Low | Docs | R1-4/R1-5/N1 |
| 10 | wire-protocols.md auth sections don't document config-sourced required header name | Low | Docs | extended R1-5 |
| 11 | Duplicate editor keys crash Save with opaque message; blank values serialized | Low | Robustness | New |
