using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The bit reader of one entropy-coded segment (the data between two restart markers, ITU-T T.81 section F.2.2.5). The
/// decoder pushes the segment bytes (byte stuffing already removed) as the container walker delivers them; the reader keeps
/// them in a bounded buffer and serves bits most significant first.
/// </summary>
/// <remarks>
/// While the end of the segment is unknown, the decoder only decodes an MCU when <see cref="AvailableBytes"/> covers the
/// largest possible MCU, so the reader never runs dry. Once <see cref="MarkEndOfSegment"/> was called, missing bits read as
/// 1-bits (the T.81 fill bits) so that Huffman codes can be looked ahead, but consuming any of them means the data is
/// truncated: <see cref="InvalidImageContentException"/>.
/// </remarks>
internal sealed class JpegEntropyReader : IDisposable
{
    private PooledBuffer? _buffer;
    private byte[] _data;
    private int _start;
    private int _end;

    // Valid bits are the most significant _bitCount bits; the last _paddingBits of them are fill bits added after the end
    private ulong _bits;
    private int _bitCount;
    private int _paddingBits;
    private bool _endOfSegment;

    public JpegEntropyReader(AllocationScope scope, int capacity)
    {
        _buffer = scope.Rent(capacity, AllocationKind.DecoderState, clear: false);
        _data = _buffer.RawBuffer;
    }

    /// <summary>Gets the number of unread bytes, including the whole bytes held in the bit accumulator.</summary>
    public int AvailableBytes => _end - _start + ((_bitCount - _paddingBits) >> 3);

    /// <summary>Gets the number of bytes that <see cref="Append"/> can accept without <see cref="Compact"/>.</summary>
    public int FreeSpace => _data.Length - _end;

    public bool IsEndOfSegment => _endOfSegment;

    /// <summary>Appends segment bytes (unstuffed).</summary>
    public void Append(ReadOnlySpan<byte> bytes)
    {
        Debug.Assert(!_endOfSegment);
        bytes.CopyTo(_data.AsSpan(_end));
        _end += bytes.Length;
    }

    /// <summary>Moves the unread bytes to the start of the buffer.</summary>
    public void Compact()
    {
        if (_start == 0)
            return;

        _data.AsSpan(_start, _end - _start).CopyTo(_data);
        _end -= _start;
        _start = 0;
    }

    /// <summary>Declares that every byte of the segment was appended.</summary>
    public void MarkEndOfSegment() => _endOfSegment = true;

    /// <summary>Discards the rest of the segment (at a restart marker) and starts a new one.</summary>
    public void Reset()
    {
        _start = 0;
        _end = 0;
        _bits = 0;
        _bitCount = 0;
        _paddingBits = 0;
        _endOfSegment = false;
    }

    /// <summary>Decodes one Huffman-coded symbol.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeHuffman(JpegHuffmanTable table)
    {
        if (_bitCount < 16)
        {
            Fill();
        }

        var entry = table.Lookup((int)(_bits >> (64 - JpegHuffmanTable.LookupBits)));
        if (entry != 0)
        {
            Skip(entry >> 8);
            return entry & 0xFF;
        }

        return DecodeLongCode(table);
    }

    /// <summary>Reads an <paramref name="size"/>-bit magnitude and extends its sign (T.81 F.2.2.1, EXTEND).</summary>
    /// <param name="size">The magnitude category, 1 to 16.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReceiveExtend(int size)
    {
        Debug.Assert(size is > 0 and <= 16);
        if (_bitCount < size)
        {
            Fill();
        }

        var value = (int)(_bits >> (64 - size));
        Skip(size);

        // Values below 2^(size-1) are negative: value - (2^size - 1)
        return value < (1 << (size - 1)) ? value - (1 << size) + 1 : value;
    }

    /// <summary>Reads <paramref name="count"/> bits as an unsigned value (EOB run lengths, progressive correction and sign bits).</summary>
    /// <param name="count">The number of bits, 1 to 16.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadBits(int count)
    {
        Debug.Assert(count is > 0 and <= 16);
        if (_bitCount < count)
        {
            Fill();
        }

        var value = (int)(_bits >> (64 - count));
        Skip(count);
        return value;
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        _data = [];
    }

    private int DecodeLongCode(JpegHuffmanTable table)
    {
        var symbol = table.DecodeLong((int)(_bits >> 48), out var length);
        if (symbol < 0)
        {
            if (_bitCount - _paddingBits < 16 && _endOfSegment)
                throw CreateTruncatedException();

            throw new InvalidImageContentException("The JPEG entropy-coded data contains an invalid Huffman code.", ImageFormat.Jpeg);
        }

        Skip(length);
        return symbol;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Skip(int count)
    {
        _bits <<= count;
        _bitCount -= count;
        if (_bitCount < _paddingBits)
            throw CreateTruncatedException();
    }

    /// <summary>Loads bytes until the accumulator holds at least 57 bits (or the buffered bytes are exhausted).</summary>
    private void Fill()
    {
        while (_bitCount <= 56)
        {
            if (_start < _end)
            {
                _bits |= (ulong)_data[_start++] << (56 - _bitCount);
            }
            else if (_endOfSegment)
            {
                _bits |= 0xFFUL << (56 - _bitCount);
                _paddingBits += 8;
            }
            else
            {
                // More bytes will come: callers decode only when the bytes of a whole MCU are buffered
                return;
            }

            _bitCount += 8;
        }
    }

    private static InvalidImageContentException CreateTruncatedException() => new("The JPEG entropy-coded data is truncated.", ImageFormat.Jpeg);
}
