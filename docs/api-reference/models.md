# Models

All models live in the `AIProviderConnect.Models` namespace.

## Conversation Models

### ChatCompletionRequest

| Property | Type | Default |
| --- | --- | --- |
| `Model` | `string` | required |
| `Messages` | `IReadOnlyList<ChatMessage>` | empty list |
| `Temperature` | `float` | `0.7` |
| `MaxTokens` | `int?` | `null` |
| `Tools` | `IReadOnlyList<ToolDefinition>?` | `null` |
| `ResponseFormat` | `ResponseFormat?` | `null` |

### ChatMessage

| Property | Type | Notes |
| --- | --- | --- |
| `Role` | `EChatRole` | required |
| `Content` | `string?` | Text content |
| `ToolCalls` | `IReadOnlyList<ToolCall>?` | Set on assistant messages that invoked tools |
| `ToolCallId` | `string?` | Set on `EChatRole.Tool` result messages |

### EChatRole

`System` · `User` · `Assistant` · `Tool`

### ChatCompletionResponse

| Property | Type |
| --- | --- |
| `Id` | `string` |
| `Model` | `string` |
| `Content` | `string` |
| `ReasoningContent` | `string?` |
| `FinishReason` | `string?` |
| `ToolCalls` | `IReadOnlyList<ToolCall>?` |
| `Usage` | `UsageInfo` |

### StreamingChatChunk

| Property | Type | Description |
| --- | --- | --- |
| `Content` | `string` | Text fragment |
| `ReasoningContent` | `string?` | Chain-of-thought reasoning fragment |
| `ToolCalls` | `IReadOnlyList<StreamingToolCallDelta>?` | Incremental tool-call fragments |
| `IsCompleted` | `bool` | Final-chunk marker |

### UsageInfo

`PromptTokens`, `CompletionTokens`, `TotalTokens` (all `int`).

## Tool Models

### ToolDefinition

| Property | Type | Description |
| --- | --- | --- |
| `Name` | `string` | required |
| `Description` | `string` | required |
| `Parameters` | `JsonElement` | required — raw JSON schema |

### ToolCall

| Property | Type | Description |
| --- | --- | --- |
| `Id` | `string` | required — correlation ID for the tool result message |
| `Name` | `string` | required — function name |
| `Arguments` | `string` | required — arguments as a JSON string |

## Format Models

### ResponseFormat

| Property | Type | Default |
| --- | --- | --- |
| `Type` | `string` | `"text"` |
| `JsonSchema` | `JsonSchemaDefinition?` | `null` |

### JsonSchemaDefinition

| Property | Type | Default |
| --- | --- | --- |
| `Name` | `string` | required |
| `Schema` | `JsonElement` | required |
| `Strict` | `bool` | `true` |

## Embedding Models

### EmbeddingRequest

| Property | Type | Default |
| --- | --- | --- |
| `Model` | `string` | `""` |
| `Input` | `IReadOnlyList<string>` | *required* |

### EmbeddingResponse

| Property | Type | Default |
| --- | --- | --- |
| `Data` | `IReadOnlyList<EmbeddingData>` | `[]` |
| `Model` | `string` | `""` |
| `Usage` | `EmbeddingUsage` | `new()` |

### EmbeddingData

| Property | Type | Description |
| --- | --- | --- |
| `Index` | `int` | Position in the input batch |
| `Embedding` | `float[]` | The embedding vector |

### EmbeddingUsage

| Property | Type |
| --- | --- |
| `PromptTokens` | `int` |

## Catalog Models

### ProviderDefinition

Immutable record loaded from embedded JSON — full field list in
[Provider Catalog](../concepts/provider-catalog.md).

### AIModel

Full property list in [Model Discovery](../model-discovery/model-discovery.md).

### ModelOverride

Partial, consumer-supplied patch for one `AIModel`, registered via
`OverrideModels` and merged over the live catalog. Every patchable field is
nullable so the merge can distinguish "not set" from "set"; only non-null
fields are applied. See
[Model Discovery](../model-discovery/model-discovery.md).

| Property | Type | Notes |
| --- | --- | --- |
| `Id` | `string` | required — matches `AIModel.Id` (case-insensitive) |
| `DisplayName` | `string?` | Overrides `AIModel.DisplayName` |
| `Description` | `string?` | Overrides `AIModel.Description` |
| `Modality` | `string?` | Overrides `AIModel.Modality` |
| `OwnedBy` | `string?` | Overrides `AIModel.OwnedBy` |
| `PromptPrice` | `decimal?` | Overrides `AIModel.PromptPrice` |
| `CompletionPrice` | `decimal?` | Overrides `AIModel.CompletionPrice` |
| `PriceUnit` | `EModelPriceUnit?` | Overrides `AIModel.PriceUnit` |
| `ContextWindow` | `int?` | Overrides `AIModel.ContextWindow` |
| `Capabilities` | `EModelCapability?` | Overrides `AIModel.Capabilities` |
| `Hidden` | `bool` | When `true`, removes the matching live model instead of patching it |

### EProviderProtocol

`Native` · `OpenAICompatible` · `MessagesApi` · `KeyQuery` · `Catalog` ·
`HybridGateway`

### EModelCapability

`[Flags]` enum: `None`, `TextGeneration`, `StructuredOutput`,
`ToolCalling`, `Embedding`, `Reranker`, `ImageRecognition`,
`ImageGeneration`, `AudioRecognition`, `TextToSpeech`, `AudioGeneration`,
`VideoTranscription`, `VideoRecognition`, `VideoGeneration`.
