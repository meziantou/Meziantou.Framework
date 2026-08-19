using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.Extensions.Logging;

/// <summary>Allows custom log messages formatting for the <see cref="FileLoggerProvider"/>.</summary>
/// <example>
/// <code>
/// internal sealed class CsvFileFormatter() : FileFormatter("csv")
/// {
///     public override void Write&lt;TState&gt;(in LogEntry&lt;TState&gt; logEntry, DateTimeOffset timestamp, FileLoggerOptions options, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
///     {
///         textWriter.Write(logEntry.LogLevel);
///         textWriter.Write(',');
///         textWriter.Write(logEntry.Formatter(logEntry.State, logEntry.Exception));
///     }
/// }
/// </code>
/// </example>
public abstract class FileFormatter
{
    /// <summary>Initializes a new instance of the <see cref="FileFormatter"/> class.</summary>
    /// <param name="name">The name of the formatter.</param>
    protected FileFormatter(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>Gets the name of the formatter.</summary>
    public string Name { get; }

    /// <summary>Writes the log message to the specified <see cref="TextWriter"/>. The line terminator is written by the provider.</summary>
    /// <typeparam name="TState">The type of the log entry state.</typeparam>
    /// <param name="logEntry">The log entry to write.</param>
    /// <param name="timestamp">The date and time at which the message was logged, in the timezone configured by <see cref="FileLoggerOptions.UseUtcTimestamp"/>.</param>
    /// <param name="options">The options of the provider that created the log entry.</param>
    /// <param name="scopeProvider">The provider of scope data, or <see langword="null" /> when the scopes must not be written.</param>
    /// <param name="textWriter">The writer the message must be written to.</param>
    /// <remarks>The method is called on the thread that logs the message, so the ambient state such as <see cref="System.Diagnostics.Activity.Current"/> can be read.</remarks>
    public abstract void Write<TState>(in LogEntry<TState> logEntry, DateTimeOffset timestamp, FileLoggerOptions options, IExternalScopeProvider? scopeProvider, TextWriter textWriter);

    /// <summary>Gets the four-letter representation of the specified log level.</summary>
    /// <param name="logLevel">The log level to format.</param>
    /// <returns>The four-letter representation of <paramref name="logLevel"/>.</returns>
    protected static string GetLogLevelString(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRCE",
        LogLevel.Debug => "DBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "FAIL",
        LogLevel.Critical => "CRIT",
        _ => logLevel.ToString().ToUpperInvariant(),
    };
}
