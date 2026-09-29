# AIProviderConnect

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com)

Unified .NET library for connecting to **35+ AI providers** — OpenAI, Anthropic, Gemini, Ollama, Groq, Mistral, xAI, DeepSeek, Cohere and more — through a single `IAIProvider` interface, driven by an embedded JSON provider catalog and five provider implementations.

## Features

- Single `IAIProvider` interface for all providers
- Runtime model discovery (`IModelDiscoveryProvider.GetModelsAsync`)
- Streaming via `IStreamingChatProvider` / `IAsyncEnumerable<StreamingChatChunk>`
- Tool / function calling
- Structured output via `ResponseFormat` with JSON schema
- Embeddings via `IEmbeddingProvider` for RAG and vector search
- Decision models via `IDecisionProvider` — typed questions (Choice, Noul, Score) with probabilistic answers and usage cost ([Jev / System One](https://alexnek.github.io/AIProviderConnect/decisions/decision-models/) as worked example)
- Structured errors via `AiException` with centralized `AiErrorCodes`
- One-line DI registration (`AddAiProviders()`) with named options per provider ID
- `appsettings.json` configuration support
- Per-request credential and model resolution via `RequestCredentials`

## Usage

```csharp
// Program.cs
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

```csharp
// Chat — pick a provider by ID
var provider = factory.GetProvider("openai");
var response = await provider.ChatAsync(new ChatCompletionRequest
{
    Model = "gpt-4o",
    Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello!" }]
});

// Stream
if (provider is IStreamingChatProvider streaming)
{
    await foreach (var chunk in streaming.StreamAsync(request))
        Console.Write(chunk.Content);
}
```

### Decision models (Jev via OpenRouter)

Decision providers are not in the embedded JSON catalog — you supply the
definition through the builder, then configure the endpoint and model:

```csharp
// Register — same AddAiProviders builder, just no embedded JSON for decision hosts
services.AddAiProviders(b => b
    .Add(new ProviderDefinition
    {
        Id = "openrouter-decisions",
        Protocol = EProviderProtocol.Decision,
        BaseUrl = "https://openrouter.ai/api/",
    })
    .Configure<DecisionProviderOptions>("openrouter-decisions", o =>
    {
        o.ApiKey = "<your-key>";
        o.DefaultModel = "typesafe/jev-1.13";
    }));
```

```csharp
// Use — cast to IDecisionProvider, send typed questions, branch on answer kind
var decision = (IDecisionProvider)serviceProvider
    .GetRequiredKeyedService<IAIProvider>("openrouter-decisions");

var response = await decision.DecideAsync(new DecisionRequest
{
    State = "Customer was double-charged for their annual plan.",
    Questions = new Dictionary<string, DecisionQuestion>
    {
        ["is_billing"] = new() { Kind = EDecisionQuestionKind.Noul },
        ["priority"]   = new()
        {
            Kind = EDecisionQuestionKind.Choice,
            Criteria = new Dictionary<string, string>
            {
                ["low"] = "Can wait", ["normal"] = "This week", ["high"] = "Blocking now"
            }
        },
        ["severity"]   = new()
        {
            Kind = EDecisionQuestionKind.Score,
            Scale = ["minor", "moderate", "major", "critical"]
        }
    }
}, ct);

foreach (var (name, answer) in response.Answers)
{
    switch (answer.Kind)
    {
        case EDecisionAnswerKind.Noul:
            Console.WriteLine($"{name}: P(yes) = {answer.Noul!.ProbabilityOfYes:P0}");
            break;
        case EDecisionAnswerKind.Choice:
            Console.WriteLine($"{name}: {answer.Choice!.Selected} ({answer.Choice.Confidence:P0})");
            break;
        case EDecisionAnswerKind.Score:
            Console.WriteLine($"{name}: {answer.Score!.Score:F2} (confidence {answer.Score.Confidence:P0})");
            break;
    }
}
```

See the [Decision Models](https://alexnek.github.io/AIProviderConnect/decisions/decision-models/) guide for the full primitive reference, a second host example (Jev first-party API), and when-to-use guidance.

Developer manual: [alexnek.github.io/AIProviderConnect](https://alexnek.github.io/AIProviderConnect/)
Full documentation and source: [github.com/AlexNek/AIProviderConnect](https://github.com/AlexNek/AIProviderConnect)

## Changelog

See [CHANGELOG.md](CHANGELOG.md)
