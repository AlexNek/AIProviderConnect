namespace AIProviderConnect.Models;

/// <summary>
/// Flags representing model capabilities and modalities.
/// Capabilities are model-level, not provider-level.
/// </summary>
[Flags]
public enum EModelCapability
{
    None = 0,

    // --- Text & Core LLM ---
    TextGeneration = 1 << 0,

    StructuredOutput = 1 << 1,

    ToolCalling = 1 << 2,

    Embedding = 1 << 3,

    Reranker = 1 << 4,

    // --- Vision (Image) ---
    ImageRecognition = 1 << 5,

    ImageGeneration = 1 << 6,

    // --- Audio ---
    AudioRecognition = 1 << 7,

    TextToSpeech = 1 << 8,

    AudioGeneration = 1 << 9,

    // --- Video ---
    VideoTranscription = 1 << 10,

    VideoRecognition = 1 << 11,

    VideoGeneration = 1 << 12,

    // --- Decision ---

    /// <summary>
    /// Marks a model that answers with typed decisions (Choice, Noul, Score) rather than generated text.
    /// </summary>
    Decision = 1 << 13
}
