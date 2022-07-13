using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

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

    public ChromiumTracingWriter(Stream stream)
        : this(stream, streamOwned: false)
    {
    }

    private ChromiumTracingWriter(Stream stream, bool streamOwned)
    {
        _stream = stream;
        _streamOwned = streamOwned;
    }

    public static ChromiumTracingWriter Create(string path)
    {
        var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        return new ChromiumTracingWriter(fs, streamOwned: true);
    }

    public static ChromiumTracingWriter Create(Stream stream)
    {
        return Create(stream, streamOwned: true);
    }

    public static ChromiumTracingWriter Create(Stream stream, bool streamOwned)
    {
        return new ChromiumTracingWriter(stream, streamOwned);
    }

    public static ChromiumTracingWriter CreateGzip(string path, CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        var gzip = new GZipStream(fs, compressionLevel, leaveOpen: false);
        return new ChromiumTracingWriter(gzip, streamOwned: true);
    }

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

    public async Task WriteEventAsync(ChromiumTracingEvent tracingEvent, CancellationToken cancellationToken = default)
    {
        if (tracingEvent == null)
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
