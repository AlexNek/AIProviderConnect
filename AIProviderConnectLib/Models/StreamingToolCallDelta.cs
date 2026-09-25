namespace AIProviderConnect.Models;

/// <summary>
/// Represents an incremental tool-call fragment received during streaming.
/// Each chunk carries a position index so the consumer can reassemble
/// multiple parallel tool calls.
/// </summary>
public sealed record StreamingToolCallDelta
{
    /// <summary>
    /// Gets the zero-based index identifying which tool call this fragment belongs to.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Gets the unique identifier for the tool call (typically only present in the first fragment).
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Gets the function name (typically only present in the first fragment).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets a JSON fragment of the arguments string to append.
    /// </summary>
    public string? ArgumentsFragment { get; init; }
}
