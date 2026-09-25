namespace AIProviderConnect.Models;

/// <summary>
/// Represents the role of a chat message sender.
/// </summary>
public enum EChatRole
{
    /// <summary>
    /// System-generated messages, typically used for instructions.
    /// </summary>
    System,

    /// <summary>
    /// Messages from the end user.
    /// </summary>
    User,

    /// <summary>
    /// Messages from the AI assistant.
    /// </summary>
    Assistant,

    /// <summary>
    /// Messages representing tool call results.
    /// </summary>
    Tool
}
