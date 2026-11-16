using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.BZip2;

/// <summary>
/// Decodes bzip2 data: the streams written by bzip2 (and the concatenated streams of pbzip2 and lbzip2), with the randomized
/// blocks of bzip2 0.9.0. The CRC of each block and the combined CRC of each stream are checked. The data after the last
/// stream are ignored when they do not start with the header of a stream.
/// </summary>
/// <remarks>
/// The decoder reads the Huffman codes with lookup tables, and inverts the Burrows-Wheeler transform with the vector of
/// the successors of each position, packed with the byte of the position in a single array, as bzip2 does.
/// </remarks>
internal sealed class BZip2DecoderStream : ReadOnlyStreamBase
{
    private const int MaxRunLength = 2 * 1024 * 1024;

    private readonly Stream _input;
    private readonly bool _leaveOpen;
    private readonly bool _multiStream;
    private readonly byte[] _inputBuffer = new byte[64 * 1024];
    private int _inputPosition;
    private int _inputLength;
    private bool _inputEnded;

    // The bits not read yet are the _bitCount lowest bits of _bitBuffer, the next one first. The last _paddingBits of them are
    // zeros added after the end of the input, so that the codes can be looked up: reading them is an error.
    private ulong _bitBuffer;
    private int _bitCount;
    private int _paddingBits;

    private readonly BZip2HuffmanDecoder[] _codes = [new(), new(), new(), new(), new(), new()];
    private readonly byte[] _selectors = new byte[BZip2Constants.MaxSelectors];
    private readonly byte[] _lengths = new byte[BZip2Constants.MaxAlphabetSize];
    private uint[] _tt = [];
    private int _blockSize;

    private State _state;
    private uint _streamCrc;
    private uint _blockCrc;
    private uint _expectedBlockCrc;

    // The output of the current block: the position in the vector, the number of positions left, and the state of the
    // run-length decoding (4 equal bytes are followed by the number of times the byte is repeated)
    private uint _position;
    private int _remaining;
    private int _lastByte;
    private int _runLength;
    private int _repeat;
    private bool _randomized;
    private int _randomCountdown;
    private int _randomIndex;

    public BZip2DecoderStream(Stream input, bool leaveOpen, bool multiStream = true)
    {
        _input = input;
        _leaveOpen = leaveOpen;
        _multiStream = multiStream;
    }

    private enum State
    {
        StreamHeader,
        BlockHeader,
        BlockData,
        Ended,
        Disposed,
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_state is State.Disposed, this);

        var written = 0;
        while (written < buffer.Length)
        {
            switch (_state)
            {
                case State.StreamHeader:
                    if (!ReadStreamHeader(first: true))
                        throw new InvalidDataException("The data do not start with the header of a bzip2 stream.");

                    break;

                case State.BlockHeader:
                    ReadBlock();
                    break;

                case State.BlockData:
                    var output = buffer[written..];
                    var count = _randomized ? WriteRandomized(output) : Write(output);
                    _blockCrc = BZip2Constants.UpdateCrc(_blockCrc, output[..count]);
                    written += count;
                    if (_remaining is 0 && _repeat is 0)
                    {
                        EndBlock();
                    }

                    break;

                default:
                    return written;
            }
        }

        return written;
    }

    private bool ReadStreamHeader(bool first)
    {
        // "BZh" and the size of the blocks, in hundreds of kB. The rest of the last byte of the previous stream is padding.
        _bitCount -= _bitCount & 7;
        if (!first && !HasMoreInput())
        {
            _state = State.Ended;
            return true;
        }

        if (!TryReadByte(out var b) || b is not (byte)'B' || !TryReadByte(out b) || b is not (byte)'Z' || !TryReadByte(out b) || b is not (byte)'h' || !TryReadByte(out b) || b is < (byte)'1' or > (byte)'9')
        {
            if (first)
                return false;

            _state = State.Ended;
            return true;
        }

        _blockSize = (b - '0') * BZip2Constants.BlockSizeUnit;
        _streamCrc = 0;
        _state = State.BlockHeader;
        return true;
    }

    private void ReadBlock()
    {
        var magic = ((ulong)ReadBits(24) << 24) | ReadBits(24);
        if (magic == BZip2Constants.EndOfStreamMagic)
        {
            var crc = ReadBits(32);
            if (crc != _streamCrc)
                throw new InvalidDataException("The combined CRC of the bzip2 stream is incorrect.");

            if (!_multiStream)
            {
                _state = State.Ended;
                return;
            }

            ReadStreamHeader(first: false);
            return;
        }

        if (magic != BZip2Constants.BlockMagic)
            throw new InvalidDataException("The bzip2 data have an invalid block header.");

        _expectedBlockCrc = ReadBits(32);
        _randomized = ReadBits(1) is not 0;
        var origin = (int)ReadBits(24);
        var blockLength = DecodeBlock();
        if (origin >= blockLength)
            throw new InvalidDataException("The origin pointer of the bzip2 block is invalid.");

        _position = _tt[origin] >> 8;
        _remaining = blockLength;
        _lastByte = -1;
        _runLength = 0;
        _repeat = 0;
        _randomCountdown = 0;
        _randomIndex = 0;
        _blockCrc = uint.MaxValue;
        _state = State.BlockData;
    }

    /// <summary>Decodes the Huffman codes, the move-to-front and the runs of zeros of a block, and creates the vector of the inverse transform.</summary>
    /// <returns>The number of bytes of the block.</returns>
    private int DecodeBlock()
    {
        // The bytes used in the block, in groups of 16
        Span<byte> used = stackalloc byte[256];
        var usedCount = 0;
        var groups = ReadBits(16);
        for (var i = 0; i < 16; i++)
        {
            if ((groups & (0x8000u >> i)) is 0)
                continue;

            var bits = ReadBits(16);
            for (var j = 0; j < 16; j++)
            {
                if ((bits & (0x8000u >> j)) is not 0)
                {
                    used[usedCount++] = (byte)((i * 16) + j);
                }
            }
        }

        if (usedCount is 0)
            throw new InvalidDataException("The bzip2 block does not use any byte.");

        var alphabetSize = usedCount + 2;
        var groupCount = (int)ReadBits(3);
        if (groupCount is < BZip2Constants.MinGroups or > BZip2Constants.MaxGroups)
            throw new InvalidDataException("The bzip2 block has an invalid number of Huffman tables.");

        var selectorCount = (int)ReadBits(15);
        if (selectorCount is 0)
            throw new InvalidDataException("The bzip2 block has no selector.");

        // The selectors are move-to-front encoded, in unary. bzip2 1.0.8 ignores the selectors after the maximum.
        Span<byte> tables = stackalloc byte[BZip2Constants.MaxGroups];
        for (var i = 0; i < groupCount; i++)
        {
            tables[i] = (byte)i;
        }

        for (var i = 0; i < selectorCount; i++)
        {
            var index = 0;
            while (ReadBits(1) is not 0)
            {
                index++;
                if (index >= groupCount)
                    throw new InvalidDataException("The bzip2 block has an invalid selector.");
            }

            var table = tables[index];
            for (var j = index; j > 0; j--)
            {
                tables[j] = tables[j - 1];
            }

            tables[0] = table;
            if (i < BZip2Constants.MaxSelectors)
            {
                _selectors[i] = table;
            }
        }

        selectorCount = Math.Min(selectorCount, BZip2Constants.MaxSelectors);

        // The lengths of the codes, as deltas from 5 bits
        for (var t = 0; t < groupCount; t++)
        {
            var length = (int)ReadBits(5);
            for (var i = 0; i < alphabetSize; i++)
            {
                while (true)
                {
                    if (length is < 1 or > BZip2Constants.MaxCodeLength)
                        throw new InvalidDataException("The bzip2 block has an invalid Huffman code length.");

                    if (ReadBits(1) is 0)
                        break;

                    length += ReadBits(1) is 0 ? 1 : -1;
                }

                _lengths[i] = (byte)length;
            }

            _codes[t].Build(_lengths.AsSpan(0, alphabetSize));
        }

        if (_tt.Length < _blockSize)
        {
            _tt = new uint[_blockSize];
        }

        var tt = _tt;
        Span<byte> mtf = stackalloc byte[256];
        used[..usedCount].CopyTo(mtf);
        Span<int> counts = stackalloc int[256];
        counts.Clear();

        var endOfBlock = usedCount + 1;
        var length2 = 0;
        var selectorIndex = 0;
        var groupRemaining = 0;
        var run = 0;
        var runWeight = 1;
        BZip2HuffmanDecoder code = _codes[0];
        ReadOnlySpan<ushort> lookup = code.Lookup;
        while (true)
        {
            if (groupRemaining is 0)
            {
                if (selectorIndex >= selectorCount)
                    throw new InvalidDataException("The bzip2 block has too few selectors.");

                code = _codes[_selectors[selectorIndex++]];
                lookup = code.Lookup;
                groupRemaining = BZip2Constants.GroupSize;
            }

            groupRemaining--;

            // Decode a symbol
            if (_bitCount < BZip2Constants.MaxCodeLength)
            {
                Refill();
            }

            var next = (uint)(_bitBuffer >> (_bitCount - BZip2Constants.MaxCodeLength)) & 0xFFFFF;
            int entry = lookup[(int)(next >> (BZip2Constants.MaxCodeLength - BZip2HuffmanDecoder.LookupBits))];
            if (entry is 0)
            {
                entry = code.DecodeLong(next);
            }

            _bitCount -= entry & 31;
            if (_bitCount < _paddingBits)
                throw Truncated();

            var symbol = entry >> 5;
            if (symbol <= BZip2Constants.RunB)
            {
                // A run of the first byte of the list, its length written in bijective base 2
                if (run is 0)
                {
                    runWeight = 1;
                }

                run += (symbol + 1) * runWeight;
                runWeight <<= 1;
                if (runWeight >= MaxRunLength)
                    throw new InvalidDataException("The bzip2 block has a run that is too long.");

                continue;
            }

            if (run > 0)
            {
                if (run > _blockSize - length2)
                    throw new InvalidDataException("The bzip2 block is larger than the block size of the stream.");

                var value = mtf[0];
                tt.AsSpan(length2, run).Fill(value);
                counts[value] += run;
                length2 += run;
                run = 0;
            }

            if (symbol == endOfBlock)
                break;

            if (symbol > endOfBlock)
                throw new InvalidDataException("The bzip2 block has an invalid symbol.");

            if (length2 >= _blockSize)
                throw new InvalidDataException("The bzip2 block is larger than the block size of the stream.");

            // Move to front
            var position = symbol - 1;
            var b = mtf[position];
            if (position < 16)
            {
                for (var j = position; j > 0; j--)
                {
                    mtf[j] = mtf[j - 1];
                }
            }
            else
            {
                mtf[..position].CopyTo(mtf[1..]);
            }

            mtf[0] = b;
            tt[length2++] = b;
            counts[b]++;
        }

        // The vector of the inverse transform: the bytes sorted by a stable counting sort give the first column
        Span<int> starts = stackalloc int[256];
        var sum = 0;
        for (var i = 0; i < 256; i++)
        {
            starts[i] = sum;
            sum += counts[i];
        }

        for (var i = 0; i < length2; i++)
        {
            var b = (byte)tt[i];
            tt[starts[b]++] |= (uint)i << 8;
        }

        return length2;
    }

    private int Write(Span<byte> output)
    {
        var tt = _tt;
        var position = _position;
        var remaining = _remaining;
        var last = _lastByte;
        var run = _runLength;
        var repeat = _repeat;
        var written = 0;
        while (written < output.Length)
        {
            if (repeat > 0)
            {
                var count = Math.Min(repeat, output.Length - written);
                output.Slice(written, count).Fill((byte)last);
                written += count;
                repeat -= count;
                continue;
            }

            if (remaining is 0)
                break;

            var entry = tt[position];
            position = entry >> 8;
            var value = (int)(entry & 0xFF);
            remaining--;
            if (run is 4)
            {
                repeat = value;
                run = 0;
                continue;
            }

            if (value == last)
            {
                run++;
            }
            else
            {
                last = value;
                run = 1;
            }

            output[written++] = (byte)value;
        }

        _position = position;
        _remaining = remaining;
        _lastByte = last;
        _runLength = run;
        _repeat = repeat;
        return written;
    }

    private int WriteRandomized(Span<byte> output)
    {
        var tt = _tt;
        var written = 0;
        while (written < output.Length)
        {
            if (_repeat > 0)
            {
                var count = Math.Min(_repeat, output.Length - written);
                output.Slice(written, count).Fill((byte)_lastByte);
                written += count;
                _repeat -= count;
                continue;
            }

            if (_remaining is 0)
                break;

            var entry = tt[_position];
            _position = entry >> 8;
            var value = (int)(entry & 0xFF);
            _remaining--;

            // The lowest bit of a byte is flipped at the distances of the table
            if (_randomCountdown is 0)
            {
                _randomCountdown = BZip2Constants.RandomNumbers[_randomIndex];
                _randomIndex = (_randomIndex + 1) & 511;
            }

            _randomCountdown--;
            if (_randomCountdown is 1)
            {
                value ^= 1;
            }

            if (_runLength is 4)
            {
                _repeat = value;
                _runLength = 0;
                continue;
            }

            if (value == _lastByte)
            {
                _runLength++;
            }
            else
            {
                _lastByte = value;
                _runLength = 1;
            }

            output[written++] = (byte)value;
        }

        return written;
    }

    private void EndBlock()
    {
        var crc = ~_blockCrc;
        if (crc != _expectedBlockCrc)
            throw new InvalidDataException("The CRC of the bzip2 block is incorrect.");

        _streamCrc = ((_streamCrc << 1) | (_streamCrc >> 31)) ^ crc;
        _state = State.BlockHeader;
    }

    private uint ReadBits(int count)
    {
        if (_bitCount < count)
        {
            Refill();
        }

        _bitCount -= count;
        if (_bitCount < _paddingBits)
            throw Truncated();

        return (uint)((_bitBuffer >> _bitCount) & ((1UL << count) - 1));
    }

    private bool TryReadByte(out byte value)
    {
        if (_bitCount < 8)
        {
            Refill();
        }

        if (_bitCount - 8 < _paddingBits)
        {
            value = 0;
            return false;
        }

        _bitCount -= 8;
        value = (byte)(_bitBuffer >> _bitCount);
        return true;
    }

    private bool HasMoreInput()
    {
        if (_bitCount > _paddingBits)
            return true;

        return _inputPosition < _inputLength || FillInput();
    }

    private void Refill()
    {
        while (_bitCount <= 56)
        {
            if (_inputPosition == _inputLength && !FillInput())
            {
                _bitBuffer <<= 8;
                _bitCount += 8;
                _paddingBits += 8;
                continue;
            }

            _bitBuffer = (_bitBuffer << 8) | _inputBuffer[_inputPosition++];
            _bitCount += 8;
        }
    }

    private bool FillInput()
    {
        if (_inputEnded || _paddingBits > 0)
            return false;

        _inputPosition = 0;
        _inputLength = _input.Read(_inputBuffer);
        if (_inputLength is 0)
        {
            _inputEnded = true;
            return false;
        }

        return true;
    }

    private static InvalidDataException Truncated() => new("The bzip2 data are truncated.");

    protected override void Dispose(bool disposing)
    {
        if (disposing && _state is not State.Disposed && !_leaveOpen)
        {
            _input.Dispose();
        }

        _state = State.Disposed;
        base.Dispose(disposing);
    }
}
