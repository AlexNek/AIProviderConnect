# AIProviderConnect

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com)

A unified .NET library for connecting to **35+ AI providers** (OpenAI, Anthropic, Gemini, Ollama, Groq, Mistral, xAI and more) through a single `IAIProvider` interface — with runtime model discovery, streaming, tool calling, and full DI support.

Instead of one class per provider, the library ships **six wire protocols and five provider implementations** and an embedded JSON catalog: each provider definition declares its wire protocol, and the right implementation is selected automatically.

## Supported Providers

| Protocol | Implementation | Providers |
|---|---|---|
| OpenAI-compatible | `OpenAICompatibleProvider` | openai, ollama, groq, mistral, xai, deepseek, cohere, cerebras, deepinfra, fireworks, together-ai, huggingface, openrouter, vercel-ai-gateway, and more |
| Messages API (Anthropic-style) | `MessagesApiProvider` | anthropic |
| Key query (Google-style) | `KeyQueryProvider` | gemini |
| Model catalog (GitHub Models-style) | `ModelCatalogProvider` | *(no embedded entry — protocol exists for consumer-supplied providers)* |
| Hybrid gateway | `OpenAICompatibleProvider` | opencode-go, opencode-zen |
| Decision (typed questions) | `DecisionProvider` | *(no embedded entry — consumer-supplied via DI)* |

Full list and metadata: `AIProviderConnectLib/ai-providers/*.json`.

## Repository Layout

Only `AIProviderConnectLib` is published to NuGet. The other projects ship in this repository as source.

```
AIProviderConnect/
├── AIProviderConnectLib/            The NuGet library — IAIProvider surface, providers, protocols, catalog
├── AIProviderConnectLib.Tests/      xUnit tests for the library
├── GraphVisualization/              Reusable WPF graph viewer used by ScraperTool's decision-tree UI
├── ScraperTool/                     WPF desktop tool — provider-catalog maintenance + package demo
├── ScraperTool.Tests/               xUnit tests for ScraperTool
├── docs/                            MkDocs source for the developer manual
└── .github/workflows/               CI, docs deployment, release publishing
```

### ScraperTool

A WPF desktop application used to research and maintain the provider catalog that ships inside `AIProviderConnectLib`, and to demonstrate the library in a real consumer. It reads and writes `ai-providers/*.json`, persists state in a local SQLite database, and drives AI-assisted URL research through the [AiCleverness](https://github.com/AlexNek/AICleverness) agent runtime.

**Features**

- Browse, edit, and validate provider definitions (all catalog fields).
- Reachability and content checks for `apiPricingUrl`, `subscriptionPricingUrl`, `loginUrl`, `documentationUrl`, `baseUrl`, and other URLs.
- AI-driven URL fixing: a decision-tree research agent proposes replacements for broken or wrong URLs and streams the trace back to the UI.
- Manual browser verification via an anti-detection Chromium (CloakBrowser) when a target URL is behind a Cloudflare challenge.

**Known limitations**

- Pricing-page verification is heuristic. It fetches the page as sanitized Markdown and requires a displayed currency amount (plus at least one plan/tier keyword for subscriptions). It does not parse pricing tables and cannot extract per-model prices — a stray `$5` in prose can pass, and a JS-rendered table captured before hydration can fail.
- Bot-protected URLs are reported as `NotEvaluated` rather than treated as a pass. The automatic probe abstains; only the manual CloakBrowser flow obtains content from aggressive Cloudflare / Vercel / DataDome rules.
- URL research runs under a wall-clock and per-call budget. Slow sites or heavily obfuscated homepages can exhaust it before the correct candidate is reached.

## Install

```bash
dotnet add package AIProviderConnect
```

Or in your `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="AIProviderConnect" Version="*" />
</ItemGroup>
```

`Version="*"` resolves to the latest stable release at restore time. Pin to an explicit version in production projects.

## Quick Start

### 1. Register providers in Program.cs

```csharp
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Options;

builder.Services.AddHttpClient();
builder.Services.AddAiProviders();

builder.Services.Configure<OpenAICompatibleProviderOptions>("openai", o =>
{
    o.Enabled = true;
    o.BaseUrl = "https://api.openai.com/v1/";
    o.ApiKey = "your-api-key";
    o.DefaultModel = "gpt-4o";
});
```

### 2. Inject and use

All providers are registered as `IAIProvider` — resolve the factory and pick by ID:

```csharp
public class MyService(IAIProviderFactory factory)
{
    public async Task<string> AskAsync(string question)
    {
        var provider = factory.GetProvider("openai");
        var response = await provider.ChatAsync(new ChatCompletionRequest
        {
            Model = "gpt-4o",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = question }]
        });
        return response.Content;
    }
}
```

### 3. Streaming

Streaming is an optional capability (`IStreamingChatProvider`):

```csharp
if (provider is IStreamingChatProvider streaming)
{
    await foreach (var chunk in streaming.StreamAsync(request))
        Console.Write(chunk.Content);
}
```

### 4. Runtime model discovery

```csharp
if (provider is IModelDiscoveryProvider discovery && discovery.SupportsModelDiscovery)
{
    IReadOnlyList<AIModel> models = await discovery.GetModelsAsync();
}
```

### 5. Tool calling

```csharp
var request = new ChatCompletionRequest
{
    Model = "gpt-4o",
    Messages = [new ChatMessage { Role = EChatRole.User, Content = "Weather in Berlin?" }],
    Tools = [new ToolDefinition
    {
        Name = "get_weather",
        Description = "Returns the current weather for a city.",
        Parameters = weatherSchemaJson
    }]
};
```

Requested calls come back in `ChatCompletionResponse.ToolCalls`; send results back as `EChatRole.Tool` messages linked by `ToolCallId`.

## Configuration

Options are **named** by provider catalog ID and can be bound from `appsettings.json`:

```csharp
builder.Services.Configure<OpenAICompatibleProviderOptions>(
    "openai", builder.Configuration.GetSection("OpenAI"));
```

```json
{
  "OpenAI": {
    "Enabled": true,
    "ApiKey": "your-api-key",
    "BaseUrl": "https://api.openai.com/v1/",
    "DefaultModel": "gpt-4o"
  },
  "Ollama": {
    "Enabled": true,
    "ApiKey": "ollama",
    "BaseUrl": "http://localhost:11434/v1/",
    "DefaultModel": "llama3.2"
  }
}
```

Base options (`AIProviderOptions`): `Enabled`, `ApiKey`, `BaseUrl`, `DefaultModel`, `DefaultHeaders`. Protocol-specific options add endpoint paths, API version headers, and gateway settings — see the [DI Extensions reference](https://alexnek.github.io/AIProviderConnect/api-reference/di-extensions/).

## Error Handling

All failures throw `AiException` with a stable `Code` (e.g. `ai/rate-limited`, `ai/unauthorized`, `ai/provider-disabled`) — switch on the code, not the message.

## Library Architecture

```
AIProviderConnectLib/
├── Abstractions/        IAIProvider, IStreamingChatProvider, IModelDiscoveryProvider, IEmbeddingProvider, IDecisionProvider, IProviderCatalog, IAIProviderFactory
├── Models/              Request, response, message, tool, streaming, and catalog models
├── Options/             AIProviderOptions base + per-protocol options classes
├── Protocols/           OpenAI-compatible and Messages API wire protocol mappers
├── Providers/           AIProviderBase + 5 protocol-specific provider implementations
├── Services/            ProviderCatalog (embedded JSON provider definitions)
├── ai-providers/        Provider definition JSON files
└── DependencyInjection/ AddAiProviders() extension method
```

## Documentation

The [developer manual](https://alexnek.github.io/AIProviderConnect/) covers installation, DI wiring, the provider catalog, wire protocols, streaming, tool calling, model discovery, and a full API reference.

## Requirements

- .NET 10.0
- `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0+
- `Microsoft.Extensions.Options` 10.0+
- `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0+

## License

MIT — see [LICENSE.txt](LICENSE.txt)

## Changelog

Package releases: [CHANGELOG.md](CHANGELOG.md)

ScraperTool — the desktop tool, which ships as source and not inside the package — keeps its own record at [ScraperTool/CHANGELOG.md](ScraperTool/CHANGELOG.md)
