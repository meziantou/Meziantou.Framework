using System.Buffers;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Filters;

/// <summary>Reverts a BCJ filter: reads the filtered data from <c>input</c> and returns the original data.</summary>
internal sealed class BranchFilterDecoderStream : ReadOnlyStreamBase
{
    private const int BufferSize = 1 << 16;

    private readonly Stream _input;
    private readonly bool _leaveOpen;
    private readonly BranchFilter _filter;
    private byte[] _buffer;
    private int _start;
    private int _filteredEnd;
    private int _end;
    private bool _endOfInput;
    private bool _disposed;

    /// <param name="input">The filtered data.</param>
    /// <param name="kind">The architecture.</param>
    /// <param name="startOffset">The start offset of the filter (the address of the first byte).</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="input"/> open when the stream is disposed.</param>
    public BranchFilterDecoderStream(Stream input, BranchFilterKind kind, uint startOffset, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(input);
        _filter = new BranchFilter(kind, startOffset, isEncoder: false);
        _input = input;
        _leaveOpen = leaveOpen;
        _buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
            return 0;

        while (_start == _filteredEnd)
        {
            if (_endOfInput)
            {
                if (_filteredEnd == _end)
                    return 0;

                // The bytes that the filter could not convert at the end of the stream are unchanged
                _filteredEnd = _end;
                break;
            }

            // Keep the bytes that are not converted yet, and read more data after them
            var remaining = _end - _filteredEnd;
            _buffer.AsSpan(_filteredEnd, remaining).CopyTo(_buffer);
            _start = _filteredEnd = 0;
            _end = remaining;
            while (_end < _buffer.Length)
            {
                var read = _input.Read(_buffer, _end, _buffer.Length - _end);
                if (read <= 0)
                {
                    _endOfInput = true;
                    break;
                }

                _end += read;
                if (_end - remaining >= BranchFilter.MaxUnprocessed)
                    break;
            }

            _filteredEnd = _filter.Convert(_buffer.AsSpan(0, _end));
        }

        var count = Math.Min(buffer.Length, _filteredEnd - _start);
        _buffer.AsSpan(_start, count).CopyTo(buffer);
        _start += count;
        return count;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
            if (!_leaveOpen)
            {
                _input.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
