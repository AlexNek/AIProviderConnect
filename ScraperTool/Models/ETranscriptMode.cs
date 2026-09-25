namespace ScraperTool.Models;

/// <summary>
/// Transcript generation mode for AiCleverness runtime executions.
/// </summary>
public enum ETranscriptMode
{
    /// <summary>
    /// No transcripts are generated.
    /// </summary>
    None,

    /// <summary>
    /// Normal transcripts with host redaction of sensitive content.
    /// </summary>
    Transcript,

    /// <summary>
    /// Debug transcripts bypass redaction and record all available content
    /// including unredacted sensitive values. Must not be used in production.
    /// </summary>
    DebugTranscript
}
