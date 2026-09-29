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

| Property | Type | Description |
| --- | --- | --- |
| `PromptTokens` | `int` | Input tokens |
| `CompletionTokens` | `int` | Output tokens |
| `TotalTokens` | `int` | Total tokens |
| `Cost` | `decimal?` | Per-call cost in USD, or `null` when the provider does not report one. Only the decisions parser populates it |

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

## Decision Models

See [Decision Models](../decisions/decision-models.md) for the full walkthrough.

### DecisionRequest

| Property | Type | Default | Notes |
| --- | --- | --- | --- |
| `Model` | `string` | `""` | Falls back to the provider's `DefaultModel` when empty |
| `State` | `object?` | `null` | Serialized as-is; must be a `string`, `IReadOnlyDictionary<string, object?>`, or `IReadOnlyList<string>` |
| `Questions` | `IReadOnlyDictionary<string, DecisionQuestion>` | empty | Must be non-empty; keyed by question name |

### DecisionQuestion

| Property | Type | Notes |
| --- | --- | --- |
| `Kind` | `EDecisionQuestionKind` | required — `Choice`, `Noul`, or `Score` |
| `Instructions` | `string?` | Free-text guidance for the question |
| `Criteria` | `IReadOnlyDictionary<string, string>?` | Choice: option → description (1–255); Noul: optional `true`/`false` descriptions |
| `Scale` | `IReadOnlyList<string>?` | Score: ordered levels (2–10); sent under the wire `criteria` key |

### EDecisionQuestionKind

`Choice` · `Noul` · `Score`

### DecisionResponse

| Property | Type | Description |
| --- | --- | --- |
| `Id` | `string` | Response identifier |
| `Model` | `string` | Model snapshot that served the request |
| `Provider` | `string` | Serving provider name reported by the host |
| `Answers` | `IReadOnlyDictionary<string, DecisionAnswer>` | One typed answer per question, keyed by question name |
| `Usage` | `UsageInfo` | Token accounting, including `Cost` when reported |

### DecisionAnswer

| Property | Type | Description |
| --- | --- | --- |
| `Kind` | `EDecisionAnswerKind` | Selects which typed-answer property is populated |
| `Choice` | `ChoiceAnswer?` | Populated when `Kind` is `Choice` |
| `Noul` | `NoulAnswer?` | Populated when `Kind` is `Noul` |
| `Score` | `ScoreAnswer?` | Populated when `Kind` is `Score` |

### EDecisionAnswerKind

`Choice` · `Noul` · `Score` · `Unknown` (forward compatibility)

### ChoiceAnswer / NoulAnswer / ScoreAnswer

| Type | Members |
| --- | --- |
| `ChoiceAnswer` | `Selected` (`string`), `Confidence` (`double`), `Probabilities` (`IReadOnlyDictionary<string, double>`) |
| `NoulAnswer` | `ProbabilityOfYes` (`double`) |
| `ScoreAnswer` | `Score` (`double` — continuous over the zero-based level indices), `Confidence` (`double`), `LevelProbabilities` (`IReadOnlyDictionary<string, double>` keyed by level index), `Legend` (`IReadOnlyList<string>` — labels in index order) |

## Credential Models

### RequestCredentials

Immutable per-call override record. Every field is nullable; `null` (or
whitespace) means "use the provider's configured value", and a record with all
three unset counts as "no override". `BaseUrl` must be the full API base
including the version segment (for example `https://test.example.com/v1/`).
The `ToString()` override masks `ApiKey` (at most the last four characters) so
the record is safe to log. See [Runtime Credentials](../concepts/runtime-credentials.md).

| Property | Type | Default | Notes |
| --- | --- | --- | --- |
| `ApiKey` | `string?` | `null` | Overrides the configured API key / Bearer token for the call |
| `BaseUrl` | `string?` | `null` | Overrides the configured base URL (full base including version segment) |
| `Model` | `string?` | `null` | Overrides the request/default model for the call |

## Catalog Models

See [Provider Catalog](../concepts/provider-catalog.md) for how definitions load.

### EndpointDefinition

One entry of the optional per-operation `endpoints` block on a
`ProviderDefinition`. Every member is optional and omitted from the written
JSON when unset.

| Property | Type | JSON | Description |
| --- | --- | --- | --- |
| `Path` | `string?` | `path` | Relative path resolved against the effective base URL |
| `BaseUrl` | `string?` | `baseUrl` | Override used only when the surface sits on a different root than the definition's common base |
| `Protocol` | `EProviderProtocol?` | `protocol` | Override for an operation served with a different wire protocol; absent means inherit |

`Protocol` deserializes through `EProviderProtocolNullableJsonConverter`:
the same case-insensitive alias vocabulary as the root `protocol` field, but
a JSON `null` (or an absent member) deserializes to `null` instead of
throwing, and an unknown token still throws `JsonException`.

Operation keys come from `EndpointOperations` (`Chat`, `Models`, `Messages`,
`Embeddings`, `Decisions`); `EndpointOperations.Find(dictionary, operation)`
is the case-insensitive lookup every consumer should use instead of indexing
the dictionary directly.

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
`HybridGateway` · `Decision`

### EModelCapability

`[Flags]` enum: `None`, `TextGeneration`, `StructuredOutput`,
`ToolCalling`, `Embedding`, `Reranker`, `ImageRecognition`,
`ImageGeneration`, `AudioRecognition`, `TextToSpeech`, `AudioGeneration`,
`VideoTranscription`, `VideoRecognition`, `VideoGeneration`, `Decision`.
