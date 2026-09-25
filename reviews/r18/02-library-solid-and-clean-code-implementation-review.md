# Refactor 18 — Library SOLID and Clean-Code Implementation Review

**File:** `refactors/refactor18/library-solid-and-clean-code.md` (design) + `refactors/refactor18/implementation-checklist.md`  
**Review Date:** 2026-09-17  
**Scope:** `6066de0..HEAD` — commits `c458990` ("r18") and `b4925d3` ("r18 correct 1"); `AIProviderConnectLib` production code, its test project, and the consumer wiring in `ScraperTool`  
**Method:** source audit against the plan's phases, manifest census (`ai-providers/*.json`), reference/usage grep across solution, build + test run  
**Exclusions:** cosmetic/style nitpicks, and findings already scheduled in `refactors/refactor21/review-remediation.md` (listed in the last section rather than re-argued)

---

## Summary

Refactor 18 delivered most of what it promised: the API-key-in-URL leak is gone, error codes are differentiated instead of one `ProviderMissingConfiguration`, the catalog returns snapshots under a lock, wire protocols are static and separately testable, `ProviderDefinition` lost its research fields, options/DI registration collapsed from six near-identical methods into one generic `Register<TOptions, TProvider>`, and records replaced mutable setters. The build is clean (0 warnings, 0 errors) and the 192 library tests pass.

The review found **two critical defects and thirteen major ones**:

1. **A real wire-level bug in the DI path the plan introduced** — `AddAiProviders()` sends Anthropic-class providers to `…/v1/chat/completions` instead of `…/v1/messages`. It is a functional regression waiting for its first consumer, not a style issue.
2. **The DI/factory surface from Phase 5 has no tests and no production caller** — which is precisely why (1) survived. The plan explicitly demanded `AIProviderServiceCollectionExtensionsTests` and `DefaultAIProviderFactoryTests`; neither exists.

Beyond those, the dominant remaining pattern is the one Refactor 18 was written to remove: **duplicated knowledge with two sources of truth**. Manifest JSON declares endpoints/auth styles the code never reads; each provider stores its options twice (base `Options` + `_options`); the protocol→provider switch now exists in the library *and* in `ScraperTool`; the wire-protocol constants migration was applied to about half of the serialization surface and skipped entirely for the two new protocols; and the plan's "composition over inheritance" instruction was implemented as a new public inheritance base that a non-OpenAI provider already derives from.

**Priority:** the Critical section should be fixed before the package's first release, while "nothing to break" still applies. Most Majors are small, localized corrections to work already in progress.

---

## Critical Issues

### C1. `AddAiProviders()` routes Messages-API providers to the wrong endpoint

**Location:** `AIProviderConnectLib/DependencyInjection/AIProviderServiceCollectionExtensions.cs` (`SeedFromDefinition`, MessagesApi case), `AIProviderConnectLib/Models/ProviderDefinition.cs`, `AIProviderConnectLib/ai-providers/anthropic.json`

The Anthropic manifest expresses its chat endpoint as a protocol-specific key:

```json
"baseUrl": "https://api.anthropic.com/v1/",
"messagesEndpoint": "messages",
"modelsEndpoint": "models",
"authenticationStyle": "ApiKeyHeader",
"apiKeyHeaderName": "x-api-key",
"apiVersion": "2023-06-01"
```

There is **no `chatEndpoint` key**, and a `git grep` across the library confirms **no code reads `messagesEndpoint`** (same for `authenticationStyle`, `apiKeyHeaderName`, `apiKeyPrefix`, `apiVersion`). So the deserialized `ProviderDefinition` keeps its C# default:

```csharp
public string ChatEndpoint { get; init; } = EndpointDefaults.ChatCompletions;  // "chat/completions"
```

and `SeedFromDefinition` then overwrites the *correct* `MessagesApiOptions.MessagesEndpoint` default with that unrelated value:

```csharp
case MessagesApiOptions messages:
    if (!string.IsNullOrEmpty(definition.ChatEndpoint))   // never empty — record default
        messages.MessagesEndpoint = definition.ChatEndpoint;   // "messages" → "chat/completions"
```

**Result:** any consumer that calls `AddAiProviders()` and resolves the `anthropic` provider POSTs to `https://api.anthropic.com/v1/chat/completions` — a 404 from the real API.

**Why it is structural, not a one-line typo:** the model makes "key absent from JSON" indistinguishable from "key present with the default value", so the seeding layer cannot tell whether it is being instructed or left alone. That is the same class of defect Phase 4 removed for `ChatRequest.Temperature` (the plan records that the JSON round-trip previously crashed on null temperature). Two-part fix:

- Make manifest-expressible endpoints null-by-default on `ProviderDefinition` (`string?`), seed only when non-null; and
- either model `messagesEndpoint` as an endpoint the definition carries, or rename the manifest keys to the protocol-neutral `chatEndpoint` / `modelsEndpoint` pair the code actually reads — the latter is preferable since it also kills the dead-key problem in M4.

**Latent status is not a mitigation.** It is invisible today for two reasons that are both accidents: `ScraperTool` never calls `AddAiProviders()` (see C2), and `anthropic.json` is currently the only `AnthropicCompatible` manifest in the catalog. Neither is a design guarantee, and the moment the published package is used as documented in `docs/api-reference/di-extensions.md`, it breaks.

---

### C2. The entire Phase-5 DI/factory surface has zero tests and zero production use

**Location:** `AIProviderConnectLib/DependencyInjection/`, `AIProviderConnectLib/Services/DefaultAIProviderFactory.cs`, `ScraperTool/App.xaml.cs` L263–279

The checklist is honest about this — L35–40 lists the ten planned test classes that were "Not yet added" and concedes that "the factory, DI overloads, mapper, and enum converters are the substantive remaining coverage gaps":

| Planned test file (checklist L35–38) | Exists |
| --- | --- |
| `AIProviderServiceCollectionExtensionsTests` | ❌ |
| `DefaultAIProviderFactoryTests` | ❌ |
| `OpenAICompatibleProviderTests` | ❌ |
| `MessagesApiProviderTests` | ❌ |
| `ProviderProtocolMapperTests` | ❌ |
| `EProviderProtocolJsonConverterTests` | ❌ |
| `KeyQueryWireProtocolTests` | ❌ |
| `CatalogWireProtocolTests` | ❌ |
| `InMemoryModelOverrideStoreTests` | ❌ |
| `MessageTextResolverTests` | ❌ (structurally impossible — see M11) |

The finding is therefore not that the ledger hides the gap; it is that the same document opens with **"All phases (1–7) are implemented"** (L7) five lines above its own admission that Phase 6 is not, and that the untested surface is also the **unused** surface:

```csharp
services.AddSingleton<ProviderCatalog>();
services.AddSingleton<AIProviderFactory>();
services.AddSingleton<IAIProviderFactory>(sp => sp.GetRequiredService<AIProviderFactory>());
services.AddSingleton<IProviderCatalog>(sp => sp.GetRequiredService<ProviderCatalog>());
```

So `AddAiProviders()` (three overloads), the keyed `IAIProvider` registrations, the keyed→non-keyed `RegisterInstance` bridge, `DefaultAIProviderFactory`, and `ModelCatalogOverrideDecorator` have no test that asserts what options a resolved provider ends up with, and no runtime path in the repository exercises them. (`AIProviderRegistrationBuilderTests` does exist and covers the builder's own `Add`/`Replace`/`Configure` semantics.) That is not merely "uncovered code"; it is *unverified design*, and C1 is the concrete proof — C1 lives in exactly the method that has neither a test nor a caller.

Two things are needed: the DI tests (seed → resolve → assert the request URI is the manifest's endpoint, per protocol), and a decision on whether `ScraperTool` should consume `AddAiProviders()` at all. Right now the library's headline integration story is decorative.

---

## Major Issues

### M1. The plan chose composition through one non-virtual seam; the implementation added a second, virtual, public inheritance layer

**Location:** `AIProviderConnectLib/Providers/OpenAICompatibleProviderBase.cs` (71 lines), `ModelCatalogProvider.cs`, `AIProviderBase.cs`

The design doc is explicit (L104, L106): `EnsureProviderEnabled` and `BuildRequest` are added to `AIProviderBase` and **"Both are non-virtual"**, header customization "goes through the `configureHeaders` delegate", and `HybridGatewayProvider` is "reduced to a class that composes `OpenAICompatibleWireProtocol` directly and reuses the base `BuildRequest`, passing a `configureHeaders` delegate". The delivered code does that **and also** inserts an undeclared abstract layer between `AIProviderBase` and three of the five providers, with new virtual/abstract members:

- `protected abstract string ChatEndpoint/ModelsEndpoint`, `protected virtual void ConfigureHeaders`, `protected virtual IReadOnlyList<AIModel> ParseModels` — none of which appear in the plan, and which reintroduce exactly the virtual-dispatch shape the plan rejected in favor of a delegate.
- `ModelCatalogProvider` (the `Catalog` protocol) derives from `OpenAICompatibleProviderBase` even though it is not an OpenAI wire-format provider, and it is the **only** consumer of the `ParseModels` virtual. The base's name is therefore wrong one level down, and a hook was added to a class to serve a subclass that shouldn't be there.
- Two mechanisms now do one job: `BuildRequest`'s `configureHeaders` parameter (used by all six non-OpenAI call sites, plus the OpenAI family via a method-group conversion) **and** the `protected virtual ConfigureHeaders` override. The parameter's own default behavior — "apply Bearer when `configureHeaders` is null" — is unreachable from inside the library, since no call site passes `null`; it survives only for external subclasses.
- `public` with no derivation outside the assembly. The visibility question is logged in `refactor21`; the *shape* question (wrong base, duplicated seam, undeclared virtuals) is not, and it is the one that decides whether the class should exist at all.

Recommendation: keep one request-shaping seam (`BuildRequest`'s delegate), make model-response parsing a protocol static (`CatalogWireProtocol.ParseModels`) rather than a virtual method, and re-parent `ModelCatalogProvider` on `AIProviderBase`.

### M2. Duplicated protocol→provider switch across library and consumer, with seeding unreachable in the consumer

**Location:** `ScraperTool/Services/AIProviderFactory.cs` L45–119 vs `AIProviderConnectLib/DependencyInjection/AIProviderServiceCollectionExtensions.cs` (`Register`/`SeedFromDefinition`)

The same five-way `EProviderProtocol` switch now exists twice. Worse, the consumer copy cannot reuse the seeding logic because `SeedFromDefinition` is `private` to the DI extension class, so it hand-rolls a lossy equivalent:

```csharp
private IOptions<OpenAICompatibleProviderOptions> BuildOptions(string apiKey, string providerId)
    => Options.Create(new OpenAICompatibleProviderOptions { ApiKey = ..., BaseUrl = _catalog.Get(providerId)?.BaseUrl ?? ... });

private IAIProvider BuildProvider(ProviderDefinition definition, IOptions<OpenAICompatibleProviderOptions> options, HttpClient http)
{
    var opts = options.Value;              // shared instance, mutated per provider
    opts.ProviderId = definition.Id;
    return definition.Protocol switch { ... new MessagesApiProvider(http, new MessagesApiOptions { ApiKey = opts.ApiKey, BaseUrl = opts.BaseUrl, ... }) ... };
}
```

Consequences: (a) a throwaway `OpenAICompatibleProviderOptions` is allocated as a carrier for *every* protocol; (b) `ChatEndpoint` / `ModelsEndpoint` / `ApiVersion` are never copied, so ScraperTool's providers always run on C# defaults — today that happens to be correct for Anthropic, which is exactly why C1 is silent here; (c) the mutation of the shared `opts` instance per call is a latent aliasing bug if `DefaultHeaders` or endpoints are ever added. This is DRY and OCP: adding a protocol means editing both switches.

### M3. Options are stored twice in every provider

**Location:** `MessagesApiProvider.cs`, `KeyQueryProvider.cs`, `ModelCatalogProvider.cs`, `HybridGatewayProvider.cs`, `OpenAICompatibleProvider.cs` — each holds `private readonly TXxxOptions _options;` while `AIProviderBase` already exposes `protected AIProviderOptions Options { get; }`

Two references to one object exist in every provider: the base's `protected AIProviderOptions Options { get; }` (assigned from the constructor) and the subclass's `private readonly TXxxOptions _options;`. The plan sanctioned this deliberately — its checklist tells providers to call `EnsureProviderEnabled(_options)` and `BuildRequest(_options, …)` explicitly — so it is not a mistake, but it is the twin of the `_definition` duplication Phase 2 removed, and it is the reason `BuildRequest` and `EnsureProviderEnabled` need an `AIProviderOptions` parameter at all: the base already has the value, and callers must remember to hand it back. It also produces mixed usage inside one class pair (`OpenAICompatibleProviderBase.ChatAsync` reads `Options`, its own subclasses read `_options`), which is how a future edit ends up setting one and reading the other. The clean resolution is a generic base (`AIProviderBase<TOptions> : AIProviderBase` exposing `protected TOptions TypedOptions`) or dropping the base property — either way, one owner.

### M4. Manifests advertise configuration the code ignores (root cause of C1)

**Location:** `AIProviderConnectLib/ai-providers/*.json`, `ProviderDefinition.cs`

Verified with `git grep` over the library: **zero readers** for `authenticationStyle`, `apiKeyHeaderName`, `apiKeyPrefix`, `messagesEndpoint`, and `apiVersion`. Meanwhile auth and versioning are hardcoded in C# per provider class. So the catalog file — the artifact ScraperTool exists to research and maintain — contains fields that look authoritative and are silently discarded, and the "39 providers" the embedded catalog serves are actually "the union of manifest values the five known properties happen to bind". Either bind them into `ProviderDefinition`/options and honor them (which is what a data-driven catalog means), or delete them from the schema and the writer. Leaving them is the worst option: it makes the JSON the wrong kind of source of truth.

Related naming rot from the same decision: `KeyQueryProvider` no longer puts a key in the query string (`?key=` was correctly removed in Phase 1) — it sends `x-goog-api-key`. The type is now named after the bug it used to have.

**The dead keys also encode an unimplemented feature.** `opencode-zen.json` and `opencode-go.json` are `HybridGateway` and carry both endpoint families plus a model-routing list:

```json
"protocol": "HybridGateway",
"chatEndpoint": "chat/completions",
"messagesEndpoint": "messages",
"anthropicModelPrefixes": ["claude-", "minimax-", …]
```

`anthropicModelPrefixes` has **zero readers solution-wide**. So the manifests describe per-model wire-format routing, while `HybridGatewayProvider`'s only real switching axis is a string `AuthType` compared against `AuthSchemes.Bearer` (everything else throws `ConfigurationError`) — two different semantics for the same provider type, with the manifest's version silently dropped. This is not a missing feature request; it is a decision the schema needs before a release freezes it: which of the two `HybridGateway` meanings is authoritative?

### M5. Transport orchestration is still duplicated six times inside the library

**Location:** `AIProviderConnectLib/Providers/MessagesApiProvider.cs` L33–44 / L50–58 / L66–86, `KeyQueryProvider.cs` L30–38 / L44–52 / L60–82 — three methods each, repeating the identical sequence with only the protocol static and the read shape varying:

```csharp
EnsureProviderEnabled(_options);
using var httpRequest = BuildRequest(_options, HttpMethod.Post, endpoint, payload, ApplyHeaders /* or ConfigureHeaders */);
using var response = await HttpClient.SendAsync(httpRequest, cancellationToken);
await ThrowIfErrorAsync(response, cancellationToken);
var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
return XxxWireProtocol.ParseResponse(json);
```

The plan diagnosed this precisely — `library-solid-and-clean-code.md` L27: *"`BuildRequest` is copy-pasted into all five concrete providers; only the header-application block varies"* — but the checklist only scheduled the removal for `HybridGatewayProvider` (L93). Three of the five providers ended up with one shared body; the other two kept three each. Six copies means six places to fix the next time the send/error/read order changes, and they are already drifting: `MessagesApiProvider` needs a three-statement header method with an `if`, `KeyQueryProvider` a one-line expression, and both stream paths re-deserialize inside the SSE loop by hand. This is the unfinished half of the refactor's own headline diagnosis.

### M6. The keyless-provider escape hatch cannot be reached

**Location:** `AIProviderBase.cs` L35 (`public virtual bool RequiresApiKey => true;`), used at L89 inside `EnsureProviderEnabled`, `IAIProvider.cs`

The flag is live and correct in the base — a missing key throws `NoApiKey` unless `RequiresApiKey` is `false` — but **no provider overrides it, and every provider is `sealed`**, so neither the library nor a consumer can ever express "this provider needs no authentication". The practical effect is that any catalog entry whose endpoint works key-free (a local server, a public catalog) is forced through the same gate and needs a placeholder key to be usable at all. The doc comment even says *"Override to false for providers that work without authentication"* — an instruction no delivered type can follow.

The other half of the problem is the abstraction: `RequiresApiKey` is not on `IAIProvider`, while `SupportsModelDiscovery` and `SupportsStreaming` are. So a consumer cannot ask through the interface whether a key is needed and cannot pre-validate; it can only catch `NoApiKey` after the fact. Either surface the capability on `IAIProvider` and override it where the semantics differ, or delete the virtual — a capability flag that is documented, consumed, and permanently `true` is speculative generality with a live cost.

### M7. `ProviderProtocolMapper` silently defaults unknown protocols to OpenAI-compatible

**Location:** `AIProviderConnectLib/Models/ProviderProtocolMapper.cs` L15–23, `Models/EProviderProtocolJsonConverter.cs` L17–21

```csharp
// ProviderProtocolMapper.FromJson
if (ByJsonValue.TryGetValue(jsonProtocol, out var protocol)) return protocol;
return EProviderProtocol.OpenAICompatible;          // L22 — any unrecognized string

// EProviderProtocolJsonConverter.Read
if (string.IsNullOrWhiteSpace(value))
    return EProviderProtocol.OpenAICompatible;      // L19 — and a missing/empty "protocol" key
```

The silent default is applied at **two** layers, so a manifest with no `protocol` key, a typo (`"anthropic_comp"`), an upstream rename, or a newly added enum value that was not registered in `ByJsonValue` all resolve to a provider that speaks the *wrong wire format* against a real endpoint — surfacing later as an opaque HTTP 4xx instead of a loud `AiException(AiErrorCodes.ConfigurationError)` at load. Note the fragility this hides: `ByJsonValue` deliberately maps names that differ from the enum members (`"anthropiccompatible" → MessagesApi`, `"geminicompatible" → KeyQuery`, `"githubmodelscompatible" → Catalog`), which is exactly the situation where a missing entry is a mistake rather than a default. Phase 1's own "Rejected alternatives" list rejects silent fallback. Also: no XML doc on either type, and neither has a test (see C2).

### M8. `ProviderCatalog` breaks its own definition/metadata pair invariant

**Location:** `AIProviderConnectLib/Services/ProviderCatalog.cs` L63, L111, L136, L219–L222

The catalog keeps two dictionaries (`_providers`, `_researchMetadata`) and the code paths no longer agree on how they stay in sync:

- `Upsert` → `_researchMetadata.TryAdd(definition.Id, new ProviderResearchMetadata())`. Dynamic providers registered through `IProviderCatalog.Add` / `Replace` therefore get an **empty, fabricated** metadata record, and there is no public API to supply the real one. Worse, `GetResearchMetadata` is documented as returning "the separate research metadata for a provider, or null if not found" — for a dynamic provider it returns a non-null object full of defaults, which a consumer cannot distinguish from "researched, nothing found". In `ReloadFromDisk` the same `Upsert` is immediately followed by `_researchMetadata[provider.Id] = metadata` (L185–186), so the `TryAdd` exists only to satisfy a path that then overwrites it.
- `WithDynamicCatalog` reads `_researchMetadata[p.Id].IsDynamicModelCatalog` — a **throwing indexer** on the dictionary that every other mutation treats with `TryAdd`. No key is missing today, but the pair invariant is held together by convention at four call sites and one violation turns a read-only query into a `KeyNotFoundException` inside a `lock`. A single `Dictionary<string, (ProviderDefinition, ProviderResearchMetadata)>` (or a small record) makes the invariant unrepresentable-to-break.
- Each manifest is deserialized **twice** — once as `ProviderDefinition` (L219) and once inside `ReadResearchMetadata` (L136) from the same text. One parse into a `JsonDocument`/root element shared by both readers removes it.
- `File.ReadAllText` is executed inside `lock (_sync)` (already logged in `refactor21` Phase 2; it also means catalog reload blocks every reader for the duration of disk I/O).

The reload error path was genuinely improved (narrowed catch, `LoadErrors`), and `ScraperTool` does read `LoadErrors`, so this is not dead code — but the invariant "one id, one coherent (definition, metadata) pair" needs one owner, not four conventions.

### M9. New `IProviderCatalog` members with no consumers

**Location:** `AIProviderConnectLib/Abstractions/IProviderCatalog.cs` L19–22

- `WithModelDiscovery`, `WithDynamicCatalog`, `GetByCategory` — grep shows **zero production callers** (only tests, and for `WithDynamicCatalog` only the catalog's own test). They were added without a consumer and, as M8 shows, one of them has an unchecked invariant behind it.
- They are also shaped inconsistently with the rest of the interface: `WithModelDiscovery` / `WithDynamicCatalog` are **properties** whose `With…` names read like transformations but which perform a filtered scan on every get; `GetByCategory(string)` is a **method** in the same group; and its parameter is a bare string compared against an undeclared value set (`"DirectProvider"`, …) with no `EProviderCategory` anywhere in the solution — the one place Phase 4/6's "type the enum" discipline was skipped, then made part of the published API.
- L19–22 carry no XML docs, and `NoWarn=CS1591` guarantees nobody notices (see m8). The interface's own summary still says "Read-only access to provider definitions" while it now also exposes research metadata and load diagnostics — three reasons to change in one abstraction (SRP), which is exactly the critique Phase 4 applied to `ProviderDefinition`.

Recommendation: keep `LoadErrors` and `GetResearchMetadata` (both have real consumers), and either wire the three filters into `ScraperTool` — whose screens currently hand-roll LINQ over `All` — or drop them until there is a caller.

### M10. Research model leaks the consumer's vocabulary into the published library

**Location:** `AIProviderConnectLib/Models/ProviderResearchMetadata.cs` L79–81

> *"URLs may contain `{customer}` placeholder — the AI fixer extracts the actual value…"*

"The AI fixer" is a `ScraperTool` feature. Phase 5's stated goal for `GetStatusMessage` was to remove exactly this kind of "AI Setup" wording from the library, and it was done for the runtime messages but reintroduced in the model documentation. The library cannot know who fixes anything.

### M11. `MessageTextResolver` is untestable as designed

**Location:** `AIProviderConnectLib/Services/MessageTextResolver.cs`, `AIProviderConnectLib.csproj`

The type is `internal static`, and `git grep InternalsVisibleTo` across the solution returns exactly one hit — `ScraperTool/ScraperTool.csproj` → `ScraperTool.Tests`. The library has none, so the `MessageTextResolverTests` demanded by the Phase 6 matrix cannot compile. The behavior it encapsulates (multi-part text selection) is used by the wire protocols and is therefore *de facto* covered only through them. Decide: make it `public`/documented as a supported helper, add `InternalsVisibleTo` for the test assembly, or fold it back into the protocols that own the rule.

### M12. Test suite quality — several assertions that cannot fail

**Location:** `AIProviderConnectLib.Tests/Services/ProviderCatalogTests.cs`, `AIProviderConnectLib.Tests/Providers/*Tests.cs`

- `CloudProvider_DuplicatePricingUrls_AreDetected` (L259–276) asserts that a hand-built fixture's two fields **are** equal — the inverse of the intent stated in its own comment — and touches no production code at all. It cannot fail.
- `All_DefaultEndpointsAreSetCorrectly` (L354–376) is both dead and inverted: it guards on `if (string.IsNullOrEmpty(provider.ChatEndpoint))` and then asserts the value `Should().Be("chat/completions")`. The branch can never be entered (the record default makes it non-empty — see C1), and if it ever were, the assertion would fail. A wrong or missing endpoint default would pass this test exactly as C1 passes it today.
- `HaveCount(8)` (L125) pins a magic number of embedded providers; adding a manifest breaks a "unit" test for no behavioral reason.
- Catalog and provider tests reach for the real embedded catalog (`new ProviderCatalog()` at `ProviderCatalogTests` L118/L134/L358 and `KeyQueryProviderTests` L86), so unit tests depend on shipped `ai-providers/*.json` content. `HybridGatewayProviderTests` L70–76 shows the right alternative already established in this codebase — construct `ProviderCatalog` with an explicit definition set — it just isn't applied consistently. `TestDoubles/` has `CapturingHttpMessageHandler` but no fake `IProviderCatalog`. `ProviderCatalogTests` L126 also shows the ergonomic cost of the metadata split: `catalog.GetResearchMetadata(p.Id)!`, a null-forgiving operator on the public API.
- `KeyQueryProviderTests.Operations_ReportConfigurationErrorsBeforeSending` (L67–95) parameterizes only the *operation* via `[InlineData]` and then `foreach`es the enabled/baseUrl/apiKey matrix (L73–78) inside the test body. A failure names one test id for nine configurations, and a configuration can be added or dropped without the theory's case count changing. `HybridGatewayProviderTests.Operations_RejectUnsupportedAuthBeforeSending` (L21) has the identical inner-loop shape. Both matrices belong in `[MemberData]` so each combination is its own case.
- The per-provider `InvokeAsync(provider, operation)` dispatch switch is duplicated verbatim across the provider test classes (`KeyQueryProviderTests` L97–116, `HybridGatewayProviderTests` L78–97) instead of living once in `TestDoubles/` — and it silently no-ops on an unknown operation string, so a typo in `[InlineData]` passes without invoking anything.
- `KeyQueryProviderTests` (L36–65) is the opposite example and worth copying: precise assertions on URL shape, `x-goog-api-key` presence, and no key in the query string. These are genuinely good regression tests.
- `AIProviderBaseTests` (60 lines) contains no streaming/SSE test despite being the class that owns `ReadServerSentEventsAsync`; it covers only `ThrowIfErrorAsync` / `GetStatusMessage`, exercised through a concrete subclass. The `[DONE]` sentinel, the non-`data:` line skip, and the whitespace-only-line branch are unasserted.
- `ModelCatalogProviderTests` (65 lines) has exactly one test — the configuration-error theory — so none of the behavior that makes this provider distinct is covered: the organization-scoped inference endpoint (`ModelCatalogProvider` L28–31), the custom `Accept` / api-version headers (L41–42), or `CatalogWireProtocol.ParseModels`. It also uses the same inner-loop theory shape, and `ChatEndpoint`'s two branches are never both exercised.
- `MessagesApiProtocolTests`' diff for this range is a mechanical `ChatRole` → `EChatRole` rename: the newly extracted `MapStreamRequest` has zero coverage, and `ParseStreamChunk`-adjacent literal parsing remains.
- Provider error-code coverage is asserted once per provider class against the shared `EnsureProviderEnabled` behavior (which lives in the base), so the same three rules are re-tested for each subclass instead of once at the base plus once per genuine difference.

### M13. Options types duplicate each other and the manifest (two sources of truth for one provider)

**Location:** `AIProviderConnectLib/Options/OpenAICompatibleProviderOptions.cs`, `HybridGatewayProviderOptions.cs`, `CatalogOptions.cs`, `MessagesApiOptions.cs`

`OpenAICompatibleProviderOptions` and `HybridGatewayProviderOptions` declare structurally identical members (`ChatEndpoint`, `ModelsEndpoint`) with identical defaults; `CatalogOptions.ApiVersion = "2022-11-28"` and `MessagesApiOptions.ApiVersion = "2023-06-01"` restate values that also live in the manifests (and which nothing reads today, per M4). Phase 1's premise was "these five classes differ only in wire-format identity and auth; share the shape". The shared shape was put on `AIProviderOptions` but the endpoint/version pairs were left duplicated. This is also what forces the `switch` on options type in `SeedFromDefinition` — a type switch over near-identical records is a sign the records were split finer than their behavior.

---

## Minor but Worth Fixing (not nitpicks)

| # | Location | Issue |
| --- | --- | --- |
| m1 | `Providers/ModelCatalogProvider.cs` L40, `HybridGatewayProvider.cs` L37 | Trim inconsistency inside the new hierarchy (tracked in `refactor21` Phase 2, still open). |
| m2 | `Protocols/OpenAICompatibleWireProtocol.cs` L20, L25–26, L56, L256–262, L352–358 | Constants migration applied to roughly half the surface: the **request-mapping** side is still raw (`"json_schema"` at L20/L25/L26, role via `m.Role.ToString().ToLowerInvariant()` at L56) while the parse side uses `OpenAICompatiblePropertyNames`; and `OpenAICompatiblePropertyNames.Function` is looked up twice in the same object (`func` / `func2`) at L256+L260 and L352+L356. |
| m3 | `Protocols/KeyQueryWireProtocol.cs`, `CatalogWireProtocol.cs` | Both new protocols were born with raw JSON literals and **no** `*PropertyNames` class, undoing Phase 2's convention at the moment of introduction; neither class has an XML doc. |
| m4 | `Protocols/MessagesApiProtocol.cs` L144–161 | `usage.input_tokens` and `usage.output_tokens` are each parsed **twice** — once for `PromptTokens`/`CompletionTokens` and again (reusing the same `out` variables) to compute `TotalTokens`, i.e. three reads of two fields where one would do. |
| m5 | `HybridGatewayProvider.ConfigureHeaders` L28–38 | `AuthType` is validated as a side effect of header construction **and** gated on `!IsNullOrWhiteSpace(ApiKey)`: a misconfigured gateway with no key yet fails quietly, and validation depends on request construction rather than `EnsureProviderEnabled`. |
| m6 | `MessagesApiProvider.ApplyHeaders` L93, L97 | `request.Headers.Remove(...)` immediately before `Add(...)` on a freshly built request — no-ops that imply a conflict the type cannot have. |
| m7 | `Providers/AIProviderBase.cs` L153–160 | The SSE prefix check slices with `SSEConstants.DataPrefix.Length`; the checklist's `DataPrefixLength` constant was skipped, so the deviation is undocumented rather than wrong. Cosmetic-adjacent, but the ledger should say so. |
| m8 | `Abstractions/IAIProvider.cs` L8 | `<see cref="Services.ProviderCatalog"/>` still points at the concrete service; Phase 5 asked for `IProviderCatalog`. (Also: `Directory.Build.props` sets `NoWarn=CS1591`, so the missing XML docs on `IProviderCatalog`'s new members, `ProviderProtocolMapper`, both new protocols, and `EPriceUnit` never surface as warnings — the suppression is hiding a real documentation gap in a published API.) |
| m9 | `Protocols/MessagesApiProtocol.cs` L31–34, `Protocols/KeyQueryWireProtocol.cs` L13–16 | System-message text is joined with `Environment.NewLine + Environment.NewLine`, so the same `ChatCompletionRequest` produces **different request bytes on Windows vs Linux** — non-deterministic payloads in a wire protocol (LF is the right constant). The identical expression is also duplicated in both protocols. |
| m10 | `AIProviderConnectLib.Tests` namespaces | Two root conventions coexist in one assembly: 11 files use `AIProviderConnect.Tests.*` and 6 use `AIProviderConnectLib.Tests.*` — including **inside the same folder** (`Providers/ModelCatalogProviderTests.cs` is `…AIProviderConnectLib.Tests.Providers`, `Providers/ModelCatalogOverrideDecoratorTests.cs` is `…AIProviderConnect.Tests.Providers`). The shared `TestDoubles.CapturingHttpMessageHandler` therefore sits under one root and is consumed from the other. The assembly is `AIProviderConnectLib.Tests`, the library namespace is `AIProviderConnect`, and the library root folder is `AIProviderConnectLib` — pick one convention before an `InternalsVisibleTo` (M11) has to be written against a specific root. |
| m11 | Ledger accuracy | `refactors/refactor18/implementation-checklist.md` L7 asserts **"All phases (1–7) are implemented"** five lines before its own L31 concedes **"Phase 6 is partially complete"** and L35–38 lists ten test classes never added — the sentence is true only of production code, and as written it is the line a reader (or a release checklist) will act on. To the document's credit, L42–43 states that the status section supersedes the phase lists, so the wall of `- [ ]` from L79 onward is explained rather than misleading; but that also means the boxes can no longer be used to tell done from not-done, and several items whose code demonstrably shipped are still unticked (`EnsureProviderEnabled` L79, `BuildRequest` L80, get-only `Definition` L83, `_definition` deletion L86) while one shipped *differently* on purpose (L82 asked to keep the legacy `protected AIProviderBase(HttpClient)` constructor "until Phase 7"; it is gone — the better outcome, and it is recorded in the status section at L23–25). `refactors/overview.md` L22 marks 18 **done** (with a caveat that the Phase 6 matrix "remains partially open") while its own remediation plan (row 25, `refactor21`) is **planned**, and `refactor21`'s premise — "no blocking defects" — is the conclusion this review's C1 contradicts. `refactors/refactor19/…` Phase 3 (consumer depends on `IProviderCatalog`) was never applied: `ScraperTool/Services/AIProviderFactory.cs` L17 still injects the concrete `ProviderCatalog`. |

---

## Verified Good (so the fixes aren't undone)

- **Security fix is real and tested.** No provider places a key in a URI; `KeyQueryProviderTests` asserts `x-goog-api-key` and the absence of `?key=`.
- **Error model differentiated.** `NoBaseUrl` / `NoApiKey` / `ProviderDisabled` / `ConfigurationError` / `ProviderNotFound` / `ModelDiscoveryNotSupported` are raised distinctly; `HybridGatewayProvider` now fails loudly on an unsupported `AuthType` instead of sending a malformed request.
- **`EnsureProviderEnabled(options)` consolidated** into the base, with `Enabled = true` and `BaseUrl`/`ApiKey` defaulting to `string.Empty`.
- **Catalog thread safety.** `_sync` plus `.ToList()` snapshots on reads/reload; the previous crash-on-null-temperature JSON round-trip is gone; `RegionalEndpoints` is defensively copied.
- **Model split honored.** `ProviderDefinition` no longer carries research fields; `ProviderResearchMetadata` is behind `IProviderCatalog.GetResearchMetadata`, and ScraperTool consumes it through that seam.
- **DI consolidation.** One generic `Register<TOptions, TProvider>`, keyed registration plus the non-keyed bridge in `RegisterInstance`, `AIProviderRegistrationBuilder.BuildDefinitions(IProviderCatalog)` done, `ModelOverrideMerger` using `with` instead of a hand-written `Copy`, and `ModelCatalogOverrideDecorator` delegating correctly including the streaming path and the `ModelDiscoveryNotSupported` catch.
- **`GetStatusMessage`** lost its "AI Setup" wording, and one type per file was respected throughout (including the new `ProviderResearchMetadata`).
- **Deviations are recorded, not hidden.** The checklist's status section (L15–29) names each place the implementation superseded the plan text — single-argument `GetProvider`, no `NotSupportedException` branch, removal of the legacy base constructor — with the reason. That is the discipline this review asks to extend to Phase 6/7 rather than invent.

---

## Phase Coverage Against the Plan

| Phase | Plan intent | Delivered | Gap |
| --- | --- | --- | --- |
| 1 | Kill `?key=` leak, differentiate errors, reject silent fallbacks | ✅ largely | ⚠️ silent fallback reintroduced in `ProviderProtocolMapper` (M7); `KeyQuery` naming now wrong (M4) |
| 2 | Remove double storage, constants instead of literals | ✅ definitions, ⚠️ partial constants | ❌ options double storage (M3); `OpenAICompatiblePropertyNames` incomplete and skipped for new protocols (m2, m3) |
| 3 | Shared transport via composition; protocols as statics | ✅ statics; ❌ composition | ❌ new inheritance base with a mis-parented subclass (M1); 6× duplicated orchestration (M5) |
| 4 | Split runtime vs research model | ✅ | ⚠️ catalog pair invariant (M8) |
| 5 | Clean public contracts + docs | ⚠️ | ❌ `IAIProvider` doc (m8), library boundary leak (M10), docs/CHANGELOG still unrewritten (tracked) |
| 6 | Test matrix per provider/protocol/DI | ⚠️ ~40% | ❌ all DI/factory/mapper/protocol-name tests missing (C2); weak assertions in catalog tests (M12) |
| 7 | Breaking-change ledger + versioned notes | ❌ | tracked in `refactor21` Phase 4; `Enabled` default flip still not itemized |

---

## Relationship to Existing Refactor Plans

**Already tracked in `refactors/refactor21/review-remediation.md`** — confirmed true, deliberately not re-argued here, but note that refactor21's premise ("no blocking defects") predates C1:

- `IAIProviderFactory.GetProviders()` deletion; `ScraperTool` returning `[]` violates the postcondition.
- `EPriceUnit` ↔ `ScraperTool.Models.EPriceUnit` name collision and the qualification tax.
- Docs drift (`interfaces.md`, `provider-catalog.md`), CHANGELOG breaking bullets, `Enabled` default disclosure.
- `ITransientCredentialProviderFactory` DIP restoration; capability type-tests → `Supports*` predicates.
- Bearer trim centralization; `ReloadFromDisk` lock scope and silent rejections; `OpenAICompatibleProviderBase` visibility decision; `ProviderManifestSerializer` extraction (the 3× ScraperTool DRY).

**New in this review, not tracked anywhere:**

| Finding | Why refactor21 misses it |
| --- | --- |
| **C1** Messages endpoint mis-seeding | refactor21 reviewed the commit's *cleanup*, not the wire behavior of the new DI path |
| **C2** No tests/no consumer for the Phase-5 surface | refactor21's scope excluded test-matrix completeness |
| M1 composition→inheritance deviation, mis-parented `ModelCatalogProvider`, undeclared virtuals, in-library-dead default header branch | only visibility was logged |
| M2 duplicated protocol switch + unreachable seeding in ScraperTool | refactor19 Phase 3 was never applied, so no consumer-side comparison was made |
| M4 dead manifest keys, `KeyQuery` naming | manifests were treated as data, not as contract |
| M3 options double storage | Phase 2 fixed only the definition twin |
| M5 6× transport duplication | OpenAI family was de-duplicated, the other two were not audited |
| M6 unreachable keyless-provider flag (`RequiresApiKey`) | — |
| M7 silent protocol fallback | — |
| M8 catalog pair invariant (throwing indexer, fabricated metadata, double parse) | only lock scope was logged |
| M9 zero-consumer catalog members | — |
| M10 `ProviderResearchMetadata` "AI fixer" leak | the analogous runtime leak *was* logged |
| M11 untestable `MessageTextResolver` | — |
| M12 vacuous/dead-branch/count-pinned tests | — |
| M13 options/options-vs-manifest duplication | — |

`refactors/refactor20` (ScraperTool god-class decomposition) is unaffected by anything here.

---

## Recommendations

### Immediate (Critical)

1. **Fix C1** — null-by-default `ProviderDefinition` endpoints, seed only non-null values, and unify manifest keys with the properties the code reads. Add a regression test asserting the *request URI* per protocol from a manifest.
2. **Write `AIProviderServiceCollectionExtensionsTests` + `DefaultAIProviderFactoryTests`** (seed → resolve → assert options/endpoint/protocol), or delete `AddAiProviders()`/`DefaultAIProviderFactory` until a consumer exists. Untested-and-unused is not a delivered feature.

### Short-Term (Major)

3. Collapse the two protocol→provider switches into one reusable, public seam (make the seeding logic reachable and have `ScraperTool/Services/AIProviderFactory` consume it).
4. Re-parent `ModelCatalogProvider` onto `AIProviderBase`; delete the `ParseModels` virtual in favor of `CatalogWireProtocol`; keep exactly one header-shaping seam.
5. Extract one shared "build → send → throw-if-error → read → parse" helper (the plan never named it; `SendAndParseAsync` here) and use it in `MessagesApiProvider` / `KeyQueryProvider` (M5); remove the parallel `_options` fields or type the base generically (M3).
6. Make `ProviderProtocolMapper` throw `ConfigurationError` on unrecognized values (M7).
7. Decide the manifest contract: bind or delete `authenticationStyle` / `apiKeyHeaderName` / `apiKeyPrefix` / `apiVersion` / `messagesEndpoint` / `anthropicModelPrefixes` (M4), and settle what `HybridGateway` means in the schema before it is frozen by a release.
8. Restore the pair invariant in `ProviderCatalog`: one parse per manifest, one owner for (`definition`, `metadata`), no throwing indexers on catalog queries (M8); then wire or remove the three unused `IProviderCatalog` members (M9).
9. Fix the boundary leak in `ProviderResearchMetadata` (M10) and resolve `MessageTextResolver`'s accessibility (M11).
10. Delete or implement `RequiresApiKey` (M6) — and put it on `IAIProvider` if it stays; merge the duplicated options types (M13).

### Medium-Term (Minor)

11. Finish the constants migration and add the two missing `*PropertyNames` classes (m2, m3); drop the no-op `Remove` calls (m6); move `AuthType` validation into configuration checking (m5).
12. Repair the weak tests (M12) and add a fake `IProviderCatalog` to `TestDoubles/` so unit tests stop depending on shipped manifests.
13. Reconcile the ledger: tick or delete the checklist boxes whose work shipped, update `refactors/overview.md`, apply refactor19 Phase 3 (m11) — and re-examine `refactor21`'s "no blocking defects" verdict in light of C1, since its whole scope assumption rests on it.

---

## Verdict

**Directionally correct, substantially delivered, and not yet trustworthy at the edges.**

Refactor 18 removed the defects it named in the provider and catalog layers, and the library is measurably more SOLID: shared base behavior, static protocols, split models, consolidated registration, no key leakage. That work is real and verified.

The review found the refactor incomplete in the way that matters: the new public integration path (`AddAiProviders`) contains a wire-level bug (C1) and has neither tests nor a consumer (C2), so the part of the design meant to be the package's front door is the least verified part of it. Underneath, the duplication the plan set out to eliminate mostly survived in a relocated form — two protocol switches, two option stores, two transport orchestrations, two sources of truth for endpoints/auth/version, two request-shaping seams — and the plan's composition instruction became a public inheritance base that a non-OpenAI provider already misuses.

**Priority:** High, and time-sensitive — the package has never been released, so C1, M4, M9, M13 and the `GetProviders`/`EPriceUnit` removals can still be fixed as design corrections rather than breaking changes.

**Effort:** Small-to-medium. C1 is a localized model/seeding change plus one regression test; C2 is the two missing DI test classes; M1–M8 are each confined to files Refactor 18 already touched. M12 (tests) is the largest single block of work, and the only item with real ongoing cost if deferred: without those tests the next refactor of this layer has the same blind spot this one did.
