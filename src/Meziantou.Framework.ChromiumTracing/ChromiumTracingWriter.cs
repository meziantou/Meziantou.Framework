using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

// https://docs.google.com/document/d/1CvAClvFfyA5R-PhYUmn5OOQtYMH4h6I0nSsKchNAySU/preview#
// https://github.com/catapult-project/catapult/blob/6d5a4e52871813b8b2e71b378fc54bca459600c4/tracing/tracing/extras/importer/trace_event_importer.html
public sealed class ChromiumTracingWriter : IAsyncDisposable
{
    private static readonly byte[] s_arrayEmpty = new[] { (byte)'[', (byte)']' };
    private static readonly byte[] s_arrayStart = new[] { (byte)'[', (byte)'\n' };
    private static readonly byte[] s_arrayEnd = new[] { (byte)'\n', (byte)']' };
    private static readonly byte[] s_arrayItemSeparator = new[] { (byte)',', (byte)'\n' };
    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.General)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        IgnoreReadOnlyProperties = false,
    };

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
            await _stream.WriteAsync(s_arrayEnd).ConfigureAwait(false);
        }
        else
        {
            await _stream.WriteAsync(s_arrayEmpty).ConfigureAwait(false);
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
            await _stream.WriteAsync(s_arrayItemSeparator, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _stream.WriteAsync(s_arrayStart, cancellationToken).ConfigureAwait(false);
            _hasItems = true;
        }

        await JsonSerializer.SerializeAsync(_stream, tracingEvent, tracingEvent.GetType(), s_options, cancellationToken).ConfigureAwait(false);
    }
}
