using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.Extensions.Logging;

/// <summary>A <see cref="FileFormatter"/> that writes one JSON object per log entry.</summary>
public sealed class JsonFileFormatter : FileFormatter
{
    // Keep the cached buffer small, so a single big entry doesn't retain a big buffer for the lifetime of the thread
    private const int MaxCachedCapacity = 4 * 1024;

    [ThreadStatic]
    private static ArrayBufferWriter<byte>? s_buffer;

    [ThreadStatic]
    private static Utf8JsonWriter? s_jsonWriter;

    /// <summary>Gets a shared instance of the <see cref="JsonFileFormatter"/> class.</summary>
    public static JsonFileFormatter Instance { get; } = new();

    /// <summary>Initializes a new instance of the <see cref="JsonFileFormatter"/> class.</summary>
    public JsonFileFormatter()
        : base(FileFormatterNames.Json)
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

        // The buffers are cached per thread. Detach them in case a formatter callback logs a message
        var buffer = s_buffer;
        var writer = s_jsonWriter;
        if (buffer is null || writer is null)
        {
            buffer = new ArrayBufferWriter<byte>(256);
            writer = new Utf8JsonWriter(buffer);
        }
        else
        {
            s_buffer = null;
            s_jsonWriter = null;
            buffer.ResetWrittenCount();
            writer.Reset(buffer);
        }

        try
        {
            writer.WriteStartObject();

            // A null format means the timestamp must be omitted, like in SimpleFileFormatter
            if (options.TimestampFormat is not null)
            {
                writer.WriteString("Timestamp", timestamp.ToString(options.TimestampFormat, CultureInfo.InvariantCulture));
            }

            if (options.IncludeLogLevel)
            {
                writer.WriteString("LogLevel", GetLogLevelString(logEntry.LogLevel));
            }

            if (options.IncludeCategory)
            {
                writer.WriteString("Category", logEntry.Category);
            }

            if (options.IncludeEventId)
            {
                writer.WriteNumber("EventId", logEntry.EventId.Id);
                if (logEntry.EventId.Name is not null)
                {
                    writer.WriteString("EventName", logEntry.EventId.Name);
                }
            }

            if (options.IncludeThreadId)
            {
                writer.WriteNumber("ThreadId", Environment.CurrentManagedThreadId);
            }

            if (options.IncludeActivityTracking && Activity.Current is { } activity)
            {
                writer.WriteString("TraceId", activity.TraceId.ToHexString());
                writer.WriteString("SpanId", activity.SpanId.ToHexString());
            }

            writer.WriteString("Message", message);

            if (logEntry.Exception is not null)
            {
                writer.WriteString("Exception", logEntry.Exception.ToString());
            }

            if (logEntry.State is IReadOnlyList<KeyValuePair<string, object?>> state)
            {
                writer.WriteStartObject("State");
                foreach (var item in state)
                {
                    WriteProperty(writer, item.Key, item.Value);
                }

                writer.WriteEndObject();
            }

            if (scopeProvider is not null)
            {
                writer.WriteStartArray("Scopes");
                scopeProvider.ForEachScope(
                    (scope, jsonWriter) =>
                    {
                        if (scope is IEnumerable<KeyValuePair<string, object?>> scopeItems)
                        {
                            jsonWriter.WriteStartObject();
                            jsonWriter.WriteString("Message", Convert.ToString(scope, CultureInfo.InvariantCulture));
                            foreach (var item in scopeItems)
                            {
                                WriteProperty(jsonWriter, item.Key, item.Value);
                            }

                            jsonWriter.WriteEndObject();
                        }
                        else
                        {
                            jsonWriter.WriteStringValue(Convert.ToString(scope, CultureInfo.InvariantCulture));
                        }
                    }, writer);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.Flush();

            WriteUtf8(textWriter, buffer.WrittenSpan);
        }
        finally
        {
            if (buffer.Capacity <= MaxCachedCapacity)
            {
                s_buffer = buffer;
                s_jsonWriter = writer;
            }
            else
            {
                writer.Dispose();
            }
        }
    }

    private static void WriteUtf8(TextWriter textWriter, ReadOnlySpan<byte> utf8)
    {
        // Transcode into a pooled buffer instead of allocating a string for every entry
        var chars = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(utf8.Length));
        try
        {
            var charCount = Encoding.UTF8.GetChars(utf8, chars);
            textWriter.Write(chars.AsSpan(0, charCount));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
        }
    }

    private static void WriteProperty(Utf8JsonWriter writer, string name, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNull(name);
                break;
            case string stringValue:
                writer.WriteString(name, stringValue);
                break;
            case bool boolValue:
                writer.WriteBoolean(name, boolValue);
                break;
            case byte or sbyte or short or ushort or int:
                writer.WriteNumber(name, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                break;
            case uint uintValue:
                writer.WriteNumber(name, uintValue);
                break;
            case long longValue:
                writer.WriteNumber(name, longValue);
                break;
            case ulong ulongValue:
                writer.WriteNumber(name, ulongValue);
                break;
            case float floatValue:
                writer.WriteNumber(name, floatValue);
                break;
            case double doubleValue:
                writer.WriteNumber(name, doubleValue);
                break;
            case decimal decimalValue:
                writer.WriteNumber(name, decimalValue);
                break;
            case DateTime dateTimeValue:
                writer.WriteString(name, dateTimeValue);
                break;
            case DateTimeOffset dateTimeOffsetValue:
                writer.WriteString(name, dateTimeOffsetValue);
                break;
            case Guid guidValue:
                writer.WriteString(name, guidValue);
                break;
            default:
                writer.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}
