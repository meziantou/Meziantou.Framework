using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The bit reader of one entropy-coded segment (the data between two restart markers, ITU-T T.81 section F.2.2.5). The
/// decoder pushes the segment bytes (byte stuffing already removed) as the container walker delivers them; the reader keeps
/// them in a bounded buffer and serves bits most significant first to the Huffman decoders, or whole bytes to the
/// arithmetic decoder (<see cref="ReadByteOrZero"/>).
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

    /// <summary>
    /// Reads the next byte of the segment for the arithmetic decoder, or zero after its end: the decoder is fed zero bytes
    /// once the marker that ends the segment is met (T.81 D.2.6), so this never fails.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadByteOrZero()
    {
        Debug.Assert(_bitCount == 0);
        return _start < _end ? _data[_start++] : 0;
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

        // Values below 2^(size-1) are negative: value - (2^size - 1), without a branch (the sign of value - 2^(size-1)
        // selects the correction)
        return value + (((value - (1 << (size - 1))) >> 31) & ((-1 << size) + 1));
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

    /// <summary>Copies the bit state into a <see cref="Cursor"/>, which keeps it in locals while the blocks of an MCU are decoded.</summary>
    /// <returns>The cursor; <see cref="Cursor.Commit"/> writes its state back.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Cursor GetCursor() => new(this);

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
    /// <remarks>
    /// With 8 buffered bytes, the 8 next bytes are loaded at once and only the whole bytes that fit are consumed: the bits
    /// below the valid ones are then the next bits of the data instead of zeros, which nothing reads before they become
    /// valid (they are ORed again, with the same values, when their byte is consumed).
    /// </remarks>
    private void Fill()
    {
        if (_end - _start >= 8)
        {
            _bits |= BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(_start)) >> _bitCount;
            var count = (63 - _bitCount) >> 3;
            _start += count;
            _bitCount += count << 3;
            return;
        }

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

    /// <summary>
    /// The bit state of the reader copied into locals: the methods of the reader, with the accumulator kept in registers
    /// between the symbols of a block instead of being loaded from and stored to the reader at every symbol. The state is
    /// written back by <see cref="Commit"/>; the slow paths (loading bytes, codes longer than
    /// <see cref="JpegHuffmanTable.LookupBits"/>) write it back, run the method of the reader and load it again. Every
    /// member is inlined, so that the cursor stays a set of registers.
    /// </summary>
    internal ref struct Cursor
    {
        private readonly JpegEntropyReader _reader;
        private int _start;
        private ulong _bits;
        private int _bitCount;
        private int _paddingBits;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cursor(JpegEntropyReader reader)
        {
            _reader = reader;
            _start = reader._start;
            _bits = reader._bits;
            _bitCount = reader._bitCount;
            _paddingBits = reader._paddingBits;
        }

        /// <summary>Writes the state back to the reader.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Commit()
        {
            _reader._start = _start;
            _reader._bits = _bits;
            _reader._bitCount = _bitCount;
        }

        /// <inheritdoc cref="JpegEntropyReader.DecodeHuffman"/>
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

            Commit();
            var symbol = _reader.DecodeLongCode(table);
            Reload();
            return symbol;
        }

        /// <summary>
        /// Decodes the next AC coefficient when its code and its magnitude bits fit in <see cref="JpegHuffmanTable.LookupBits"/>
        /// (<see cref="JpegHuffmanTable.FastAc"/>) and are not fill bits; otherwise consumes nothing.
        /// </summary>
        /// <param name="fastAc">The <see cref="JpegHuffmanTable.FastAc"/> of the table.</param>
        /// <returns><c>(coefficient &lt;&lt; 8) | (run &lt;&lt; 4) | length</c>, or 0 when the regular path must decode the symbol.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int TryDecodeFastAc(int[] fastAc)
        {
            if (_bitCount < 16)
            {
                Fill();
            }

            var entry = fastAc[(int)(_bits >> (64 - JpegHuffmanTable.LookupBits))];
            var length = entry & 0x0F;

            // Fill bits are left to the regular path, which reports the truncation where it occurs
            if (entry == 0 || _bitCount - length < _paddingBits)
                return 0;

            _bits <<= length;
            _bitCount -= length;
            return entry;
        }

        /// <inheritdoc cref="JpegEntropyReader.ReceiveExtend"/>
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

            // Values below 2^(size-1) are negative: value - (2^size - 1), without a branch (the sign of value - 2^(size-1)
            // selects the correction)
            return value + (((value - (1 << (size - 1))) >> 31) & ((-1 << size) + 1));
        }

        /// <inheritdoc cref="JpegEntropyReader.ReadBits"/>
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Skip(int count)
        {
            _bits <<= count;
            _bitCount -= count;
            if (_bitCount < _paddingBits)
                throw CreateTruncatedException();
        }

        /// <summary>The bulk path of <see cref="JpegEntropyReader.Fill"/>, inlined; the other cases are the reader's.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Fill()
        {
            // The buffer and its end are read from the reader, which keeps fewer values alive in the decoding loops
            var reader = _reader;
            if (reader._end - _start >= 8)
            {
                _bits |= BinaryPrimitives.ReadUInt64BigEndian(reader._data.AsSpan(_start)) >> _bitCount;
                var count = (63 - _bitCount) >> 3;
                _start += count;
                _bitCount += count << 3;
            }
            else
            {
                Commit();
                _reader.Fill();
                Reload();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Reload()
        {
            _start = _reader._start;
            _bits = _reader._bits;
            _bitCount = _reader._bitCount;
            _paddingBits = _reader._paddingBits;
        }
    }
}
