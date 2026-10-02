# Architecture Rules

## Project Structure

```
AIProviderConnect_private/
├── AIProviderConnectLib/          (main library — NuGet package)
├── AIProviderConnectLib.Tests/    (unit tests)
├── GraphVisualization/          (reusable WPF universal graph viewer — mirrored to public repo)
├── ScraperTool/                  (WPF research tool & package demo — mirrored to public repo)
├── ScraperTool.Tests/            (ScraperTool unit tests — mirrored to public repo)
└── AIProviderConnect.sln
```

## Layer Dependency Direction (strict one-way)

```
ScraperTool  →  AIProviderConnectLib  ←  AIProviderConnectLib.Tests
      │                (library)              ScraperTool.Tests → ScraperTool
      ↓
GraphVisualization  (pure WPF graph viewer — no domain dependencies)
```

- **AIProviderConnectLib** — abstractions, models, provider implementations, wire protocols, DI extensions. This is the published NuGet package.
- **AIProviderConnectLib.Tests** — references AIProviderConnectLib. Unit tests only. Not published.
- **GraphVisualization** — reusable, domain-agnostic WPF graph viewer library: generic graph model (pre-styled nodes/edges), pure hierarchical layout engine, DOT export, interactive pan/zoom control with a caller-supplied legend slot. Knows nothing about decision trees or AiCleverness; domain adapters live in ScraperTool. Mirrored to the public repo as source.
- **ScraperTool** — WPF tooling used to research/maintain the provider catalog; it also serves as the demo application for the AIProviderConnect package. Mirrored to the public repo as source.

## Forbidden in ScraperTool

- ❌ Test assertions — tests belong in ScraperTool.Tests
- ❌ Secrets, API keys, or personal absolute file paths — ScraperTool source is mirrored to the public repo

## Forbidden in GraphVisualization

- ❌ References to AiCleverness, ScraperTool, or any domain model — the library is universal; domain adapters belong in the consuming app
- ❌ Hard-coded domain semantics (e.g. decision-tree node types, verdict colors)

## Forbidden in AIProviderConnectLib.Tests / ScraperTool.Tests

- ❌ Live network calls in unit tests — use a fake `HttpMessageHandler` or Moq mocks
- ✅ Integration tests that hit the network must be in a separate category/trait

## Forbidden in AIProviderConnectLib (library)

- ❌ References to ScraperTool, GraphVisualization, or test projects (circular dependency)
- ❌ Console.WriteLine or any UI output
- ❌ Hard-coded secrets or API keys

## One type per file (MANDATORY)

- ❌ NEVER put more than one type (class, record, struct, interface, enum) in a single `.cs` file
- ✅ Each type gets its own file, named after the type

## Security — test data rules (MANDATORY)

- ❌ NEVER use real hostnames, usernames, passwords, or API keys in tests or documentation
- ❌ NEVER connect to a live service in unit tests — tests must be hermetic
- ✅ Use obviously fake values: `https://test.example.com`, `fake-api-key`
- ✅ Mock HTTP responses with a fake `HttpMessageHandler` or Moq

## Test conventions

- AAA pattern (Arrange / Act / Assert)
- xUnit as test framework
- FluentAssertions for assertions
- Moq for mocking interfaces
- One test class per production class, named `{ClassName}Tests`

## CHANGELOG maintenance (release workflow)

`CHANGELOG.md` follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.

### Which changelog records what (MANDATORY)

The root `CHANGELOG.md` is the **NuGet package** record: `.github/workflows/release.yml` extracts its
`## [X.Y.Z]` section and publishes it as the package's GitHub Release notes, so it may only contain
changes a package consumer receives — `AIProviderConnectLib` (public API, wire protocols, DI,
provider catalog data, XML docs) and the published `docs/` site.

`ScraperTool/CHANGELOG.md` is the **desktop tool** record. ScraperTool is mirrored to the public
repository as source but is never installed with the package, so its changes (views, view models,
controls, `ScraperTool/Services`, `ScraperTool/Data`, `Config/`) go there and must not appear in
package release notes.

- A change that alters both surfaces is recorded in **both** files, each bullet describing only its own surface
- Both files follow the same format and category rules, and both are renamed to the same `X.Y.Z` when a release is prepared — the tool heading marks the package release whose source snapshot first carried the change

### Format rules

- Top-level heading: `# Changelog`
- Each release is a level-2 heading: `## [X.Y.Z] - YYYY-MM-DD`
- Work-in-progress lives under: `## [Unreleased]`
- Change categories (level-3 headings): `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`
- Bottom of the file has reference links: `[X.Y.Z]: https://github.com/AlexNek/AIProviderConnect/releases/tag/vX.Y.Z`

### Algorithm: when making code changes

The `[Unreleased]` section must always describe the **final user-facing state relative to the last released version** — never the development history.

1. **Every user-visible change** (feature, fix, breaking change) must be covered under `## [Unreleased]` in the appropriate category
2. **New feature in development** — maintain ONE bullet per feature that describes its complete current state; do NOT add a new bullet per incremental change
3. **Change to an already-released behavior** — one bullet per issue; if revisited, update the existing bullet
4. **Fix/change that only concerns an unreleased feature** — fold it into the feature's bullet; never add a separate `Fixed`/`Changed` entry
5. Keep bullets concise but descriptive enough for end users
6. Do NOT modify existing versioned sections unless explicitly asked

### Algorithm: preparing a release (when asked to tag/release version X.Y.Z)

1. Rename `## [Unreleased]` → `## [X.Y.Z] - YYYY-MM-DD` (use actual date) — in the root `CHANGELOG.md` and, when it has entries, in `ScraperTool/CHANGELOG.md`
2. Add a new empty `## [Unreleased]` section above it (same files)
3. Update the reference links at the bottom:
   - Change `[Unreleased]` link to compare `vX.Y.Z...HEAD`
   - Add `[X.Y.Z]: https://github.com/AlexNek/AIProviderConnect/releases/tag/vX.Y.Z`
4. The release workflow (`.github/workflows/release.yml`) will automatically:
   - Build, test, and pack the NuGet package
   - Publish to NuGet.org
   - Create a GitHub Release with auto-generated release notes

### Important

- CHANGELOG.md must exist in the repo root (the release workflow reads it at checkout path)

## Edit discipline (MANDATORY)

- Make surgical edits only: change exactly what the task requires.
- Never refactor, "simplify", or restructure adjacent working code unless explicitly asked.
- If a change seems to require touching unrelated structure, stop and ask first.

## Git discipline (MANDATORY)

- ❌ NEVER run `git commit`, `git push`, `git tag`, or any history-rewriting command — commits are made by the user only, with the user's own commit text
- ✅ Leave finished changes in the working tree for the user to review, stage, and commit
- ✅ You may suggest commit message text when asked, but never execute the commit yourself
