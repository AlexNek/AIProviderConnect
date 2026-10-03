# Changelog

ScraperTool — the AI Provider Catalog Researcher desktop application. It is published as
source in the public repository and is **not** part of the `AIProviderConnect` NuGet
package, so its changes are recorded here. The root [CHANGELOG.md](../CHANGELOG.md) drives
the package release notes and stays limited to what a package consumer installs.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Version headings match the package release whose source snapshot first carried the change;
work in progress lives under `## [Unreleased]`.

## [Unreleased]

## [1.2.0] - 2026-10-03

### Added
- New **Model Test** panel (dashboard tile under Settings) with two tabs — **Embeddings** and **Decision** — that use the provider and API key already configured in AI Setup. Each tab lets the user select a model (filtered by the matching capability), send a real request, and see the response with token usage and cost. Cumulative counters track total tokens and cost across repeated calls
- Manual editor: Endpoint Overrides editor gained an **Additional Query Parameter** column between Path and Base-URL. The value is persisted into the endpoint definition's `additionalQueryParameter` field and round-trips through load/save; an auto-seeded default row with only a query-parameter override is preserved on save (it is no longer treated as a no-op)
- Manual editor: unified Endpoint Overrides editor for all endpoint data (path, optional base-URL override, optional protocol override). Legacy flat fields are auto-seeded into the editor on load (marked as non-deletable so essential endpoints cannot be accidentally removed), and edits to the flat properties sync back into the matching row so the data survives save. The protocol override ComboBox is disabled for `chat`, `models`, `messages`, and `embeddings` rows — these operations are always served by the provider's root protocol class; only `decisions` allows a per-endpoint protocol switch
- Model picker: `LoadModels` accepts an optional `requiredCapability` parameter so the caller can name the default capability selection (e.g. `TextGeneration` for chat model picking); the selector opens on that choice instead of All. The filter is not enforced until Feature 17 populates model capabilities

### Changed
- Model picker: the Modalities column is split into **Modalities In** and **Modalities Out**, each rendering icons only — the raw provider string (e.g. `text+image->image`) no longer appears inline; it stays on the cell tooltip and is what the text filter searches. The `text` token is included (📝). New modality tokens are recognised: `embeddings` (🔢), `decisions` (🎯), `rerank` (📊), `speech`/`transcription` (🔊, reuses the audio icon). The legend bar at the bottom of the grid lists every recognised token. The **Capabilities** column sits directly after Modalities Out and shows provider-reported flags when available, or capabilities derived from the modality string otherwise (`image` in → ImageRecognition, `image` out → ImageGeneration, etc.)
- Model picker: the capability filter uses three-state semantics — `null` (not reported), `None` (reported as having none), or a specific value — so the filter acts only on data the provider actually returned, not on a fabricated default
- Model picker: the capability filter dropdown now lists exactly **All, Chat, Embedding, Decision** (Chat maps to `TextGeneration`) instead of every `EModelCapability` member, matching the model-kind selector the feature design specifies

### Fixed
- Model picker: the provider now seeds endpoint paths (including `additionalQueryParameter`) from the catalog definition before making the discovery request, so OpenRouter's `output_modalities=all` widening parameter is actually sent. The factory used to create options with only `ApiKey`/`BaseUrl`/`Enabled`/`DefaultHeaders`, leaving `ModelsEndpoint` at its default `"models"` and ignoring the query parameter declared in the provider JSON
- Model picker: models are browsed by **Name** and **Owner** in separate columns, and text filtering no longer matches fields the grid hides. The Name column shows the id without its owner prefix (`gpt-4o` for `openai/gpt-4o`) and is the value its header sorts on, so names line up alphabetically across owners instead of clustering under each provider prefix; the default price sort uses that name as its tie-break. The complete id — the value the API needs — stays one hover away in the cell tooltip and is what every copy path and the selection itself produce, and the filter box spells out the searched fields
- Model picker: a **Capabilities** column shows what the provider reported for each model, or `not reported` when it reported nothing, and "Filter by capability" is now disabled with a visible reason whenever no loaded model reports capabilities. Choosing a capability used to empty the list silently, because model discovery leaves the capability flags unset
- Model picker: the one text box is split into **Filter by name or owner** and **Filter by description**, each matching — and painting its match in — only the columns it owns; modality and capability keep their dropdowns. The single box used to search id, description and modality words at once, so typing `text` listed 115 of 464 models for a word visible nowhere: it lived in the truncated tail of the description and inside the emoji-only modality icons. A pasted id (`openai/gpt-4o`) is split at the slash, so the owner half is matched and painted in the Owner column and the rest in the Name column
- Model picker: a count line directly above the list states which provider the rows came from and how many are showing — `OpenRouter — 464 models`, tightening to `OpenRouter — 12 of 464 models shown` while filtering — so a filtered or partial list is visible instead of something to trust
- AI Setup: the model list now follows the provider and API key chosen in the dialog, refetching when either changes. It used to query the **last saved** provider with the **last saved** key and then reuse that list for the rest of the dialog session, so evaluating a new provider (for example OpenRouter, whose catalog has 464 models) could show another provider's models, and unsaved credentials blocked the picker with "Configure API key first"

## [1.1.0]
no additional file