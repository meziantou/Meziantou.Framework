using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.Extensions.Logging;

/// <summary>A <see cref="FileFormatter"/> that writes one human-readable line per log entry.</summary>
public sealed class SimpleFileFormatter : FileFormatter
{
    /// <summary>Gets a shared instance of the <see cref="SimpleFileFormatter"/> class.</summary>
    public static SimpleFileFormatter Instance { get; } = new();

    /// <summary>Initializes a new instance of the <see cref="SimpleFileFormatter"/> class.</summary>
    public SimpleFileFormatter()
        : base(FileFormatterNames.Simple)
    {
    }

    /// <inheritdoc/>
    public override void Write<TState>(in LogEntry<TState> logEntry, DateTimeOffset timestamp, FileLoggerOptions options, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(textWriter);

        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
            return;

        if (options.TimestampFormat is not null)
        {
            WriteSegment(textWriter, timestamp.ToString(options.TimestampFormat, CultureInfo.InvariantCulture));
        }

        if (options.IncludeLogLevel)
        {
            WriteSegment(textWriter, GetLogLevelString(logEntry.LogLevel));
        }

        if (options.IncludeCategory)
        {
            WriteSegment(textWriter, logEntry.Category);
        }

        if (options.IncludeEventId)
        {
            textWriter.Write("[EventId:");
            textWriter.Write(logEntry.EventId.Id.ToString(CultureInfo.InvariantCulture));
            if (logEntry.EventId.Name is not null)
            {
                textWriter.Write(':');
                textWriter.Write(logEntry.EventId.Name);
            }

            textWriter.Write("] ");
        }

        if (options.IncludeThreadId)
        {
            textWriter.Write("[ThreadId:");
            textWriter.Write(Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture));
            textWriter.Write("] ");
        }

        if (options.IncludeActivityTracking && Activity.Current is { } activity)
        {
            textWriter.Write("[TraceId:");
            textWriter.Write(activity.TraceId.ToHexString());
            textWriter.Write(" SpanId:");
            textWriter.Write(activity.SpanId.ToHexString());
            textWriter.Write("] ");
        }

        scopeProvider?.ForEachScope(
            (scope, writer) =>
            {
                writer.Write("=> ");
                writer.Write(Convert.ToString(scope, CultureInfo.InvariantCulture));
                writer.Write(' ');
            }, textWriter);

        textWriter.Write(message);

        if (logEntry.Exception is not null)
        {
            textWriter.WriteLine();
            textWriter.Write(logEntry.Exception.ToString());
        }
    }

    private static void WriteSegment(TextWriter textWriter, string value)
    {
        textWriter.Write('[');
        textWriter.Write(value);
        textWriter.Write("] ");
    }
}
