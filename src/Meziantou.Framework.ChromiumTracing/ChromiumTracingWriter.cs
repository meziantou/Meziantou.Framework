using System.Buffers;
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
    private readonly SemaphoreSlim _semaphore = new(initialCount: 1, maxCount: 1);
    private bool _hasItems;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ChromiumTracingWriter"/> class with the specified stream.</summary>
    /// <param name="stream">The stream to write trace events to. The stream is <b>not</b> disposed when the writer is disposed; the caller keeps ownership of it.</param>
    public ChromiumTracingWriter(Stream stream)
        : this(stream, streamOwned: false, serializerContext: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ChromiumTracingWriter"/> class with the specified stream and serializer context.</summary>
    /// <param name="stream">The stream to write trace events to. The stream is <b>not</b> disposed when the writer is disposed; the caller keeps ownership of it.</param>
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

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes to the specified stream and takes ownership of it.</summary>
    /// <param name="stream">The stream to write trace events to. It is disposed when the writer is disposed. Use <see cref="Create(Stream, bool)"/> or the constructor to keep ownership of the stream.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter Create(Stream stream)
    {
        return Create(stream, streamOwned: true, serializerContext: null);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes to the specified stream and takes ownership of it.</summary>
    /// <param name="stream">The stream to write trace events to. It is disposed when the writer is disposed. Use <see cref="Create(Stream, bool, JsonSerializerContext?)"/> or the constructor to keep ownership of the stream.</param>
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
    /// <param name="stream">The stream to write compressed trace events to. It is <b>not</b> disposed when the writer is disposed; only the compression stream wrapping it is.</param>
    /// <param name="compressionLevel">The compression level to use.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter CreateGzip(Stream stream, CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        var gzip = new GZipStream(stream, compressionLevel, leaveOpen: true);
        return new ChromiumTracingWriter(gzip, streamOwned: true, serializerContext: null);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes GZip-compressed trace events to the specified stream.</summary>
    /// <param name="stream">The stream to write compressed trace events to. It is <b>not</b> disposed when the writer is disposed; only the compression stream wrapping it is.</param>
    /// <param name="compressionLevel">The compression level to use.</param>
    /// <param name="serializerContext">The serializer context to combine with the built-in one.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter CreateGzip(Stream stream, JsonSerializerContext? serializerContext, CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        var gzip = new GZipStream(stream, compressionLevel, leaveOpen: true);
        return new ChromiumTracingWriter(gzip, streamOwned: true, serializerContext);
    }

    /// <summary>Finalizes the JSON array and disposes the underlying stream if owned. Subsequent calls do nothing.</summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            await _semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(_hasItems ? ArrayEnd : ArrayEmpty).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }
        }
        finally
        {
            // The stream must be released even when the closing bracket could not be written,
            // otherwise a failure at the very end leaks the file handle and its buffered content.
            _semaphore.Dispose();

            if (_streamOwned)
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Writes a trace event to the stream.</summary>
    /// <param name="tracingEvent">The trace event to write.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "The json options are guarantee to contains the TypeResolver for events")]
    [UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling", Justification = "The options only use source-generated resolvers, so a type that is not registered fails with NotSupportedException instead of falling back to reflection")]
    public async Task WriteEventAsync(ChromiumTracingEvent tracingEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracingEvent);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Serialize the event before writing anything to the stream. Writing the item separator first would
        // leave it dangling when the serialization fails, which makes the whole document unparsable.
        // Serializing outside the lock also keeps the critical section down to the stream writes.
        var buffer = new ArrayBufferWriter<byte>();
        using (var jsonWriter = new Utf8JsonWriter(buffer))
        {
            JsonSerializer.Serialize(jsonWriter, tracingEvent, tracingEvent.GetType(), _jsonSerializerOptions);
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(_hasItems ? ArrayItemSeparator : ArrayStart, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
            _hasItems = true;
        }
        finally
        {
            _semaphore.Release();
        }
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
    // Values in ChromiumTracingEvent.Arguments are resolved by their runtime type. Without these,
    // only the types that happen to appear on the event properties can be used as argument values.
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(byte))]
    [JsonSerializable(typeof(char))]
    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(DateTimeOffset))]
    [JsonSerializable(typeof(decimal))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(float))]
    [JsonSerializable(typeof(Guid))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(sbyte))]
    [JsonSerializable(typeof(short))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(TimeSpan))]
    [JsonSerializable(typeof(uint))]
    [JsonSerializable(typeof(ulong))]
    [JsonSerializable(typeof(ushort))]
    [JsonSerializable(typeof(Uri))]
    private sealed partial class SourceGenerationContext : JsonSerializerContext
    {
    }
}
