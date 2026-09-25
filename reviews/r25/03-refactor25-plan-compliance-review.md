# Refactor 25 implementation review — plan compliance audit (03)

- **Date:** 2026-09-25
- **Plan under review:** `refactors/refactor25/ai-provider-connect-lib-clean-code-consolidation.md` ("plan x1")
- **Review target:** commit `d0cf981` "r25 implementation" (42 files; working tree otherwise clean except this untracked `reviews/r25/` folder)
- **Method:** full CodeReview pass over the commit diff, followed by independent re-verification of every finding against current source (file reads + repo-wide greps), per this repository's review-discipline rules.
- **Relation to prior reviews:** `01-implementation-review.md` and `02-refactor25-implementation-review.md` predate this pass; the findings below were verified against the current source independently and are not carried over unexamined.

## Verdict

**FAIL** — one unresolved Error remains.

## Coverage summary

| Metric | Count |
|---|---|
| Plan requirements | 30 |
| Implemented correctly | 27 |
| Missing | 1 (new unit tests — F-1) |
| Incorrect | 2 (retry log argument alignment — F-2; residual `features/10` ProviderId docs — F-3) |
| Unknown | 0 |
| Errors | 1 |
| Warnings | 2 |

## Findings

### F-1 — Plan-mandated new unit tests are missing

- **Severity:** Error
- **Plan requirement:** Mandatory implementation rules — "Add new unit tests for `ProtocolParsingHelpers`, retry behavior, and logging output." (also D2, D8, D9)
- **Affected location:** `AIProviderConnectLib.Tests/` (whole project)
- **Evidence:** Grep over the test project for `ProtocolParsingHelpers`, `SafeGetString`, `ParseModelArray`, `TryExtractApiKeyHeaderName`, `ResolveSource`, `MapContentParts`, `MaxRetryCount`, `RetryDelay`, `ResiliencePipeline`, `NullLogger`, `ILogger`, `TestLogger`, `FakeLogger` → **0 matches**. The only logging-related test changes are plumbing (`services.AddLogging()` in `AIProviderRegistrationBuilderTests` to satisfy the new constructor), not behavior assertions.
- **Explanation:** The refactor adds three non-trivial new behaviors — shared parsing helpers used by all four `ParseModels` implementations, Polly retry activated by `MaxRetryCount`/`RetryDelay` with `RateLimited`/`NoServer` predicates, and Debug/Warning/Error transport logging. None has any test coverage; the plan explicitly required it.
- **Required correction:** Add:
  1. `ProtocolParsingHelpersTests` — `SafeGetString` present / missing / non-string; `ParseModelArray` root-property vs direct-array extraction, empty-`Id` filtering.
  2. Retry tests with a fake `HttpMessageHandler` — no retry at `MaxRetryCount = 0`; retry on 429/5xx with `MaxRetryCount > 0`; no retry on non-retryable 4xx; streaming path not retried.
  3. Logging tests with a fake/captured `ILogger` — Debug request/response, Warning retry, Error final failure.

### F-2 — Retry log template arguments misaligned with placeholders

- **Severity:** Warning
- **Plan requirement:** D8/D9 — "OnRetry: a callback that logs the retry attempt at `Warning` level."
- **Affected location:** `AIProviderConnectLib/Providers/AIProviderBase.cs`, `BuildResiliencePipeline`, L104–L112
- **Evidence:**

  ```csharp
  Logger.LogWarning(
      "Provider '{ProviderId}': retry {Attempt}/{MaxAttempts} after {Delay}ms — {Code}",
      ProviderId, args.AttemptNumber + 1, Options.MaxRetryCount,
      args.Outcome.Exception is AiException aiEx ? aiEx.Code : "unknown",
      args.Outcome.Exception?.Message);
  ```

  Five placeholders, five arguments — but `{Delay}` is bound to the **error code** and `{Code}` to the **exception message**; no delay value is ever logged.
- **Explanation:** The rendered output is objectively wrong (e.g. "after RateLimitedms — The rate limit…"), and the structured-log properties `Delay`/`Code` carry mismatched values. The plan's core requirement (Warning-level retry logging) is met, so this is reported as a Warning rather than an Error — but it is a real output defect in new code.
- **Required correction:** Insert a delay value for `{Delay}` (e.g. `Options.RetryDelay.TotalMilliseconds` or Polly's computed delay argument) so the argument order matches the template.

### F-3 — `AIProviderOptions.ProviderId` references remain in `features/10`

- **Severity:** Warning
- **Plan requirement:** D10 / Mandatory doc rules — "remove `AIProviderOptions.ProviderId` references (`docs/api-reference/di-extensions.md`, `docs/getting-started/dependency-injection.md`, `features/10/custom-provider-registration.md`)"
- **Affected location:** `features/10/custom-provider-registration.md` L12, L114
- **Evidence:**
  - L12: "DI sets only `ProviderId`; `AIProviderOptions.BaseUrl` defaults to `string.Empty`…"
  - L114: "- [ ] In each `RegisterXxx` method, after `.Configure(o => o.ProviderId = providerId)`, chain …"

  The two `docs/` files were updated (grep over `docs/` for the deleted-symbol patterns → 0 matches); the commit did edit this file (`DefaultMaxTokens` removal) but left the `ProviderId` references.
- **Explanation:** L114 instructs a future implementer to chain configuration after a call on a property that no longer exists. (L12 is the feature's historical "current defects" prose, which the plan nonetheless listed for cleanup.)
- **Required correction:** Update both spots to describe the current flow — `providerId` as constructor parameter plus `SeedFromDefinition` seeding — and drop the `.Configure(o => o.ProviderId = …)` wording.

## Checked and NOT reported (verified correct)

- `ResiliencePipeline.Empty` short-circuit when `MaxRetryCount <= 0` vs the plan's "build the pipeline unconditionally" wording — functionally identical transparent pass-through; equally valid implementation.
- `ImageSource` modeled as a separate file — complies with the one-type-per-file rule.
- Leftover-symbol sweep: `HybridGatewayProvider` class, `AuthSchemes`, `HeaderNames`, `ChatEndpointPattern`/`StreamEndpointPattern`, `DefaultMaxTokens`, `options.ProviderId` — **0 hits** in library, ScraperTool, tests, and docs.
- Catalog caching: all three snapshot fields (`_allSnapshot`, `_withModelDiscoverySnapshot`, `_withDynamicCatalogSnapshot`) invalidated via `InvalidateSnapshots()` under `lock` in both `Merge` (L131) and `ReloadFromDisk` (L247); mutable nullable fields, lazy rebuild under lock, `IReadOnlyList<T>` returns — matches D6 exactly.
- `ThrowIfErrorAsync` unmodified; `StreamCoreAsync` not wrapped in the resilience pipeline; `KeyQueryOptions` does **not** implement `IChatAndModelsEndpointOptions`; `IChatAndModelsEndpointOptions` accessed only via constructor-time cast into private string fields — matches D3/D6 and the "Do not" list.
- `HybridGatewayProviderTests.cs` renamed to `OpenAICompatibleProviderTests.cs` with all three `[InlineData]` operations retained, constructing `OpenAICompatibleProvider` from `HybridGatewayProviderOptions`; `CatalogWireProtocolTests` `"Accept"` inlining; endpoint-rename updates in `KeyQueryProviderTests`, `KeyQueryWireProtocolTests`, `AIProviderRegistrationBuilderTests` — matches the plan's test-update rules.
- No CHANGELOG finding: the package has no first release; the project rule states changelog content is written only when a release is prepared.

## Per-decision coverage

| Item | Status | Evidence |
|---|---|---|
| D1 — transport consolidation | Verified | `Providers/OpenAICompatibleProviderBase.cs` delegates `ChatAsync`/`GetModelsAsync` to `SendChatAndParseAsync`/`SendGetModelsAndParseAsync`; `StreamAsync` still uses `StreamCoreAsync` |
| D2 — shared protocol helpers | Verified | `Protocols/ProtocolParsingHelpers.cs` (internal) with `SafeGetString`, `ParseModelArray`, `TryExtractApiKeyHeaderName` + content-part dispatch; all four protocols refactored to use them |
| D3 — provider consolidation | Verified | `HybridGatewayProvider.cs` deleted; DI + `ScraperTool/Services/AIProviderFactory.cs` use `OpenAICompatibleProvider` with `HybridGatewayProviderOptions`; widened ctor with `ArgumentException` guard; 9 doc files updated, 0 residual class references |
| D4 — magic-string elimination | Verified | `OpenAICompatiblePropertyNames.JsonSchema` added and used; assistant/tool comparisons via `EChatRole…ToString().ToLowerInvariant()`; output wire literals `"text"`/`"image_url"` kept as required |
| D5 — error-path generalization | Verified | `private static readonly` path array `["error.message", "message"]` walked generically; signature unchanged; no virtual extensibility point |
| D6 — minor cleanups | Verified | `ProviderCatalog` parameterless ctor inlined; `AuthSchemes.cs`/`HeaderNames.cs` deleted with `"Bearer"`/`"Accept"` inlined at single use sites; `EProviderProtocol.Native` XML doc added; endpoint renames complete; snapshot caching as specced |
| D7 — options SRP cleanup | **Partial** | Code fully compliant (`ProviderId` removed from options, ctor parameter, `SetApiKeyHeader` instance method, DI updated, XML doc cleaned); `features/10` docs stale (F-3) |
| D8 — logging integration | **Partial** | `protected ILogger Logger` with `NullLogger` default, DI injection, `OpenAICompatibleProviderBase` forwarding, Debug/Error transport logs present; no logging tests (F-1); F-2 template defect |
| D9 — retry support | **Partial** | `Polly.Core` referenced; `AddRetry` with `RateLimited`/`NoServer` predicate, exponential backoff, Warning-level `OnRetry`; streaming excluded; `ThrowIfErrorAsync` untouched; no retry tests (F-1) |
| D10 — DefaultMaxTokens removal | Verified | Property + `<remarks>` removed; `MapRequest`/`MapStreamRequest` parameter dropped; `MapPayload` emits `request.MaxTokens` directly; all 4 listed doc files updated |
| Compatibility items | Verified | All breaking changes implemented as stated; `Microsoft.Extensions.Logging.Abstractions` + `Polly.Core` references added to csproj; library builds and tests pass |
| Mandatory test updates | Verified | Renames, file rename `HybridGatewayProviderTests.cs` → `OpenAICompatibleProviderTests.cs`, literal inlining, endpoint renames all present |
| Mandatory new tests | **Not done** | F-1 |
| Documentation updates | **Partial** | `docs/` complete; `features/10` residual `ProviderId` references (F-3) |

## Build/test evidence

- `dotnet build AIProviderConnectLib/AIProviderConnectLib.csproj` — succeeds.
- `dotnet test AIProviderConnectLib.Tests/AIProviderConnectLib.Tests.csproj` — passes.
- `dotnet build ScraperTool/ScraperTool.csproj` — succeeds (consumer of the widened constructor).
