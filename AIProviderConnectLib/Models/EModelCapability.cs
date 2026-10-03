namespace AIProviderConnect.Models;

/// <summary>
/// Flags describing what a model may be asked to do — its operations, such as tool calling,
/// structured output, embedding, or answering decisions.
/// Capabilities are model-level, not provider-level.
/// </summary>
/// <remarks>
/// These are not the input/output data types: that is <see cref="AIModel.Modality"/>
/// (for example "text+image->text"). A modality never implies a capability —
/// <see cref="ToolCalling"/>, <see cref="StructuredOutput"/>, <see cref="Embedding"/>,
/// <see cref="Reranker"/>, and <see cref="Decision"/> carry no modality signal at all, and the
/// arrow direction cannot separate <see cref="TextToSpeech"/> from <see cref="AudioGeneration"/>
/// or <see cref="VideoTranscription"/> from <see cref="VideoRecognition"/>.
/// <para>
/// On <see cref="AIModel.Capabilities"/> (typed <c>EModelCapability?</c>) the three states are:
/// <see langword="null"/> — the provider did not report capabilities (unknown);
/// <see cref="None"/> — the provider reported having no capabilities;
/// any other value — the provider reported exactly these flags.
/// Absence of a bit inside a reported value means negative; <see langword="null"/> means unreported.
/// Supply the flags an application depends on through <see cref="ModelOverride.Capabilities"/>.
/// </para>
/// </remarks>
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
