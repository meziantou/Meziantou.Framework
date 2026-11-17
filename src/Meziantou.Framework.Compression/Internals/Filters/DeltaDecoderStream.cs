// A port of the delta filter of xz-utils (liblzma/delta), which is in the public domain.
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Filters;

/// <summary>Reverts the delta filter: each byte is added to the byte <c>distance</c> bytes before it.</summary>
internal sealed class DeltaDecoderStream : ReadOnlyStreamBase
{
    private readonly Stream _input;
    private readonly bool _leaveOpen;
    private readonly int _distance;
    private readonly byte[] _history = new byte[256];
    private byte _position;
    private bool _disposed;

    /// <param name="input">The filtered data.</param>
    /// <param name="distance">The distance, from 1 to 256.</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="input"/> open when the stream is disposed.</param>
    public DeltaDecoderStream(Stream input, int distance, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfLessThan(distance, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(distance, 256);
        _input = input;
        _distance = distance;
        _leaveOpen = leaveOpen;
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var read = _input.Read(buffer);
        var history = _history;
        var distance = _distance;
        var position = _position;
        for (var i = 0; i < read; i++)
        {
            var value = (byte)(buffer[i] + history[(byte)(distance + position)]);
            buffer[i] = value;
            history[position--] = value;
        }

        _position = position;
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            if (!_leaveOpen)
            {
                _input.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
