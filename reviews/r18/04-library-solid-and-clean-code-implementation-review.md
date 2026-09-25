# Refactor 18 — Implementation Re-Verification and Remaining Gaps

**File:** `refactors/refactor18/library-solid-and-clean-code.md` (design) + `refactors/refactor18/implementation-checklist.md` (ledger)  
**Review Date:** 2026-09-17  
**Scope:** `6066de0..HEAD` (`ad8683f`), i.e. code commits `c458990` ("r18") and `b4925d3` ("r18 correct 1"). `AIProviderConnectLib` production code, its test project, the `ai-providers/*.json` manifest set, and the `ScraperTool` paths that read/write the library's model.  
**Method:** source audit against every plan phase + manifest/vocabulary census + `git grep` reference counts + `dotnet test` (192 library tests pass, 0 failures) + a **hermetic runtime probe** executed against the built `AIProviderConnect.dll` (no network, `https://test.example.com` only). The probe project was temporary and has been deleted; its transcript is reproduced in Appendix A (only column padding trimmed).  
**Relationship to prior records:** `02-library-solid-and-clean-code-implementation-review.md` reviewed `c458990` and produced 2 Critical / 13 Major / 11 minor findings; `refactors/refactor21/review-remediation.md` is the follow-up plan derived from it. This review does **not** re-argue those findings. It reports (a) defects neither record contains, (b) proof that a previous Critical is still live, and (c) where the two documents no longer match HEAD. Excluded as instructed: cosmetic/style nitpicks.

---

## Summary

Refactor 18's structural claims hold up under re-verification: transport really did move into `AIProviderBase`/`OpenAICompatibleProviderBase`, the five `RegisterXxx` methods really did collapse into one generic `Register<>`, the key-in-URL leak is gone, `ProviderDefinition` really is an 8-property runtime record with research metadata split out, and the catalog really does lock-and-snapshot. Those are not re-litigated here.

What this pass adds is **one new Critical with a runtime proof, and evidence that the previous review's Critical is still broken and has no owner**:

1. **NEW — `EProviderProtocolJsonConverter` is not the inverse of its own reader.** Serializing a `ProviderDefinition` writes the *enum name* (`"MessagesApi"`), deserializing accepts only the *manifest alias* (`"anthropiccompatible"`). Four of six protocol values therefore do not survive a round trip — they silently become `OpenAICompatible`. Three production write paths already emit the unreadable form: the library's own serializer (used by the consumer's LLM prompt builder), the manual editor's save, and the manual editor's protocol combo. The project's own schema validator rejects the resulting file. The plan asked for `EProviderProtocolJsonConverterTests` and `ProviderProtocolMapperTests`; neither exists, which is exactly why this survived.
2. **STILL OPEN — the Anthropic endpoint defect (`02` C1) is reproducible at HEAD**: `AddAiProviders()` gives the `anthropic` provider `MessagesEndpoint='chat/completions'`, so every Messages-API call is directed at `https://api.anthropic.com/v1/chat/completions`. The manifest declares `"messagesEndpoint": "messages"`, and that key has **zero readers in any `.cs` file in the solution**. Refactor 21's remediation list does not contain this item — the only Critical in the whole refactor set is currently untracked.

Beyond those, the recurring theme is **contract drift between the four independent copies of one rule** (the protocol vocabulary: mapper dictionary, editor dropdown, validator array, docs table) and **configuration the manifests advertise but the options types cannot receive** (`CatalogOptions` and `KeyQueryOptions` have no endpoint members at all).

Finally, a process finding with real cost: `b4925d3` already implemented roughly nine items that `refactors/refactor21/review-remediation.md` still lists as to-do, so executing that plan as written would re-do closed work while the open Criticals sit outside it.

**Priority:** findings N1 and N2 must be fixed before the first release, while the "nothing to break" directive still makes them free. N3–N9 are small, localized corrections to code Refactor 18 just introduced.

---

## Critical

### N1 (NEW). `ProviderDefinition` cannot round-trip through its own JSON converter — 4 of 6 protocol values are silently rewritten

**Location:** `AIProviderConnectLib/Models/EProviderProtocolJsonConverter.cs` L12-31, `AIProviderConnectLib/Models/ProviderProtocolMapper.cs` L5-23, `AIProviderConnectLib/Models/ProviderDefinition.cs` L58-60

The reader and the writer use two different vocabularies:

```csharp
// EProviderProtocolJsonConverter.cs L17-21 — read path: alias vocabulary only
var value = reader.GetString();
if (string.IsNullOrWhiteSpace(value))
    return EProviderProtocol.OpenAICompatible;
return ProviderProtocolMapper.FromJson(value);

// L29-30 — write path: enum names, with a comment claiming the opposite
// Write the enum name as camelCase for round-trip compatibility
writer.WriteStringValue(value.ToString());
```

`ProviderProtocolMapper.ByJsonValue` (L5-13) contains exactly five keys — `openaicompatible`, `anthropiccompatible`, `geminicompatible`, `githubmodelscompatible`, `hybridgateway` — and `FromJson` (L15-23) returns `OpenAICompatible` on a miss (L22). There is **no `ToJson`** anywhere in the solution (`git grep ToJson` returns only unrelated `ToJsonString`/`ExportToJson` hits), so nothing derives the wire form from the enum. `OpenAICompatible` and `HybridGateway` survive only because their enum name happens to equal their alias; `MessagesApi`, `KeyQuery`, `Catalog`, and `Native` do not.

**Runtime proof (probe, PROOF A3):**

```
=== ProviderDefinition serialize -> deserialize round-trip ===
  in-memory MessagesApi -> json "protocol":"MessagesApi" -> re-read OpenAICompatible *** CORRUPTED ***
  Native               serialized 'Native',        re-read OpenAICompatible  *** CORRUPTED ***
  OpenAICompatible     serialized 'OpenAICompatible', re-read OpenAICompatible  OK
  MessagesApi          serialized 'MessagesApi',   re-read OpenAICompatible  *** CORRUPTED ***
  KeyQuery             serialized 'KeyQuery',      re-read OpenAICompatible  *** CORRUPTED ***
  Catalog              serialized 'Catalog',       re-read OpenAICompatible  *** CORRUPTED ***
  HybridGateway        serialized 'HybridGateway', re-read HybridGateway     OK
```

This is the library's *published* model type (`[JsonConverter]` is applied at `ProviderDefinition.cs` L59), so the corruption is reachable by any consumer who serializes a definition — which the repo itself does three times:

| Write path | Code | Consequence |
| --- | --- | --- |
| LLM prompt for provider research | `ScraperTool/Services/AiDefinitionAnalyzer.cs` L177 `JsonSerializer.SerializeToNode(provider, options)` | The prompt tells the model its protocol is `"MessagesApi"`; the model echoes that token back in its patch, so the value lands in a manifest. |
| Manual editor save | `ScraperTool/ViewModels/ProviderManualEditorViewModel.cs` L1341 `Set(obj, ProviderJsonFields.Protocol, JsonValue.Create(def.Protocol.ToString()))` | Writing a provider from the editor persists `"protocol": "MessagesApi"` — a token the same screen's `Protocols` list (L188-196) does not contain, so reopening it loses the selection (`Protocol = def.Protocol.ToString()` at L1393 has no matching combo item, and L548 `ProviderProtocolMapper.FromJson(Protocol)` maps it to `OpenAICompatible`). One open/save/reopen cycle changes an Anthropic provider into an OpenAI one. |
| Anything that serializes the record | library converter, no opt-out | Same value written. |

**The project's own validator rejects the output.** `ScraperTool/Services/Validation/ProviderSchemaValidator.cs` L14-18 accepts only the alias vocabulary, so `CheckProtocol` reports `Unknown protocol 'MessagesApi'. Expected: Native, OpenAICompatible, AnthropicCompatible, …` for the file the editor just wrote. Meanwhile `CheckDeserialization` in the same validator *passes*, because the converter never fails — the two checks mask each other, and the failure surfaces as a validation issue on a file the tool itself produced.

**Why the test suite cannot see it:** `AIProviderConnectLib.Tests/Models/ProviderDefinitionTests.cs` tests the two directions separately and never crosses the boundary — `Serialize_ProducesValidJson` (L122-150) round-trips only `OpenAICompatible`, and `Protocol_DeserializesAllValidProtocols` (L152-176) feeds the alias vocabulary in. A single Theory over all six enum values (`serialize → deserialize → be the same`) fails immediately. The plan named that missing test class explicitly: design doc L78 lists "No tests for … `ProviderProtocolMapper`, or `EProviderProtocolJsonConverter`" as a defect to remove, and L144 promises both classes; checklist L161-162 are unchecked and neither file exists.

**Fix direction (one rule, one source):** add `ProviderProtocolMapper.ToJson(EProviderProtocol)` derived from the same dictionary that `FromJson` uses, and have `Write` call it; make `Native` an explicit alias or remove it from the editor/validator vocabularies; make the unknown-string branch fail (or at minimum report) instead of defaulting — `docs/concepts/wire-protocols.md` L20 currently *documents* the silent fallback, so the doc and the fix must move together. Then add the round-trip Theory.

### N2 (escalated, still open). The seeded endpoint for Messages-API providers is wrong at runtime

Confirmed live at HEAD, not as a code-reading inference but as observed output (probe, PROOF B / B3):

```
=== catalog view of anthropic (manifest omits chatEndpoint) ===
  ProviderDefinition(anthropic): Protocol=MessagesApi ChatEndpoint='chat/completions' ModelsEndpoint='models'

=== AddAiProviders() seeding for 'anthropic' (protocol=MessagesApi) ===
  MessagesApiOptions(anthropic): BaseUrl='https://api.anthropic.com/v1/' MessagesEndpoint='chat/completions' ModelsEndpoint='models'
  ai-providers/anthropic.json  : declares "messagesEndpoint": "messages" / declares NO chatEndpoint
```

`MessagesApiProvider` builds its URL straight from that value (`_options.MessagesEndpoint` at `Providers/MessagesApiProvider.cs` L37 and L68), so a `ChatAsync`/`StreamAsync` on `anthropic` posts to `https://api.anthropic.com/v1/chat/completions`.

Mechanism (all three links verified): `AIProviderServiceCollectionExtensions.cs` L141-143 assigns `messages.MessagesEndpoint = definition.ChatEndpoint`; `ProviderDefinition.cs` L28 gives `ChatEndpoint` a **non-nullable default** `EndpointDefaults.ChatCompletions`, so "key absent from JSON" is indistinguishable from "key present with OpenAI's value" and the `IsNullOrEmpty` guard at L142 can never fire; and the manifest's own `messagesEndpoint` key is inert — `git grep '"messagesEndpoint"' -- '*.cs'` returns **0 hits**, i.e. no `[JsonPropertyName]`, no constant, no reader.

Blast radius today is one manifest (protocol census: `OpenAICompatible` 33, `HybridGateway` 2, `AnthropicCompatible` 1, `GeminiCompatible` 1, `GitHubModelsCompatible` 1 = 38 files), but the code path is the entire Messages-API family, and the plan's governing directive ("nothing published yet") means fixing it now costs nothing.

**Cross-reference, and the actual problem with it:** `02` C1 described this defect and `02` M4 named its root cause ("manifests advertise configuration the code ignores"). `refactors/refactor21/review-remediation.md` does not contain it — not in Phase 1, 2, 3, or its Tests phase. So the only Critical in the ledger currently has no owner and no checklist line. The same gap applies to N1 and to every Major below (N3–N9).

---

## Major

### N3. Two of the five options types cannot receive any endpoint configuration the manifest offers

Probe PROOF B2, enumerating the shipped options classes by reflection:

```
  CatalogOptions                  endpoint properties: []
  KeyQueryOptions                 endpoint properties: []
  MessagesApiOptions              endpoint properties: [MessagesEndpoint, ModelsEndpoint]
  OpenAICompatibleProviderOptions endpoint properties: [ChatEndpoint, ModelsEndpoint]
  HybridGatewayProviderOptions    endpoint properties: [ChatEndpoint, ModelsEndpoint]
```

`SeedFromDefinition` acknowledges this in a comment (L149: "CatalogOptions and KeyQueryOptions: seed BaseUrl only").

That is a two-sources-of-truth defect *today*, not a hypothetical: `github-models.json` declares `chatEndpoint=inference/chat/completions` and `modelsEndpoint=catalog/models`, and `EndpointDefaults.Catalog` L13/L15 declares the same two strings again. Nothing asserts they agree, so a manifest edit silently does nothing while the constant keeps the old value — the same class of bug as N2, one notch further down. `gemini.json` declares `chatEndpoint=chat/completions`/`modelsEndpoint=models` which likewise reach no code. The providers hard-code those paths instead of reading configuration: `ModelCatalogProvider.cs` L28-33 uses `EndpointDefaults.Catalog.*`, and `KeyQueryProvider.cs` L32-33, L46, L63 use `EndpointDefaults.KeyQuery.*` / `EndpointDefaults.Models`.

Related and in the same five files: `KeyQueryOptions.cs` is an **empty subclass** (`public sealed class KeyQueryOptions : AIProviderOptions { }`) that exists only to satisfy the generic `Register<TOptions, TProvider>` seam; the `ApiVersion` defaults are duplicated literals in two option classes (`CatalogOptions.cs` `"2022-11-28"`, `MessagesApiOptions.cs` `"2023-06-01"`) while the manifest's `apiVersion` key (1 file) has no reader; `authenticationStyle` is declared by `anthropic.json` (`"ApiKeyHeader"`) and `gemini.json` (`"QueryString"`) with **no reader**, and the refactor replaced exactly those two behaviors with hard-coded headers — so the vocabulary for "this provider authenticates differently" exists in the data and nowhere in the code, and the next provider that needs it has no way to say so; and the type names split two conventions in one folder — `OpenAICompatibleProviderOptions`/`HybridGatewayProviderOptions` versus `MessagesApiOptions`/`KeyQueryOptions`/`CatalogOptions`.

### N4. `ModelCatalogOverrideDecorator` implements a capability it does not have

`Providers/ModelCatalogOverrideDecorator.cs` L15-16 declares `IAIProvider, IModelDiscoveryProvider, IStreamingChatProvider` unconditionally, then L77-84 forwards streaming only `if (_inner is IStreamingChatProvider streaming)` and otherwise throws `AiException(ConfigurationError, "… does not support streaming.")`. Since `IStreamingChatProvider.cs` L7 documents the type test as *the* capability check ("Use `is IStreamingChatProvider` to check at the call site") and no `SupportsStreaming` predicate exists anywhere, the decorator converts a caller's compile-time-meaningful test into a guaranteed runtime exception for any non-streaming inner provider — reachable through `AddProvider<TProvider>` plus `OverrideModels`. Latent today (all five built-in providers stream), so it is a design/contract defect rather than a live failure. Its `SupportsModelDiscovery => true` (L46) is a *documented, correct* decision (rationale L42-45: the decorator is only installed when overrides exist, and overrides alone produce a list) — not a capability lie. The fix is narrow: forward the interface implementation decision to the inner provider's capability, or drop `IStreamingChatProvider` from the decorator and expose a streaming pass-through method instead.

### N5. `InMemoryModelOverrideStore` hands out its live internal list, and the wrapping decision is one-shot

`Services/InMemoryModelOverrideStore.cs` L35-38 returns the `List<ModelOverride>` itself typed as `IReadOnlyList<>`; `Add` (L20-31) mutates that same list in place, with no synchronization on either the `Dictionary` (L12) or the list. `ProviderCatalog` follows the snapshot discipline the plan demanded (checklist L110, `.ToList()`/`.ToFrozenDictionary()` under `lock (_sync)`) — this class is the exception in the same phase. Compounding it, `AIProviderServiceCollectionExtensions.cs` L233-236 decides whether to wrap a provider from `store.Get(providerId).Count` during **first singleton resolution**, so an override `Add`ed after that first resolution is permanently invisible. Neither the class doc nor `IModelOverrideStore.cs` states that registration-time-only constraint; it is implicit in code that reads like a mutable store.

### N6. One protocol vocabulary, maintained in four unrelated places, with a hole at `Native`

| Copy | Values | Notes |
| --- | --- | --- |
| `ProviderProtocolMapper.cs` L5-13 | 5 aliases | the only one `FromJson` accepts; **no `native` key** |
| `ProviderManualEditorViewModel.cs` L188-196 | 6 (adds `Native`) | the editor's dropdown |
| `ProviderSchemaValidator.cs` L14-18 | 6 (adds `Native`) | the validation gate |
| `docs/concepts/wire-protocols.md` L12-21 | 5 + prose about `Native` | the published contract |

Consequence, verified by reading the mapper against the dropdown: selecting the offered `Native` in the editor stores `"Native"`, which `FromJson` misses, which yields `OpenAICompatible`. So the deliberate `Native` guard in the library — `RegisterProvider` L186-191, which throws a well-written "no built-in wire implementation, register a consumer provider" error — never fires for a manifest-declared provider, and a Native-designated provider quietly gets OpenAI wire semantics with a bearer header instead. (The guard is still reachable for a programmatically constructed `ProviderDefinition`, so the fix is the vocabulary, not the guard.) One enumerable source (the mapper, extended with `Native`, or an attribute on the enum) consumed by the dropdown, the validator, and the docs is the correction.

### N7. Consumer and library now disagree about the same provider's configuration — a fork, not a duplicate

`AIProviderServiceCollectionExtensions.cs` L178-221 dispatches protocol→provider and seeds endpoints from the definition; `ScraperTool/Services/AIProviderFactory.cs` L45-119 re-implements the same dispatch with its own option construction. The two copies have already diverged in a way that proves the seeding rule wrong rather than merely duplicated: the consumer copies only `ApiKey`/`BaseUrl`/`Enabled`/`ProviderId` (L79-85, L99-105, L109-115) and never touches endpoints, so it leaves `MessagesApiOptions.MessagesEndpoint` at its declared default `"messages"` — **the consumer reaches the correct Anthropic URL and the library reaches the wrong one for the same provider id and the same manifest.** The consumer's fallback `_ => new OpenAICompatibleProvider(...)` (L117) additionally swallows `Native` where the library throws, and its `DefaultHeaders` are a single hard-coded pair (L57-58) applied to every provider. `02` M2 flagged the duplication; what is new here is that the duplication is already producing two different behaviors from one input, and the shared logic is unreachable because `Register`/`SeedFromDefinition` are `private static`. Exposing one seeding/dispatch surface (or having the consumer call the library's builder) is the fix; keeping both copies "temporary" guarantees drift.

### N8. The remediation ledger (`refactor21`) no longer describes HEAD, in both directions

Verified against HEAD and the `b4925d3` diff:

- **Already implemented but still listed as to-do:** `docs/api-reference/interfaces.md`, `docs/concepts/provider-catalog.md`, `docs/faq.md`, `docs/getting-started/dependency-injection.md` rewrites; the `di-extensions.md` `Enabled` default row (`-| false |` → `+| true |`); the `OpenAICompatibleWireProtocol.cs` mangled XML doc; the `KeyQueryProvider.cs` vestigial `$"{…}"` interpolations; the `AiSetupViewModel` capability predicate (`is not IModelDiscoveryProvider discoveryProvider || !discoveryProvider.SupportsModelDiscovery`); the `ProviderCatalog` lock/`ReloadFromDisk` restructure (56 lines).
- **Still open, verified:** `Models/EPriceUnit.cs` is still named that (no `EModelPriceUnit.cs`); `IAIProviderFactory.GetProviders()` still exists (declaration L19, two implementations, **zero call sites** — `git grep '\.GetProviders('` is empty) while `ScraperTool/Services/AIProviderFactory.cs` L43 still returns `[]`; `SetBearerAuthentication` (`AIProviderBase.cs` L173-180) still does not trim; no `ProviderManifestSerializer.cs`; no `ITransientCredentialProviderFactory.cs`; `docs/concepts/wire-protocols.md` mentions only `AIProviderBase` (L82), not `OpenAICompatibleProviderBase`.
- **Unactionable as written:** Phase 3 (L72) tells the implementer to replace streaming type-tests with `provider.SupportsStreaming` — no interface in the library declares that member, so the instruction cannot be executed without first adding it (which is precisely the N4 correction, arriving by accident).
- **Contradicts AGENTS.md:** Phase 4 (L53) says to add a `## [Unreleased]` breaking section now. AGENTS.md ("packages without a first release") says CHANGELOG content is written when a release is prepared, and the r18 checklist asserts the same at L209 (`[x] Do not update CHANGELOG.md for Phases 1-6`). `CHANGELOG.md` at HEAD contains only `## [Unreleased]`, which is *correct*. The defect here is the plan item, not the file — `02`'s corresponding finding should be closed as "not a defect" rather than implemented.

The practical risk is wasted work plus false assurance: reading `refactor21` as "the known-defect list" implies nothing Critical is open, while N1/N2 are outside it.

### N9. The newly shared transport has no test on the shared parts, and the plan's completion claim is not accurate

Checklist L7 asserts "All phases (1-7) are implemented" while its own Phase 3 boxes (L79-105), Phase 4 (L109-126), Phase 5 (L131-147), Phase 7 (L171-190) are unchecked, and Phase 6 (L151-167) is checked only for `HybridGatewayProviderTests` (L153) and `KeyQueryProviderTests` (L155). L195 is checked: "`dotnet test` … passes **with the new tests included**", yet ten of the plan's test classes were never created — confirmed by directory listing: present are `AIProviderBaseTests` (60 lines), `HybridGatewayProviderTests`, `KeyQueryProviderTests`, `ModelCatalogOverrideDecoratorTests`, `ModelCatalogProviderTests` (65 lines), `ProviderCatalogTests`, `ModelOverrideMergerTests`, `AIProviderRegistrationBuilderTests`, protocol and model tests; absent are `OpenAICompatibleProviderTests`, `MessagesApiProviderTests`, `AIProviderServiceCollectionExtensionsTests`, `DefaultAIProviderFactoryTests`, `InMemoryModelOverrideStoreTests`, `MessageTextResolverTests`, `ProviderProtocolMapperTests`, `EProviderProtocolJsonConverterTests`, `KeyQueryWireProtocolTests`, `CatalogWireProtocolTests`.

The concrete coverage holes are where the risk is, not merely where the checklist is unchecked:

- `Providers/AIProviderBaseTests.cs` is one 16-case Theory about `ThrowIfErrorAsync` status mapping. The other three shared members introduced by the refactor — `ReadServerSentEventsAsync` (`AIProviderBase.cs` L137-168, the `[DONE]` sentinel, blank-line and non-`data:` skipping), `BuildRequest` (L97-126: URL composition, `DefaultHeaders`, bearer fallback, dispose-on-failure), and `EnsureProviderEnabled` (L83-91) — have no test of their own, even though all five providers now depend on them. Checklist L156 asked for exactly this.
- `ModelCatalogProviderTests.cs` asserts only the three configuration-error codes; the header contract (`Accept`, `api-version`), the organization-scoped inference endpoint, and `CatalogWireProtocol.ParseModels` (checklist L154) are untested.
- No test anywhere crosses the alias↔enum boundary (see N1), which is the only reason N1 is invisible to a green suite.

---

## Minor (real defects, not style)

- **m1 — `MessagesApiProtocol` emits two different bodies for the same conversation.** `MapRequest` (L17-18) forwards the payload as built by `MapPayload`, which writes `[MessagesApiPropertyNames.System] = … ? null : systemText` (L49); `MapStreamRequest` (L20-26) then removes the key when it is null (L24-25). So `"system": null` is sent on the non-streaming path and omitted on the streaming one. Whichever is intended, one path is wrong, and the asymmetry is an artifact of the extraction rather than a decision.
- **m2 — duplicated id-upsert rule.** `ProviderCatalog.Upsert` (L102-112) and `AIProviderRegistrationBuilder.Upsert` (L191-200) encode the same "match id case-insensitively, replace in place, else append" rule, and one `AddAiProviders(builder)` call runs both in sequence (builder `BuildDefinitions` → `catalog.Merge`). Case-insensitive id comparison is expressed three more ways: two `StringComparer.OrdinalIgnoreCase` collections (`ProviderCatalog.cs` L83, `AIProviderRegistrationBuilder.cs` L18/L22) and three manual `string.Equals(…, OrdinalIgnoreCase)` scans (`ProviderCatalog.cs` L104-105, `AIProviderRegistrationBuilder.cs` L178-179, L193-194). One `UpsertById` helper over a shared comparer removes the drift risk.
- **m3 — options stored twice in every provider.** Each of the five providers declares `private readonly XxxOptions _options` while `AIProviderBase.cs` L45 already exposes `Options`, and `EnsureProviderEnabled`/`BuildRequest` take an options argument that is not necessarily the instance the base holds (`MessagesApiProvider.cs` L33/L36, `KeyQueryProvider.cs` L31/L37). (`02` M3 identified the duplication; the divergence hazard is the part that matters, and the base's `IsEnabled`/`Options` reads versus the subclass's `_options` reads are already mixed.)
- **m4 — `RequiresApiKey` is unreachable configuration.** `AIProviderBase.cs` L35 declares `public virtual bool RequiresApiKey => true` and L89 consumes it, but `git grep RequiresApiKey -- '*.cs'` returns exactly those two hits: no override exists, and all five concrete providers are `sealed`, so no in-library override is possible. There is also no data path to it — no manifest key expresses "this provider needs no key" — so a local-server provider (the catalog ships `ollama`, `localai`, `lmstudio`, `jan`, `textgenwebui`) can only be used by handing the options a non-empty placeholder key, which is the pattern the API-key-in-URL fix was meant to end. (`02` M6; listed here because it is a live usability defect, and N4 shows the same "advertised capability, no mechanism" shape.)

---

## Refuted during this review — do not "fix" these

- **`AddHttpClient()` + `AddAiProviders()` works.** The library resolves a bare `HttpClient` (`AIProviderServiceCollectionExtensions.cs` L165) and the docs tell consumers to call `builder.Services.AddHttpClient()` (`docs/getting-started/quick-start.md` L9-21, `docs/api-reference/di-extensions.md` L107-113). Probe PROOF C shows that is sufficient in this stack: `sp.GetService<HttpClient>()` returns an instance after `AddHttpClient()` (not only `IHttpClientFactory`), and `GetRequiredKeyedService<IAIProvider>("openai")` resolves `OpenAICompatibleProvider` without a manual `AddSingleton(new HttpClient())`. No documentation or registration defect exists here.
- **`ModelCatalogOverrideDecorator.SupportsModelDiscovery => true`** is correct-by-design and documented (see N4's carve-out).
- **`SSEConstants.DataPrefixLength`** (checklist L113-114, unchecked) is satisfied in a better form: `AIProviderBase.cs` L160 uses `SSEConstants.DataPrefix.Length`, which cannot drift from the prefix. Close the box; change nothing.
- **`AiDefinitionAnalyzer` has no protocol-handling code of its own** (zero case-insensitive `protocol` matches). Its role in N1 is only through `SerializeToNode` at L177.
- Phase 1's behavioral fixes are all present and verified by reading: no `?key=` in any provider, `x-goog-api-key` carried via `HeaderNames.XGoogApiKey` (`KeyQueryProvider.cs` L85-86), `NoBaseUrl`/`NoApiKey` split, `HybridGatewayProvider` `AuthType` rejection (L28-38), `ProviderMissingConfiguration` retained in `AiErrorCodes` but unreferenced.

## Verified good

`AIProviderBase` really is the single home for request building, SSE reading, error mapping, and enablement checks — no provider copies them any more; `OpenAICompatibleProvider` is 25 lines of endpoint exposure; the five `RegisterXxx` methods are gone in favor of one generic `Register<>` plus a keyed/non-keyed bridge in `RegisterInstance`; `DefaultAIProviderFactory` is registered by `RegisterCore` (L85); the catalog locks and snapshots its reads and freezes `RegionalEndpoints`; `ProviderDefinition`/`ProviderResearchMetadata` split matches the plan (design L149) with the flat on-disk shape preserved; records with `init` replaced mutable setters; `EChatRole` follows the enum convention; `RegisterProvider` carries an explicit, well-written `Native` guard (it does not fire for manifest-declared providers — see N6 — but it protects the programmatic path); no `ScraperTool` or "AI Setup" string remains in the library (checklist L197-198); `HybridGatewayProviderTests` and the extended `KeyQueryProviderTests` are substantive; 192 library tests pass with zero live network calls.

---

## Recommendations, in order

1. **N1** — add `ProviderProtocolMapper.ToJson`, make `Write` use it, give `Native` a real disposition, make unknown tokens fail loudly, and land `ProviderProtocolMapperTests` + `EProviderProtocolJsonConverterTests` including the all-values round-trip Theory. Then re-point the editor dropdown/save (`ProviderManualEditorViewModel.cs` L1341/L1393) at the same mapper so the UI can no longer write an unreadable token.
2. **N2** — let `ProviderDefinition` distinguish "absent" from "default" (`string?` or an explicit `HasChatEndpoint`), give `MessagesApiOptions` its own manifest key path (read `messagesEndpoint`), and add the `AIProviderServiceCollectionExtensionsTests` seeding assertions the plan already specifies (checklist L157) — those two tests would have caught N1 and N2 at authoring time.
3. **N8** — restamp `refactors/refactor21/review-remediation.md` against HEAD before executing it: move the nine `b4925d3`-implemented items to done, drop the CHANGELOG item as AGENTS.md-compliant, add `SupportsStreaming` (or replace the predicate with `IsStreamingProvider`) so Phase 3 is executable, and — decisive — add N1, N2, N3, N4, N5, N6, N7 so the Criticals have an owner.
4. **N7** — make the protocol→provider→options mapping one callable surface (public seam in the library, consumed by `ScraperTool/Services/AIProviderFactory.cs`), and give `IAIProviderFactory` no member nobody calls (`GetProviders()`).
5. **N3, N5, N9** — per-protocol options carry their own endpoints (and one source for `ApiVersion`), `InMemoryModelOverrideStore` returns a snapshot and documents its lifetime, and the shared `AIProviderBase` members get direct tests. Fold m1–m4 in as the same edits.

---

## Appendix A — runtime probe transcript

Executed against `AIProviderConnectLib.Tests/bin/Debug/net10.0/AIProviderConnect.dll` from a throwaway console project (`net10.0`, `Microsoft.Extensions.DependencyInjection` + `Microsoft.Extensions.Http` 10.0.12). No sockets were opened; the only URL used is `https://test.example.com`. The probe project and its script were deleted after the run; no repository file other than this review was created or modified, and `git status` confirms no production-code change in this session.

```
=== PROOF A: EProviderProtocol JSON round-trip through the shipped converter ===
  Native                 writes 0   re-reads Native          OK
  OpenAICompatible       writes 1   re-reads OpenAICompatible OK
  MessagesApi            writes 2   re-reads MessagesApi      OK
  KeyQuery               writes 3   re-reads KeyQuery         OK
  Catalog                writes 4   re-reads Catalog          OK
  HybridGateway          writes 5   re-reads HybridGateway    OK
```
(PROOF A is the *unattributed* enum case: the converter is registered only on `ProviderDefinition.Protocol` — `ProviderDefinition.cs` L59 — and never added to a `JsonSerializerOptions`, so a bare enum value serializes as a number and `System.Text.Json`'s own numeric path handles it. Every manifest and every consumer path goes through the property, i.e. PROOF A3.)

```
=== PROOF A2: a manifest that already carries an enum-name protocol ===
  protocol:"MessagesApi" -> OpenAICompatible (expected MessagesApi)

=== PROOF A3: ProviderDefinition serialize -> deserialize round-trip ===
  in-memory MessagesApi -> json "protocol":"MessagesApi" -> re-read OpenAICompatible *** CORRUPTED ***
  Native serialized 'Native'            -> OpenAICompatible  *** CORRUPTED ***
  OpenAICompatible serialized 'OpenAICompatible' -> OpenAICompatible  OK
  MessagesApi serialized 'MessagesApi'  -> OpenAICompatible  *** CORRUPTED ***
  KeyQuery serialized 'KeyQuery'        -> OpenAICompatible  *** CORRUPTED ***
  Catalog serialized 'Catalog'          -> OpenAICompatible  *** CORRUPTED ***
  HybridGateway serialized 'HybridGateway' -> HybridGateway  OK

=== PROOF B3: catalog view of anthropic (manifest omits chatEndpoint) ===
  ProviderDefinition(anthropic): Protocol=MessagesApi ChatEndpoint='chat/completions' ModelsEndpoint='models'

=== PROOF B: AddAiProviders() seeding for 'anthropic' ===
  MessagesApiOptions(anthropic): BaseUrl='https://api.anthropic.com/v1/' MessagesEndpoint='chat/completions' ModelsEndpoint='models'
  ai-providers/anthropic.json  : declares "messagesEndpoint": "messages" / declares NO chatEndpoint

=== PROOF B2: endpoint members available on the per-protocol options ===
  CatalogOptions                  endpoint properties: []
  KeyQueryOptions                 endpoint properties: []
  MessagesApiOptions              endpoint properties: [MessagesEndpoint, ModelsEndpoint]
  OpenAICompatibleProviderOptions endpoint properties: [ChatEndpoint, ModelsEndpoint]
  HybridGatewayProviderOptions    endpoint properties: [ChatEndpoint, ModelsEndpoint]

=== PROOF C: documented quick-start flow = AddHttpClient() + AddAiProviders() ===
  sp.GetService<HttpClient>() after AddHttpClient() : instance
  sp.GetService<IHttpClientFactory>()               : instance
  GetRequiredKeyedService<IAIProvider>("openai")    : resolved OpenAICompatibleProvider

=== PROOF C2: AddSingleton(new HttpClient()) + AddAiProviders() ===
  resolved OpenAICompatibleProvider
```

## Appendix B — static census backing the findings

```
protocol values across ai-providers/*.json (38 files):
  OpenAICompatible 33 | HybridGateway 2 | AnthropicCompatible 1 | GeminiCompatible 1 | GitHubModelsCompatible 1

manifest keys with zero readers in any .cs file (git grep '"<key>"' -- '*.cs' -> 0 hits):
  messagesEndpoint (3 manifests) | authenticationStyle (2) | apiKeyHeaderName (1) | apiKeyPrefix (1) | apiVersion (1)

endpoint keys declared by manifests vs. receivable by options:
  anthropic.json      AnthropicCompatible       messagesEndpoint=messages  modelsEndpoint=models   -> messagesEndpoint unreachable (N2)
  github-models.json  GitHubModelsCompatible    chatEndpoint=inference/chat/completions modelsEndpoint=catalog/models -> unreachable (N3)
  gemini.json         GeminiCompatible          chatEndpoint=chat/completions modelsEndpoint=models -> unreachable (N3)
  opencode-go/zen     HybridGateway             + messagesEndpoint=messages                          -> no MessagesEndpoint member (hybrid is OpenAI-wire by design; noted, not a defect)

call sites: IAIProviderFactory.GetProviders() -> 0 | RequiresApiKey -> 2 (declaration + consumption)
EProviderProtocolJsonConverter registrations -> 1 (ProviderDefinition.Protocol)
tests: 192 passed, 0 failed (dotnet test AIProviderConnectLib.Tests)
```
