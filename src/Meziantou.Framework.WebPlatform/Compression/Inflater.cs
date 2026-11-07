using System.Buffers.Binary;
using System.IO.Hashing;

namespace Meziantou.Framework.WebPlatform.Compression;

/// <summary>The header and the trailer around the DEFLATE data.</summary>
internal enum InflaterWrapper
{
    /// <summary>The raw DEFLATE data (RFC 1951).</summary>
    None,

    /// <summary>The zlib format (RFC 1950).</summary>
    ZLib,

    /// <summary>The gzip format (RFC 1952), with a single member.</summary>
    Gzip,
}

/// <summary>
/// An incremental DEFLATE decoder (RFC 1951) and its zlib (RFC 1950) and gzip (RFC 1952) wrappers, with the requirements
/// of the Compression Standard (https://compression.spec.whatwg.org/#supported-formats): the preset dictionaries of zlib
/// (FDICT), the incorrect checksums, a gzip header CRC (FHCRC) that does not match, and the input after the end of the
/// data (or of the first gzip member) are errors. The base class library cannot be used: its decoders read from a stream
/// and do not report where the compressed data ends.
/// </summary>
/// <remarks>
/// The input is retained until it is consumed. Each step (a header, a block header, a code and its extra bits...) is
/// decoded only when the input contains all of it: otherwise the bit position is restored and the step is decoded again
/// when the next chunk is received.
/// </remarks>
internal sealed class Inflater : Decompressor
{
    private const int WindowSize = 32768;
    private const int MaxCodeLength = 15;

    // https://www.rfc-editor.org/rfc/rfc1951#section-3.2.5
    private static readonly ushort[] LengthBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
    private static readonly byte[] LengthExtraBits = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    private static readonly ushort[] DistanceBase = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
    private static readonly byte[] DistanceExtraBits = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];

    // https://www.rfc-editor.org/rfc/rfc1951#section-3.2.7: the order of the code lengths of the code length alphabet
    private static readonly byte[] CodeLengthOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

    private static readonly Huffman FixedLiteralLengthCode = CreateFixedLiteralLengthCode();
    private static readonly Huffman FixedDistanceCode = CreateFixedDistanceCode();

    private readonly InflaterWrapper _wrapper;
    private State _state;

    // The input that is not consumed yet; _bitPosition is the position of the next bit in _input
    private byte[] _input = [];
    private int _inputLength;
    private long _bitPosition;

    // The output of the current call, and the last 32 KiB of the output (the sliding window of the back-references)
    private byte[] _output = [];
    private int _outputLength;
    private int _checksumOffset;
    private readonly byte[] _window = new byte[WindowSize];
    private long _totalOutput;

    private bool _finalBlock;
    private int _storedRemaining;
    private Huffman? _literalLengthCode;
    private Huffman? _distanceCode;
    private readonly Adler32? _adler32;
    private readonly Crc32? _crc32;

    public Inflater(InflaterWrapper wrapper)
    {
        _wrapper = wrapper;
        if (wrapper == InflaterWrapper.ZLib)
        {
            _adler32 = new Adler32();
        }
        else if (wrapper == InflaterWrapper.Gzip)
        {
            _crc32 = new Crc32();
        }
    }

    private enum State
    {
        Header,
        BlockHeader,
        StoredBlock,
        HuffmanBlock,
        Trailer,
        Ended,
    }

    public override bool IsEnded => _state == State.Ended;

    public override byte[] Decompress(ReadOnlySpan<byte> input)
    {
        HasTrailingInput = false;
        if (_state == State.Ended)
        {
            HasTrailingInput = !input.IsEmpty;
            return [];
        }

        AppendInput(input);
        _outputLength = 0;
        _checksumOffset = 0;
        while (Step())
        {
        }

        UpdateChecksum();
        if (_state == State.Ended)
        {
            // The rest of the last byte of the DEFLATE data is padding
            HasTrailingInput = _inputLength > (_bitPosition + 7) >> 3;
            _input = [];
            _inputLength = 0;
            _bitPosition = 0;
        }
        else
        {
            DiscardConsumedInput();
        }

        var result = _output.AsSpan(0, _outputLength).ToArray();
        _outputLength = 0;
        return result;
    }

    public override void Dispose()
    {
    }

    /// <summary>Decodes the next step: returns false when more input is needed or the end of the data is reached.</summary>
    private bool Step()
    {
        switch (_state)
        {
            case State.Header:
                return ReadHeader();

            case State.BlockHeader:
                return ReadBlockHeader();

            case State.StoredBlock:
                return CopyStoredBlock();

            case State.HuffmanBlock:
                return DecodeHuffmanBlock();

            case State.Trailer:
                return ReadTrailer();

            default:
                return false;
        }
    }

    private bool ReadHeader()
    {
        switch (_wrapper)
        {
            case InflaterWrapper.ZLib:
                // https://www.rfc-editor.org/rfc/rfc1950#section-2.2
                if (AvailableBytes < 2)
                    return false;

                var cmf = _input[0];
                var flg = _input[1];
                if ((cmf & 0x0F) != 8)
                    throw new InvalidDataException("The compression method of the zlib data is not deflate");

                if ((cmf >> 4) > 7)
                    throw new InvalidDataException("The window size of the zlib data is invalid");

                if (((cmf << 8) | flg) % 31 != 0)
                    throw new InvalidDataException("The header check bits of the zlib data are invalid");

                if ((flg & 0x20) != 0)
                    throw new InvalidDataException("The zlib data requires a preset dictionary");

                _bitPosition = 16;
                break;

            case InflaterWrapper.Gzip:
                if (!ReadGzipHeader())
                    return false;

                break;
        }

        _state = State.BlockHeader;
        return true;
    }

    // https://www.rfc-editor.org/rfc/rfc1952#section-2.3
    private bool ReadGzipHeader()
    {
        var header = _input.AsSpan(0, _inputLength);
        if ((header.Length >= 1 && header[0] != 0x1F) || (header.Length >= 2 && header[1] != 0x8B))
            throw new InvalidDataException("The gzip data does not start with the gzip identification bytes");

        if (header.Length >= 3 && header[2] != 8)
            throw new InvalidDataException("The compression method of the gzip data is not deflate");

        if (header.Length >= 4 && (header[3] & 0xE0) != 0)
            throw new InvalidDataException("The reserved flags of the gzip header are set");

        if (header.Length < 10)
            return false;

        var flags = header[3];
        var position = 10;

        // FEXTRA
        if ((flags & 0x04) != 0)
        {
            if (header.Length < position + 2)
                return false;

            position += 2 + BinaryPrimitives.ReadUInt16LittleEndian(header[position..]);
            if (header.Length < position)
                return false;
        }

        // FNAME and FCOMMENT: zero-terminated strings
        foreach (var flag in (ReadOnlySpan<byte>)[0x08, 0x10])
        {
            if ((flags & flag) == 0)
                continue;

            var end = header[position..].IndexOf((byte)0);
            if (end < 0)
                return false;

            position += end + 1;
        }

        // FHCRC: the two least significant bytes of the CRC-32 of the header
        if ((flags & 0x02) != 0)
        {
            if (header.Length < position + 2)
                return false;

            var crc = Crc32.HashToUInt32(header[..position]);
            if ((ushort)crc != BinaryPrimitives.ReadUInt16LittleEndian(header[position..]))
                throw new InvalidDataException("The CRC of the gzip header is incorrect");

            position += 2;
        }

        _bitPosition = position * 8L;
        return true;
    }

    // https://www.rfc-editor.org/rfc/rfc1951#section-3.2.3
    private bool ReadBlockHeader()
    {
        var start = _bitPosition;
        if (!TryReadBits(3, out var header))
            return false;

        _finalBlock = (header & 1) != 0;
        switch (header >> 1)
        {
            case 0:
                // https://www.rfc-editor.org/rfc/rfc1951#section-3.2.4
                AlignToByte();
                if (AvailableBytes < 4)
                {
                    _bitPosition = start;
                    return false;
                }

                var offset = (int)(_bitPosition >> 3);
                var length = BinaryPrimitives.ReadUInt16LittleEndian(_input.AsSpan(offset));
                var complement = BinaryPrimitives.ReadUInt16LittleEndian(_input.AsSpan(offset + 2));
                if (length != (ushort)~complement)
                    throw new InvalidDataException("The length of a stored block does not match its complement");

                _bitPosition += 32;
                _storedRemaining = length;
                _state = State.StoredBlock;
                return true;

            case 1:
                _literalLengthCode = FixedLiteralLengthCode;
                _distanceCode = FixedDistanceCode;
                _state = State.HuffmanBlock;
                return true;

            case 2:
                if (!ReadDynamicCodes())
                {
                    _bitPosition = start;
                    return false;
                }

                _state = State.HuffmanBlock;
                return true;

            default:
                throw new InvalidDataException("The block type of the DEFLATE data is invalid");
        }
    }

    // https://www.rfc-editor.org/rfc/rfc1951#section-3.2.7
    private bool ReadDynamicCodes()
    {
        if (!TryReadBits(5, out var hlit) || !TryReadBits(5, out var hdist) || !TryReadBits(4, out var hclen))
            return false;

        var literalLengthCount = hlit + 257;
        var distanceCount = hdist + 1;
        if (literalLengthCount > 286 || distanceCount > 30)
            throw new InvalidDataException("The DEFLATE data has too many length or distance codes");

        Span<byte> lengths = stackalloc byte[286 + 30];
        lengths.Clear();
        for (var i = 0; i < hclen + 4; i++)
        {
            if (!TryReadBits(3, out var length))
                return false;

            lengths[CodeLengthOrder[i]] = (byte)length;
        }

        var codeLengthCode = Huffman.Create(lengths[..19], isCodeLengthCode: true);
        var count = literalLengthCount + distanceCount;
        var index = 0;
        while (index < count)
        {
            if (!TryDecodeSymbol(codeLengthCode, out var symbol))
                return false;

            if (symbol < 16)
            {
                lengths[index++] = (byte)symbol;
                continue;
            }

            byte repeatedLength = 0;
            int repeat;
            if (symbol == 16)
            {
                if (index == 0)
                    throw new InvalidDataException("The DEFLATE data repeats a code length with no previous length");

                repeatedLength = lengths[index - 1];
                if (!TryReadBits(2, out repeat))
                    return false;

                repeat += 3;
            }
            else if (symbol == 17)
            {
                if (!TryReadBits(3, out repeat))
                    return false;

                repeat += 3;
            }
            else
            {
                if (!TryReadBits(7, out repeat))
                    return false;

                repeat += 11;
            }

            if (index + repeat > count)
                throw new InvalidDataException("The DEFLATE data has too many code lengths");

            lengths.Slice(index, repeat).Fill(repeatedLength);
            index += repeat;
        }

        if (lengths[256] == 0)
            throw new InvalidDataException("The DEFLATE data has no end-of-block code");

        _literalLengthCode = Huffman.Create(lengths[..literalLengthCount], isCodeLengthCode: false);
        _distanceCode = Huffman.Create(lengths.Slice(literalLengthCount, distanceCount), isCodeLengthCode: false);
        return true;
    }

    private bool CopyStoredBlock()
    {
        if (_storedRemaining == 0)
        {
            _state = _finalBlock ? State.Trailer : State.BlockHeader;
            return true;
        }

        var count = Math.Min(_storedRemaining, AvailableBytes);
        if (count == 0)
            return false;

        var offset = (int)(_bitPosition >> 3);
        foreach (var value in _input.AsSpan(offset, count))
        {
            WriteByte(value);
        }

        _bitPosition += count * 8L;
        _storedRemaining -= count;
        return true;
    }

    // https://www.rfc-editor.org/rfc/rfc1951#section-3.2.5
    private bool DecodeHuffmanBlock()
    {
        var literalLengthCode = _literalLengthCode!;
        var distanceCode = _distanceCode!;
        while (true)
        {
            var start = _bitPosition;
            if (!TryDecodeSymbol(literalLengthCode, out var symbol))
                return false;

            if (symbol < 256)
            {
                WriteByte((byte)symbol);
                continue;
            }

            if (symbol == 256)
            {
                _state = _finalBlock ? State.Trailer : State.BlockHeader;
                return true;
            }

            symbol -= 257;
            if (symbol >= LengthBase.Length)
                throw new InvalidDataException("The DEFLATE data has an invalid length code");

            if (!TryReadBits(LengthExtraBits[symbol], out var extra) || !TryDecodeSymbol(distanceCode, out var distanceSymbol))
            {
                _bitPosition = start;
                return false;
            }

            var length = LengthBase[symbol] + extra;
            if (distanceSymbol >= DistanceBase.Length)
                throw new InvalidDataException("The DEFLATE data has an invalid distance code");

            if (!TryReadBits(DistanceExtraBits[distanceSymbol], out extra))
            {
                _bitPosition = start;
                return false;
            }

            var distance = DistanceBase[distanceSymbol] + extra;
            if (distance > _totalOutput)
                throw new InvalidDataException("The DEFLATE data refers to a distance too far back");

            for (var i = 0; i < length; i++)
            {
                WriteByte(_window[(int)((_totalOutput - distance) & (WindowSize - 1))]);
            }
        }
    }

    private bool ReadTrailer()
    {
        AlignToByte();
        var offset = (int)(_bitPosition >> 3);
        switch (_wrapper)
        {
            case InflaterWrapper.ZLib:
                // https://www.rfc-editor.org/rfc/rfc1950#section-2.2: ADLER32, most significant byte first
                if (AvailableBytes < 4)
                    return false;

                UpdateChecksum();
                if (BinaryPrimitives.ReadUInt32BigEndian(_input.AsSpan(offset)) != _adler32!.GetCurrentHashAsUInt32())
                    throw new InvalidDataException("The ADLER-32 checksum of the zlib data is incorrect");

                _bitPosition += 32;
                break;

            case InflaterWrapper.Gzip:
                // https://www.rfc-editor.org/rfc/rfc1952#section-2.3.1: CRC32 and ISIZE (the size modulo 2^32)
                if (AvailableBytes < 8)
                    return false;

                UpdateChecksum();
                if (BinaryPrimitives.ReadUInt32LittleEndian(_input.AsSpan(offset)) != _crc32!.GetCurrentHashAsUInt32())
                    throw new InvalidDataException("The CRC-32 of the gzip data is incorrect");

                if (BinaryPrimitives.ReadUInt32LittleEndian(_input.AsSpan(offset + 4)) != (uint)_totalOutput)
                    throw new InvalidDataException("The size of the gzip data is incorrect");

                _bitPosition += 64;
                break;
        }

        _state = State.Ended;
        return false;
    }

    private int AvailableBytes => _inputLength - (int)((_bitPosition + 7) >> 3);

    private void AlignToByte() => _bitPosition = (_bitPosition + 7) & ~7L;

    private bool TryReadBits(int count, out int value)
    {
        value = 0;
        if (_bitPosition + count > (long)_inputLength << 3)
            return false;

        for (var i = 0; i < count; i++)
        {
            var position = _bitPosition + i;
            value |= ((_input[(int)(position >> 3)] >> (int)(position & 7)) & 1) << i;
        }

        _bitPosition += count;
        return true;
    }

    /// <summary>Decodes a symbol of a canonical Huffman code, reading its bits one by one (the first bit is the most significant bit of the code).</summary>
    private bool TryDecodeSymbol(Huffman huffman, out int symbol)
    {
        var limit = (long)_inputLength << 3;
        var position = _bitPosition;
        var code = 0;
        var first = 0;
        var index = 0;
        for (var length = 1; length <= MaxCodeLength; length++)
        {
            if (position >= limit)
            {
                symbol = 0;
                return false;
            }

            code |= (_input[(int)(position >> 3)] >> (int)(position & 7)) & 1;
            position++;
            var count = huffman.Counts[length];
            if (code - first < count)
            {
                symbol = huffman.Symbols[index + code - first];
                _bitPosition = position;
                return true;
            }

            index += count;
            first = (first + count) << 1;
            code <<= 1;
        }

        throw new InvalidDataException("The DEFLATE data has an invalid code");
    }

    private void WriteByte(byte value)
    {
        if (_outputLength == _output.Length)
        {
            Array.Resize(ref _output, Math.Max(_output.Length * 2, 4096));
        }

        _output[_outputLength++] = value;
        _window[(int)(_totalOutput & (WindowSize - 1))] = value;
        _totalOutput++;
    }

    private void UpdateChecksum()
    {
        var output = _output.AsSpan(_checksumOffset, _outputLength - _checksumOffset);
        _checksumOffset = _outputLength;
        _adler32?.Append(output);
        _crc32?.Append(output);
    }

    private void AppendInput(ReadOnlySpan<byte> input)
    {
        if (_inputLength + input.Length > _input.Length)
        {
            Array.Resize(ref _input, Math.Max(_inputLength + input.Length, _input.Length * 2));
        }

        input.CopyTo(_input.AsSpan(_inputLength));
        _inputLength += input.Length;
    }

    private void DiscardConsumedInput()
    {
        var consumed = (int)(_bitPosition >> 3);
        if (consumed == 0 || _state == State.Header)
            return;

        _input.AsSpan(consumed, _inputLength - consumed).CopyTo(_input);
        _inputLength -= consumed;
        _bitPosition -= consumed * 8L;
    }

    private static Huffman CreateFixedLiteralLengthCode()
    {
        // https://www.rfc-editor.org/rfc/rfc1951#section-3.2.6
        Span<byte> lengths = stackalloc byte[288];
        lengths[..144].Fill(8);
        lengths[144..256].Fill(9);
        lengths[256..280].Fill(7);
        lengths[280..].Fill(8);
        return new Huffman(lengths);
    }

    private static Huffman CreateFixedDistanceCode()
    {
        Span<byte> lengths = stackalloc byte[30];
        lengths.Fill(5);
        return new Huffman(lengths);
    }

    /// <summary>A canonical Huffman code: the number of codes of each length and the symbols ordered by code.</summary>
    private sealed class Huffman
    {
        public Huffman(ReadOnlySpan<byte> lengths)
        {
            Symbols = new short[lengths.Length];
            foreach (var length in lengths)
            {
                Counts[length]++;
            }

            // The number of unused codes: negative when the code is over-subscribed, positive when it is incomplete
            var left = 1;
            for (var length = 1; length <= MaxCodeLength; length++)
            {
                left = (left << 1) - Counts[length];
                if (Counts[length] != 0)
                {
                    MaxLength = length;
                }
            }

            Left = left;

            Span<short> offsets = stackalloc short[MaxCodeLength + 2];
            for (var length = 1; length <= MaxCodeLength; length++)
            {
                offsets[length + 1] = (short)(offsets[length] + Counts[length]);
            }

            for (var symbol = 0; symbol < lengths.Length; symbol++)
            {
                if (lengths[symbol] != 0)
                {
                    Symbols[offsets[lengths[symbol]]++] = (short)symbol;
                }
            }
        }

        public short[] Counts { get; } = new short[MaxCodeLength + 1];

        public short[] Symbols { get; }

        public int Left { get; }

        public int MaxLength { get; }

        /// <summary>
        /// Creates the code of the lengths of a dynamic block: an over-subscribed code is an error, and so is an incomplete
        /// code, except a single code of one bit and a code without any symbol (whose use is an error), as in zlib.
        /// </summary>
        public static Huffman Create(ReadOnlySpan<byte> lengths, bool isCodeLengthCode)
        {
            var huffman = new Huffman(lengths);
            if (huffman.MaxLength == 0)
                return huffman;

            if (huffman.Left < 0)
                throw new InvalidDataException("The DEFLATE data has an over-subscribed Huffman code");

            if (huffman.Left > 0 && (isCodeLengthCode || huffman.MaxLength != 1))
                throw new InvalidDataException("The DEFLATE data has an incomplete Huffman code");

            return huffman;
        }
    }
}
