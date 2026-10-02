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
- Manual editor: unified Endpoint Overrides editor for all endpoint data (path, optional base-URL override, optional protocol override). Legacy flat fields are auto-seeded into the editor on load (marked as non-deletable so essential endpoints cannot be accidentally removed), and edits to the flat properties sync back into the matching row so the data survives save. The protocol override ComboBox is disabled for `chat`, `models`, `messages`, and `embeddings` rows — these operations are always served by the provider's root protocol class; only `decisions` allows a per-endpoint protocol switch

### Fixed
- Model picker: models are browsed by **Name** and **Owner** in separate columns, and text filtering no longer matches fields the grid hides. The Name column shows the id without its owner prefix (`gpt-4o` for `openai/gpt-4o`) and is the value its header sorts on, so names line up alphabetically across owners instead of clustering under each provider prefix; the default price sort uses that name as its tie-break. The complete id — the value the API needs — stays one hover away in the cell tooltip and is what every copy path and the selection itself produce, and the filter box spells out the searched fields
- Model picker: a **Capabilities** column shows what the provider reported for each model, or `not reported` when it reported nothing, and "Filter by capability" is now disabled with a visible reason whenever no loaded model reports capabilities. Choosing a capability used to empty the list silently, because model discovery leaves the capability flags unset
- Model picker: the one text box is split into **Filter by name or owner** and **Filter by description**, each matching — and painting its match in — only the columns it owns; modality and capability keep their dropdowns. The single box used to search id, description and modality words at once, so typing `text` listed 115 of 464 models for a word visible nowhere: it lived in the truncated tail of the description and inside the emoji-only modality icons. A pasted id (`openai/gpt-4o`) is split at the slash, so the owner half is matched and painted in the Owner column and the rest in the Name column
- Model picker: a count line directly above the list states which provider the rows came from and how many are showing — `OpenRouter — 464 models`, tightening to `OpenRouter — 12 of 464 models shown` while filtering — so a filtered or partial list is visible instead of something to trust
- AI Setup: the model list now follows the provider and API key chosen in the dialog, refetching when either changes. It used to query the **last saved** provider with the **last saved** key and then reuse that list for the rest of the dialog session, so evaluating a new provider (for example OpenRouter, whose catalog has 464 models) could show another provider's models, and unsaved credentials blocked the picker with "Configure API key first"
