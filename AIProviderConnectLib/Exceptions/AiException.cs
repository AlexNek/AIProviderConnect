namespace AIProviderConnect.Exceptions;

/// <summary>
/// Represents an error that occurred during AI provider operations.
/// </summary>
public sealed class AiException : Exception
{
    /// <summary>
    /// Gets the error code identifying the type of error.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AiException"/> class.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    public AiException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AiException"/> class with an inner exception.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The inner exception.</param>
    public AiException(string code, string message, Exception? inner)
        : base(message, inner)
    {
        Code = code;
    }
}
