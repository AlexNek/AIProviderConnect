# Refactor 22 — Second-pass implementation review

Scope: all code and documentation changes attributed to refactor 22 (streaming DRY, constants extraction, clean-code fixes, documentation corrections). The first review (`01-refactor22-implementation-review.md`) was spot-checked against the commit diff; this pass reads every touched file end-to-end and cross-references the plan's stated goal against the delivered state.

Plan under review: `refactors/refactor22/library-code-quality-and-docs-accuracy.md`.

Evidence: `dotnet build` clean; library tests green. All claims verified by reading the current source and documentation files.

## Verdict

The code changes are correct and behavior-preserving. The documentation corrections cover the items explicitly enumerated in the plan. However, several stale references survive in files the implementation either missed or only partially updated, and the constants extraction left a mixed constants/literals state in the protocol files. **Five documentation findings and two code findings** are recorded below; three of the documentation findings are in files outside the plan's explicit enumeration but contradict the same corrections the plan applied elsewhere.

---

## Findings

### F1 — `dependency-injection.md` still documents removed `GetProviders()` (MEDIUM)

`docs/getting-started/dependency-injection.md` lines 96–98:

> `IAIProviderFactory`. It exposes `GetProvider(providerId)` and `GetProviders()`, resolving the keyed `IAIProvider` registrations from the container.

`GetProviders()` was removed from `IAIProviderFactory` in refactor 21. The current interface (`Abstractions/IAIProviderFactory.cs`) declares only `GetProvider(string providerId)`. The DI page was correctly updated in the "Resolving Providers" section (lines 75–92 show `IAIProviderFactory`), but the `IAIProviderFactory` subsection still advertises the removed method.

### F2 — `interfaces.md` code block still shows removed `GetProviders()` (MEDIUM)

`docs/api-reference/interfaces.md` lines 103–108:

```csharp
public interface IAIProviderFactory
{
    IAIProvider GetProvider(string providerId);
    IReadOnlyList<IAIProvider> GetProviders();
}
```

The `GetProviders()` line does not exist in the shipped interface. This is the API reference page — developers copy from code blocks — so the stale signature is more harmful than a prose mention.

### F3 — `tool-calling.md` still uses `ChatRole` (LOW)

`docs/chat/tool-calling.md` line 62:

> Append the assistant message that carried the tool calls, then one `ChatRole.Tool` message per result

The plan explicitly listed this file for the `ChatRole` → `EChatRole` correction. The code examples in the same file (lines 35, 67, 73) were correctly updated to `EChatRole`; only the inline-code reference on line 62 was missed.

### F4 — `quick-start.md` uses stale `ChatRole` and `IEnumerable<IAIProvider>` pattern (MEDIUM)

`docs/getting-started/quick-start.md`:

- Line 65: `ChatRole.User` — should be `EChatRole.User`.
- Lines 56–60: The resolution example uses `IEnumerable<IAIProvider>` with `.First(p => p.Id == "openai")` linear scan. This is the exact pattern the plan identified as stale in `dependency-injection.md` and replaced with `IAIProviderFactory.GetProvider(id)`. The quick-start was not in the plan's explicit file list, but it now contradicts the corrected DI guidance: one page tells consumers to use the factory, the other shows the obsolete collection-scan approach.

### F5 — `wire-protocols.md` uses stale `ChatRole` (LOW)

`docs/concepts/wire-protocols.md` line 43:

> `ChatRole.System` messages are folded into the top-level `system` field.

Should be `EChatRole.System`. Not in the plan's explicit file list but the same rename correction applies.

### F6 — Constants extraction left protocol files half-migrated (LOW, carries over from r22/01 F2)

The plan's stated goal was to remove hardcoded literals from the two protocol files "for consistency and maintainability." The enumerated key list was applied, but sibling literals in the same methods remain raw:

- `MessagesApiProtocol.ParseStreamChunk`: `"content_block_delta"` (line 196), `"delta"` (line 197), `"message_stop"` (line 203).
- `KeyQueryWireProtocol.ParseStreamChunk`: `"finishReason"` (line 157).

Both files now mix constants and inline literals in the same parsing methods. The previous review flagged this; no action was taken. Either finish the centralization or record a one-line rationale for why discriminator/event values stay literal so the next reader does not wonder why the rule was applied unevenly.

### F7 — `CatalogWireProtocol` still uses all-raw string literals (LOW, informational)

`Protocols/CatalogWireProtocol.cs` uses hardcoded `"id"`, `"name"`, `"publisher"` throughout. This file was never in the plan's scope, but after the other two protocols were migrated, the inconsistency is more visible. Flagging only — no action required unless a follow-up pass is planned.

---

## Verified correct (spot-checked)

- **`StreamCoreAsync` template method** — `AIProviderBase.StreamCoreAsync` consolidates the `SendAsync(ResponseHeadersRead)` → `ThrowIfErrorAsync` → `ReadServerSentEventsAsync` → chunk-parse → yield scaffold. All three streaming providers delegate to it. No duplicate transport scaffold survives outside `AIProviderBase`.
- **`GetStatusMessage` consolidation** — `502 or 503` share one switch arm; `504` kept separate with its own text.
- **Tool-call double lookup fix** — `OpenAICompatibleWireProtocol.ParseResponse` and `ParseStreamChunk` each look up the `"function"` property once per tool-call element and extract both `name` and `arguments` from the same `JsonElement`.
- **Dead code removal** — `HeaderNames.Authorization` removed (zero references); `Headers.Remove` calls removed from `MessagesApiProvider.ApplyHeaders`.
- **Exception filter simplification** — `ProviderCatalog.ReloadFromDisk` uses `catch (JsonException ex)`.
- **`SeedFromDefinition` consolidation** — `IChatAndModelsEndpointOptions` internal interface merges the two identical switch arms. Both `OpenAICompatibleProviderOptions` and `HybridGatewayProviderOptions` implement it.
- **`MessagesApiPropertyNames` / `KeyQueryPropertyNames`** — Constants classes created and used in parsing methods for the enumerated keys.
- **Plan-enumerated doc fixes** — `EChatRole` in `models.md`, `chat-completions.md`, `faq.md`; `IReadOnlyList<>` types; `EModelPriceUnit?`; `ReasoningContent` / `ToolCalls` added to `StreamingChatChunk` and `ChatCompletionResponse` tables; `IAIProviderFactory` resolution pattern in `dependency-injection.md`; `provider is IStreamingChatProvider` in `faq.md`; Mermaid diagram corrected in `index.md`; four-protocol listing in `installation.md`. All verified against current source.

---

## Summary

| # | File | Issue | Severity |
| --- | --- | --- | --- |
| F1 | `dependency-injection.md` | `GetProviders()` still mentioned in prose | MEDIUM |
| F2 | `interfaces.md` | `GetProviders()` in code block — developers will copy it | MEDIUM |
| F3 | `tool-calling.md` | `ChatRole.Tool` inline reference missed | LOW |
| F4 | `quick-start.md` | `ChatRole.User` + stale `IEnumerable<IAIProvider>` pattern | MEDIUM |
| F5 | `wire-protocols.md` | `ChatRole.System` inline reference | LOW |
| F6 | Protocol files | Half-migrated constants/literals in streaming parsers | LOW |
| F7 | `CatalogWireProtocol` | All-raw literals (out of plan scope, informational) | LOW |

## Suggested next steps

1. Fix F1 and F2 — remove `GetProviders()` from both documentation pages; the interface no longer exposes it.
2. Fix F3, F4, F5 — rename remaining `ChatRole` → `EChatRole`; update `quick-start.md` resolution example to use `IAIProviderFactory.GetProvider(id)`.
3. Decide F6: either finish centralizing the remaining streaming discriminator literals or document why they stay inline.
