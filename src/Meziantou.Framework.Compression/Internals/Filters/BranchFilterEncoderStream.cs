using System.Buffers;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Filters;

/// <summary>Applies a BCJ filter to the data written to the stream, and writes the filtered data to <c>output</c>.</summary>
internal sealed class BranchFilterEncoderStream : WriteOnlyStreamBase
{
    private const int BufferSize = 1 << 16;

    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private readonly BranchFilter _filter;
    private byte[] _buffer;
    private int _length;
    private bool _disposed;

    /// <param name="output">The stream that receives the filtered data.</param>
    /// <param name="kind">The architecture.</param>
    /// <param name="startOffset">The start offset of the filter (the address of the first byte).</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="output"/> open when the stream is disposed.</param>
    public BranchFilterEncoderStream(Stream output, BranchFilterKind kind, uint startOffset, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(output);
        _filter = new BranchFilter(kind, startOffset, isEncoder: true);
        _output = output;
        _leaveOpen = leaveOpen;
        _buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (!buffer.IsEmpty)
        {
            var count = Math.Min(buffer.Length, _buffer.Length - _length);
            buffer[..count].CopyTo(_buffer.AsSpan(_length));
            _length += count;
            buffer = buffer[count..];
            if (_length == _buffer.Length)
            {
                var processed = _filter.Convert(_buffer.AsSpan(0, _length));
                _output.Write(_buffer, 0, processed);
                _buffer.AsSpan(processed, _length - processed).CopyTo(_buffer);
                _length -= processed;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try
            {
                // The bytes that the filter cannot convert at the end of the stream are written unchanged
                _filter.Convert(_buffer.AsSpan(0, _length));
                _output.Write(_buffer, 0, _length);
                _output.Flush();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = [];
                if (!_leaveOpen)
                {
                    _output.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }
}
