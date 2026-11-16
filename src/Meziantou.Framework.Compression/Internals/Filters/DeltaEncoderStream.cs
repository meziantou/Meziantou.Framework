// A port of the delta filter of xz-utils (liblzma/delta), which is in the public domain.
using System.Buffers;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Filters;

/// <summary>Applies the delta filter: each byte is replaced by its difference with the byte <c>distance</c> bytes before it.</summary>
internal sealed class DeltaEncoderStream : WriteOnlyStreamBase
{
    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private readonly int _distance;
    private readonly byte[] _history = new byte[256];
    private byte _position;
    private bool _disposed;

    /// <param name="output">The stream that receives the filtered data.</param>
    /// <param name="distance">The distance, from 1 to 256.</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="output"/> open when the stream is disposed.</param>
    public DeltaEncoderStream(Stream output, int distance, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfLessThan(distance, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(distance, 256);
        _output = output;
        _distance = distance;
        _leaveOpen = leaveOpen;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var chunk = ArrayPool<byte>.Shared.Rent(Math.Min(buffer.Length, 1 << 16));
        try
        {
            while (!buffer.IsEmpty)
            {
                var count = Math.Min(buffer.Length, chunk.Length);
                var history = _history;
                var distance = _distance;
                var position = _position;
                for (var i = 0; i < count; i++)
                {
                    var value = buffer[i];
                    var previous = history[(byte)(distance + position)];
                    history[position--] = value;
                    chunk[i] = (byte)(value - previous);
                }

                _position = position;
                _output.Write(chunk, 0, count);
                buffer = buffer[count..];
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try
            {
                _output.Flush();
            }
            finally
            {
                if (!_leaveOpen)
                {
                    _output.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }
}
