# Installation

## Requirements

- .NET 10.0
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Options`
- `Microsoft.Extensions.Options.ConfigurationExtensions`

All three are declared by the library and flow in transitively.

## Installing the Package

Add the `AIProviderConnect` package to your project:

```bash
dotnet add package AIProviderConnect
```

Or reference it from your `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="AIProviderConnect" Version="*" />
</ItemGroup>
```

`Version="*"` resolves to the latest stable release at restore time. Pin to an explicit version in production projects.

All public types live in the `AIProviderConnect.*` namespaces
(e.g. `AIProviderConnect.Abstractions`, `AIProviderConnect.Models`,
`AIProviderConnect.DependencyInjection`).

## What You Get

| Folder | Contents |
| --- | --- |
| `Abstractions/` | `IAIProvider`, `IStreamingChatProvider`, `IModelDiscoveryProvider`, `IProviderCatalog`, `IAIProviderFactory` |
| `Models/` | Request/response, message, tool, streaming, and catalog models |
| `Options/` | Per-protocol configuration classes |
| `Protocols/` | Wire protocol mappers: OpenAI-compatible, Messages API, KeyQuery, and Catalog |
| `Providers/` | `AIProviderBase` plus four provider implementations |
| `Services/` | `ProviderCatalog` — embedded JSON provider definitions |
| `DependencyInjection/` | `AddAiProviders()` extension method |

Next: [Quick Start](quick-start.md).
