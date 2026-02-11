using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Writes trace events in the Chromium Trace Event Format to a stream.</summary>
/// <example>
/// <code>
/// await using var writer = ChromiumTracingWriter.Create("trace.json");
/// await writer.WriteEventAsync(new ChromiumTracingCompleteEvent
/// {
///     Name = "My Operation",
///     Category = "category1",
///     Timestamp = DateTimeOffset.UtcNow,
///     Duration = TimeSpan.FromMilliseconds(150),
///     ProcessId = Environment.ProcessId,
///     ThreadId = Environment.CurrentManagedThreadId
/// });
/// </code>
/// </example>
// https://docs.google.com/document/d/1CvAClvFfyA5R-PhYUmn5OOQtYMH4h6I0nSsKchNAySU/preview#
// https://github.com/catapult-project/catapult/blob/6d5a4e52871813b8b2e71b378fc54bca459600c4/tracing/tracing/extras/importer/trace_event_importer.html
public sealed partial class ChromiumTracingWriter : IAsyncDisposable
{
    private static readonly byte[] ArrayEmpty = "[]"u8.ToArray();
    private static readonly byte[] ArrayStart = "[\n"u8.ToArray();
    private static readonly byte[] ArrayEnd = "\n]"u8.ToArray();
    private static readonly byte[] ArrayItemSeparator = ",\n"u8.ToArray();

    private readonly bool _streamOwned;
    private readonly Stream _stream;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private bool _hasItems;

    /// <summary>Initializes a new instance of the <see cref="ChromiumTracingWriter"/> class with the specified stream.</summary>
    /// <param name="stream">The stream to write trace events to.</param>
    public ChromiumTracingWriter(Stream stream)
        : this(stream, streamOwned: false, serializerContext: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ChromiumTracingWriter"/> class with the specified stream and serializer context.</summary>
    /// <param name="stream">The stream to write trace events to.</param>
    /// <param name="serializerContext">The serializer context to combine with the built-in one.</param>
    public ChromiumTracingWriter(Stream stream, JsonSerializerContext? serializerContext)
        : this(stream, streamOwned: false, serializerContext)
    {
    }

    private ChromiumTracingWriter(Stream stream, bool streamOwned, JsonSerializerContext? serializerContext)
    {
        _stream = stream;
        _streamOwned = streamOwned;
        _jsonSerializerOptions = serializerContext is null ? SourceGenerationContext.Default.Options : CreateSerializerOptions(serializerContext);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes to a file at the specified path.</summary>
    /// <param name="path">The file path where trace events will be written.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter Create(string path)
    {
        var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        return new ChromiumTracingWriter(fs, streamOwned: true, serializerContext: null);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes to a file at the specified path.</summary>
    /// <param name="path">The file path where trace events will be written.</param>
    /// <param name="serializerContext">The serializer context to combine with the built-in one.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter Create(string path, JsonSerializerContext? serializerContext)
    {
        var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        return new ChromiumTracingWriter(fs, streamOwned: true, serializerContext);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes to the specified stream.</summary>
    /// <param name="stream">The stream to write trace events to.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter Create(Stream stream)
    {
        return Create(stream, streamOwned: true, serializerContext: null);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes to the specified stream.</summary>
    /// <param name="stream">The stream to write trace events to.</param>
    /// <param name="serializerContext">The serializer context to combine with the built-in one.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter Create(Stream stream, JsonSerializerContext? serializerContext)
    {
        return Create(stream, streamOwned: true, serializerContext);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes to the specified stream.</summary>
    /// <param name="stream">The stream to write trace events to.</param>
    /// <param name="streamOwned">Indicates whether the stream should be disposed when the writer is disposed.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter Create(Stream stream, bool streamOwned)
    {
        return new ChromiumTracingWriter(stream, streamOwned, serializerContext: null);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes to the specified stream.</summary>
    /// <param name="stream">The stream to write trace events to.</param>
    /// <param name="streamOwned">Indicates whether the stream should be disposed when the writer is disposed.</param>
    /// <param name="serializerContext">The serializer context to combine with the built-in one.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter Create(Stream stream, bool streamOwned, JsonSerializerContext? serializerContext)
    {
        return new ChromiumTracingWriter(stream, streamOwned, serializerContext);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes GZip-compressed trace events to a file at the specified path.</summary>
    /// <param name="path">The file path where compressed trace events will be written.</param>
    /// <param name="compressionLevel">The compression level to use.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter CreateGzip(string path, CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        var gzip = new GZipStream(fs, compressionLevel, leaveOpen: false);
        return new ChromiumTracingWriter(gzip, streamOwned: true, serializerContext: null);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes GZip-compressed trace events to a file at the specified path.</summary>
    /// <param name="path">The file path where compressed trace events will be written.</param>
    /// <param name="compressionLevel">The compression level to use.</param>
    /// <param name="serializerContext">The serializer context to combine with the built-in one.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter CreateGzip(string path, JsonSerializerContext? serializerContext, CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        var gzip = new GZipStream(fs, compressionLevel, leaveOpen: false);
        return new ChromiumTracingWriter(gzip, streamOwned: true, serializerContext);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes GZip-compressed trace events to the specified stream.</summary>
    /// <param name="stream">The stream to write compressed trace events to.</param>
    /// <param name="compressionLevel">The compression level to use.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter CreateGzip(Stream stream, CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        var gzip = new GZipStream(stream, compressionLevel, leaveOpen: true);
        return new ChromiumTracingWriter(gzip, streamOwned: true, serializerContext: null);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes GZip-compressed trace events to the specified stream.</summary>
    /// <param name="stream">The stream to write compressed trace events to.</param>
    /// <param name="compressionLevel">The compression level to use.</param>
    /// <param name="serializerContext">The serializer context to combine with the built-in one.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter CreateGzip(Stream stream, JsonSerializerContext? serializerContext, CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        var gzip = new GZipStream(stream, compressionLevel, leaveOpen: true);
        return new ChromiumTracingWriter(gzip, streamOwned: true, serializerContext);
    }

    /// <summary>Finalizes the JSON array and disposes the underlying stream if owned.</summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_hasItems)
        {
            await _stream.WriteAsync(ArrayEnd).ConfigureAwait(false);
        }
        else
        {
            await _stream.WriteAsync(ArrayEmpty).ConfigureAwait(false);
        }

        if (_streamOwned)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Writes a trace event to the stream.</summary>
    /// <param name="tracingEvent">The trace event to write.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "The json options are guarantee to contains the TypeResolver for events")]
    public async Task WriteEventAsync(ChromiumTracingEvent tracingEvent, CancellationToken cancellationToken = default)
    {
        if (tracingEvent is null)
            return;

        if (_hasItems)
        {
            await _stream.WriteAsync(ArrayItemSeparator, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _stream.WriteAsync(ArrayStart, cancellationToken).ConfigureAwait(false);
            _hasItems = true;
        }

        await JsonSerializer.SerializeAsync(_stream, tracingEvent, tracingEvent.GetType(), _jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private static JsonSerializerOptions CreateSerializerOptions(JsonSerializerContext serializerContext)
    {
        var options = new JsonSerializerOptions(SourceGenerationContext.Default.Options)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(SourceGenerationContext.Default, serializerContext),
        };

        return options;
    }

    [JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false, IgnoreReadOnlyProperties = false, GenerationMode = JsonSourceGenerationMode.Default)]
    [JsonSerializable(typeof(ChromiumTracingAsyncBeginEvent))]
    [JsonSerializable(typeof(ChromiumTracingAsyncEndEvent))]
    [JsonSerializable(typeof(ChromiumTracingAsyncInstantEvent))]
    [JsonSerializable(typeof(ChromiumTracingClockSyncEvent))]
    [JsonSerializable(typeof(ChromiumTracingCompleteEvent))]
    [JsonSerializable(typeof(ChromiumTracingContextBeginEvent))]
    [JsonSerializable(typeof(ChromiumTracingContextEndEvent))]
    [JsonSerializable(typeof(ChromiumTracingContextEvent))]
    [JsonSerializable(typeof(ChromiumTracingCounterEvent))]
    [JsonSerializable(typeof(ChromiumTracingDurationBeginEvent))]
    [JsonSerializable(typeof(ChromiumTracingDurationEndEvent))]
    [JsonSerializable(typeof(ChromiumTracingFlowBeginEvent))]
    [JsonSerializable(typeof(ChromiumTracingFlowEndEvent))]
    [JsonSerializable(typeof(ChromiumTracingFlowStepEvent))]
    [JsonSerializable(typeof(ChromiumTracingInstantEvent))]
    [JsonSerializable(typeof(ChromiumTracingLinkIdEvent))]
    [JsonSerializable(typeof(ChromiumTracingMarkEvent))]
    [JsonSerializable(typeof(ChromiumTracingMemoryDumpGlobalEvent))]
    [JsonSerializable(typeof(ChromiumTracingMemoryDumpProcessEvent))]
    [JsonSerializable(typeof(ChromiumTracingMetadataEvent))]
    [JsonSerializable(typeof(ChromiumTracingObjectCreatedEvent))]
    [JsonSerializable(typeof(ChromiumTracingObjectDestroyedEvent))]
    [JsonSerializable(typeof(ChromiumTracingObjectSnapshotEvent))]
    [JsonSerializable(typeof(long))]
    private sealed partial class SourceGenerationContext : JsonSerializerContext
    {
    }
}
