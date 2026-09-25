using Microsoft.Extensions.Logging;

namespace AIProviderConnect.Tests.TestDoubles;

/// <summary>
/// A test <see cref="ILogger"/> that stores every entry with its message already rendered from the
/// log template, so both the log level and the placeholder ordering can be asserted.
/// </summary>
public sealed class RecordingLogger : ILogger
{
    /// <summary>
    /// Gets the recorded entries in the order they were logged.
    /// </summary>
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    /// <summary>
    /// Returns the rendered messages recorded at the given level.
    /// </summary>
    public IEnumerable<string> MessagesAt(LogLevel level) =>
        Entries.Where(entry => entry.Level == level).Select(entry => entry.Message);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
