using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    private bool _hasItems;

    /// <summary>Initializes a new instance of the <see cref="ChromiumTracingWriter"/> class with the specified stream.</summary>
    /// <param name="stream">The stream to write trace events to.</param>
    public ChromiumTracingWriter(Stream stream)
        : this(stream, streamOwned: false)
    {
    }

    private ChromiumTracingWriter(Stream stream, bool streamOwned)
    {
        _stream = stream;
        _streamOwned = streamOwned;
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes to a file at the specified path.</summary>
    /// <param name="path">The file path where trace events will be written.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter Create(string path)
    {
        var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        return new ChromiumTracingWriter(fs, streamOwned: true);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes to the specified stream.</summary>
    /// <param name="stream">The stream to write trace events to.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter Create(Stream stream)
    {
        return Create(stream, streamOwned: true);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes to the specified stream.</summary>
    /// <param name="stream">The stream to write trace events to.</param>
    /// <param name="streamOwned">Indicates whether the stream should be disposed when the writer is disposed.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter Create(Stream stream, bool streamOwned)
    {
        return new ChromiumTracingWriter(stream, streamOwned);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes GZip-compressed trace events to a file at the specified path.</summary>
    /// <param name="path">The file path where compressed trace events will be written.</param>
    /// <param name="compressionLevel">The compression level to use.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter CreateGzip(string path, CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        var gzip = new GZipStream(fs, compressionLevel, leaveOpen: false);
        return new ChromiumTracingWriter(gzip, streamOwned: true);
    }

    /// <summary>Creates a new <see cref="ChromiumTracingWriter"/> that writes GZip-compressed trace events to the specified stream.</summary>
    /// <param name="stream">The stream to write compressed trace events to.</param>
    /// <param name="compressionLevel">The compression level to use.</param>
    /// <returns>A new <see cref="ChromiumTracingWriter"/> instance.</returns>
    public static ChromiumTracingWriter CreateGzip(Stream stream, CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        var gzip = new GZipStream(stream, compressionLevel, leaveOpen: true);
        return new ChromiumTracingWriter(gzip, streamOwned: true);
    }

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

        await JsonSerializer.SerializeAsync(_stream, tracingEvent, tracingEvent.GetType(), SourceGenerationContext.Default, cancellationToken).ConfigureAwait(false);
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
    private sealed partial class SourceGenerationContext : JsonSerializerContext
    {
    }
}
