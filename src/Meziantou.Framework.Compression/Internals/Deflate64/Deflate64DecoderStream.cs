using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Deflate64;

/// <summary>
/// Decodes raw Deflate data (RFC 1951) or Deflate64 data (the "enhanced deflating" method 9 of zip): a window of 64 KiB,
/// the length code 285 that is followed by 16 extra bits (lengths 3 to 65538), and the distance codes 30 and 31.
/// </summary>
/// <remarks>
/// The decoder stops at the end of the final block. It reads its input in chunks: <see cref="GetUnconsumedInput"/> returns
/// the bytes it read after the end of the data, from the next byte boundary, so that a reader can find what follows them
/// (such as the data descriptor of a zip entry) in a stream that cannot seek.
/// </remarks>
internal sealed class Deflate64DecoderStream : ReadOnlyStreamBase
{
    // https://www.rfc-editor.org/rfc/rfc1951#section-3.2.5
    private static readonly ushort[] LengthBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
    private static readonly byte[] LengthExtraBits = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    private static readonly int[] DistanceBase = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577, 32769, 49153];
    private static readonly byte[] DistanceExtraBits = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14];
    private static readonly byte[] CodeLengthOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

    private readonly Stream _input;
    private readonly bool _leaveOpen;
    private readonly bool _deflate64;
    private readonly LsbBitReader _reader;
    private readonly int _windowSize;
    private readonly int _distanceCodeCount;

    // The output: the last window of the output and the output not read yet, in a ring of twice the window
    private readonly byte[] _ring;
    private readonly int _ringMask;
    private long _written;
    private long _delivered;

    private readonly LsbHuffmanDecoder _literalLengthCode = new(288);
    private readonly LsbHuffmanDecoder _distanceCode = new(32);
    private readonly LsbHuffmanDecoder _codeLengthCode = new(19);
    private readonly byte[] _lengths = new byte[288 + 32];

    private State _state;
    private bool _finalBlock;
    private int _storedRemaining;
    private int _matchLength;
    private int _matchDistance;
    private byte[]? _unconsumedInput;
    private bool _disposed;

    public Deflate64DecoderStream(Stream input, bool deflate64, bool leaveOpen)
    {
        _input = input;
        _deflate64 = deflate64;
        _leaveOpen = leaveOpen;
        _reader = new LsbBitReader(input);
        _windowSize = deflate64 ? 65536 : 32768;
        _distanceCodeCount = deflate64 ? 32 : 30;
        _ring = new byte[2 * _windowSize];
        _ringMask = _ring.Length - 1;
    }

    private enum State
    {
        BlockHeader,
        StoredBlock,
        HuffmanBlock,
        Completed,
    }

    /// <summary>Gets a value indicating whether the final block is decoded.</summary>
    public bool IsCompleted => _state is State.Completed;

    /// <summary>
    /// Gets the bytes read from the input after the end of the final block (from the next byte boundary), once
    /// <see cref="Read(Span{byte})"/> has returned 0.
    /// </summary>
    public ReadOnlyMemory<byte> GetUnconsumedInput()
    {
        if (_state is not State.Completed)
            throw new InvalidOperationException("The deflate data are not decoded to their end.");

        return _unconsumedInput ??= _reader.GetUnconsumedBytes();
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var read = 0;
        while (read < buffer.Length)
        {
            var pending = (int)(_written - _delivered);
            if (pending > 0)
            {
                var start = (int)(_delivered & _ringMask);
                var count = Math.Min(Math.Min(pending, buffer.Length - read), _ring.Length - start);
                _ring.AsSpan(start, count).CopyTo(buffer[read..]);
                _delivered += count;
                read += count;
                continue;
            }

            if (_state is State.Completed)
                break;

            Decode();
        }

        return read;
    }

    /// <summary>Decodes until a window of output is pending, or the end of the data.</summary>
    private void Decode()
    {
        while (_written - _delivered < _windowSize)
        {
            switch (_state)
            {
                case State.BlockHeader:
                    ReadBlockHeader();
                    break;

                case State.StoredBlock:
                    CopyStoredBlock();
                    break;

                case State.HuffmanBlock:
                    DecodeHuffmanBlock();
                    break;

                default:
                    return;
            }
        }
    }

    private void ReadBlockHeader()
    {
        _finalBlock = _reader.Read(1) is not 0;
        switch (_reader.Read(2))
        {
            case 0:
                _reader.AlignToByte();
                var length = (int)_reader.Read(16);
                var complement = (int)_reader.Read(16);
                if (length != (~complement & 0xFFFF))
                    throw new InvalidDataException("The length of a stored block does not match its complement.");

                _storedRemaining = length;
                _state = State.StoredBlock;
                break;

            case 1:
                Span<byte> lengths = _lengths;
                lengths[..144].Fill(8);
                lengths[144..256].Fill(9);
                lengths[256..280].Fill(7);
                lengths[280..288].Fill(8);
                _literalLengthCode.Build(lengths[..288]);
                lengths[..32].Fill(5);
                _distanceCode.Build(lengths[..32]);
                _state = State.HuffmanBlock;
                break;

            case 2:
                ReadDynamicCodes();
                _state = State.HuffmanBlock;
                break;

            default:
                throw new InvalidDataException("The deflate data have an invalid block type.");
        }
    }

    // https://www.rfc-editor.org/rfc/rfc1951#section-3.2.7
    private void ReadDynamicCodes()
    {
        var literalLengthCount = (int)_reader.Read(5) + 257;
        var distanceCount = (int)_reader.Read(5) + 1;
        var codeLengthCount = (int)_reader.Read(4) + 4;
        if (literalLengthCount > 286 || distanceCount > _distanceCodeCount)
            throw new InvalidDataException("The deflate data have too many length or distance codes.");

        Span<byte> lengths = _lengths;
        lengths[..19].Clear();
        for (var i = 0; i < codeLengthCount; i++)
        {
            lengths[CodeLengthOrder[i]] = (byte)_reader.Read(3);
        }

        if (!_codeLengthCode.Build(lengths[..19]))
            throw new InvalidDataException("The deflate data have an empty code length code.");

        var count = literalLengthCount + distanceCount;
        var index = 0;
        while (index < count)
        {
            var symbol = _codeLengthCode.Decode(_reader);
            if (symbol < 16)
            {
                lengths[index++] = (byte)symbol;
                continue;
            }

            byte repeated = 0;
            int repeat;
            switch (symbol)
            {
                case 16:
                    if (index is 0)
                        throw new InvalidDataException("The deflate data repeat a code length with no previous length.");

                    repeated = lengths[index - 1];
                    repeat = 3 + (int)_reader.Read(2);
                    break;

                case 17:
                    repeat = 3 + (int)_reader.Read(3);
                    break;

                default:
                    repeat = 11 + (int)_reader.Read(7);
                    break;
            }

            if (index + repeat > count)
                throw new InvalidDataException("The deflate data have too many code lengths.");

            lengths.Slice(index, repeat).Fill(repeated);
            index += repeat;
        }

        if (lengths[256] is 0)
            throw new InvalidDataException("The deflate data have no end-of-block code.");

        _literalLengthCode.Build(lengths[..literalLengthCount]);
        _distanceCode.Build(lengths.Slice(literalLengthCount, distanceCount));
    }

    private void CopyStoredBlock()
    {
        while (_storedRemaining > 0)
        {
            var space = _windowSize - (int)(_written - _delivered);
            if (space <= 0)
                return;

            var position = (int)(_written & _ringMask);
            var count = Math.Min(Math.Min(_storedRemaining, space), _ring.Length - position);
            var read = _reader.ReadBytes(_ring.AsSpan(position, count));
            if (read is 0)
                throw new InvalidDataException("The compressed data are truncated.");

            _written += read;
            _storedRemaining -= read;
        }

        EndBlock();
    }

    private void DecodeHuffmanBlock()
    {
        var ring = _ring;
        var mask = _ringMask;
        var reader = _reader;
        var literalLengthCode = _literalLengthCode;
        var distanceCode = _distanceCode;
        var written = _written;
        var limit = _delivered + _windowSize;
        try
        {
            while (true)
            {
                if (_matchLength > 0)
                {
                    var count = (int)Math.Min(_matchLength, limit - written);
                    if (count <= 0)
                        return;

                    CopyMatch(ring, mask, written, _matchDistance, count);
                    written += count;
                    _matchLength -= count;
                    continue;
                }

                if (written >= limit)
                    return;

                var symbol = literalLengthCode.Decode(reader);
                if (symbol < 256)
                {
                    ring[(int)written & mask] = (byte)symbol;
                    written++;
                    continue;
                }

                if (symbol is 256)
                {
                    _written = written;
                    EndBlock();
                    return;
                }

                symbol -= 257;
                if (symbol >= 29)
                    throw new InvalidDataException("The deflate data contain an invalid length code.");

                int length;
                if (symbol is 28 && _deflate64)
                {
                    length = 3 + (int)reader.Read(16);
                }
                else
                {
                    length = LengthBase[symbol] + (int)reader.Read(LengthExtraBits[symbol]);
                }

                var distanceSymbol = distanceCode.Decode(reader);
                if (distanceSymbol >= _distanceCodeCount)
                    throw new InvalidDataException("The deflate data contain an invalid distance code.");

                var distance = DistanceBase[distanceSymbol] + (int)reader.Read(DistanceExtraBits[distanceSymbol]);
                if (distance > written)
                    throw new InvalidDataException("The deflate data refer to a distance before the start of the data.");

                _matchLength = length;
                _matchDistance = distance;
            }
        }
        finally
        {
            _written = written;
        }
    }

    private static void CopyMatch(byte[] ring, int mask, long written, int distance, int count)
    {
        var destination = (int)written & mask;
        var source = (int)(written - distance) & mask;
        if (distance >= count && destination + count <= ring.Length && source + count <= ring.Length)
        {
            ring.AsSpan(source, count).CopyTo(ring.AsSpan(destination));
            return;
        }

        for (var i = 0; i < count; i++)
        {
            ring[(destination + i) & mask] = ring[(source + i) & mask];
        }
    }

    private void EndBlock()
    {
        if (_finalBlock)
        {
            _state = State.Completed;
            _reader.AlignToByte();
        }
        else
        {
            _state = State.BlockHeader;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed && !_leaveOpen)
        {
            _input.Dispose();
        }

        _disposed = true;
        base.Dispose(disposing);
    }
}
