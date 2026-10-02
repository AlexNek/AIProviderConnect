# Changelog

ScraperTool — the AI Provider Catalog Researcher desktop application. It is published as
source in the public repository and is **not** part of the `AIProviderConnect` NuGet
package, so its changes are recorded here. The root [CHANGELOG.md](../CHANGELOG.md) drives
the package release notes and stays limited to what a package consumer installs.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Version headings match the package release whose source snapshot first carried the change;
work in progress lives under `## [Unreleased]`.

## [Unreleased]

### Added
- Manual editor: unified Endpoint Overrides editor for all endpoint data (path, optional base-URL override, optional protocol override). Legacy flat fields are auto-seeded into the editor on load (marked as non-deletable so essential endpoints cannot be accidentally removed), and edits to the flat properties sync back into the matching row so the data survives save. The protocol override ComboBox is disabled for `chat`, `models`, and `messages` rows — these operations are always served by the provider's root protocol class; only `decisions` and `embeddings` allow a per-endpoint protocol switch

### Fixed
- Model picker: models are browsed by **Name** and **Owner** in separate columns, and text filtering no longer matches fields the grid hides. The Name column shows the id without its owner prefix (`gpt-4o` for `openai/gpt-4o`) and is the value its header sorts on, so names line up alphabetically across owners instead of clustering under each provider prefix; the default price sort uses that name as its tie-break. The complete id — the value the API needs — stays one hover away in the cell tooltip and is what every copy path and the selection itself produce. The Modalities column tooltip shows the raw modality string that the filter searches, so typing `audio` matches the modality data instead of only the emoji rendered in the cell, and the filter box spells out the searched fields
- Model picker: a **Capabilities** column shows what the provider reported for each model, or `not reported` when it reported nothing, and "Filter by capability" is now disabled with a visible reason whenever no loaded model reports capabilities. Choosing a capability used to empty the list silently, because model discovery leaves the capability flags unset
- Model picker: the dialog header names the provider and states the loaded row count (`Select a model — OpenRouter · 464 models`, narrowing to `12 of 464` while filtering), so a partial list is visible instead of assumed complete
- AI Setup: the model list now follows the provider and API key chosen in the dialog, refetching when either changes. It used to query the **last saved** provider with the **last saved** key and then reuse that list for the rest of the dialog session, so evaluating a new provider (for example OpenRouter, whose catalog has 464 models) could show another provider's models, and unsaved credentials blocked the picker with "Configure API key first"
