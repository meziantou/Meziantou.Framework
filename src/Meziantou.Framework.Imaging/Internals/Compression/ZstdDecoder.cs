using System.Buffers.Binary;
using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A decoder of Zstandard frames (RFC 8878) into a buffer that holds the whole output, so that a match reads the bytes
/// decoded before it in place. Dictionaries are not supported. An instance keeps its decoding tables between calls and is
/// not thread-safe.
/// </summary>
/// <remarks>The sections cited are those of RFC 8878.</remarks>
internal sealed class ZstdDecoder
{
    private const uint FrameMagic = 0xFD2FB528;
    private const int MaxBlockSize = 128 * 1024;
    private const int MaxLiteralsLengthSymbol = 35;
    private const int MaxMatchLengthSymbol = 52;
    private const int MaxOffsetSymbol = 31;
    private const int MaxHuffmanBits = 11;

    // Section 3.1.1.3.2.1.1: the baselines and the extra bits of the literals length codes and the match length codes
    private static ReadOnlySpan<int> LiteralsLengthBaselines => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 28, 32, 40, 48, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536];

    private static ReadOnlySpan<byte> LiteralsLengthBits => [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

    private static ReadOnlySpan<int> MatchLengthBaselines => [3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 37, 39, 41, 43, 47, 51, 59, 67, 83, 99, 131, 259, 515, 1027, 2051, 4099, 8195, 16387, 32771, 65539];

    private static ReadOnlySpan<byte> MatchLengthBits => [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3, 3, 4, 4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

    // Section 3.1.1.3.2.2: the default distributions
    private static ReadOnlySpan<short> DefaultLiteralsLengthDistribution => [4, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 1, 1, 1, 1, 1, -1, -1, -1, -1];

    private static ReadOnlySpan<short> DefaultMatchLengthDistribution => [1, 4, 3, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, -1, -1, -1];

    private static ReadOnlySpan<short> DefaultOffsetDistribution => [1, 1, 1, 1, 1, 1, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, -1];

    private readonly FseTable _literalsLengths = new(9);
    private readonly FseTable _offsets = new(8);
    private readonly FseTable _matchLengths = new(9);
    private readonly FseTable _weights = new(6);
    private readonly short[] _distribution = new short[256];
    private readonly byte[] _huffmanSymbols = new byte[1 << MaxHuffmanBits];
    private readonly byte[] _huffmanLengths = new byte[1 << MaxHuffmanBits];
    private readonly byte[] _huffmanWeights = new byte[256];
    private readonly byte[] _literals = new byte[MaxBlockSize + 8];
    private readonly int[] _sequences = new int[3 * ((MaxBlockSize / 3) + 1)];
    private int _huffmanMaxBits = -1;

    /// <summary>
    /// Decodes the frames of <paramref name="source"/> (skippable frames are skipped) into <paramref name="destination"/>,
    /// which they must fill exactly.
    /// </summary>
    /// <param name="source">One or more frames, and nothing else.</param>
    /// <param name="destination">Receives the decoded bytes.</param>
    /// <exception cref="InvalidDataException">The data are not valid Zstandard frames, use a dictionary, or do not produce exactly the length of <paramref name="destination"/>.</exception>
    public void Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var position = 0;
        var written = 0;
        var frames = 0;
        while (position < source.Length)
        {
            if (source.Length - position < 4)
                throw Corrupt("the data end inside a frame magic number.");

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(source[position..]);
            if ((magic & 0xFFFFFFF0) == 0x184D2A50)
            {
                // Section 3.1.2: a skippable frame
                if (source.Length - position < 8)
                    throw Corrupt("a skippable frame is truncated.");

                var size = BinaryPrimitives.ReadUInt32LittleEndian(source[(position + 4)..]);
                if (size > (uint)(source.Length - position - 8))
                    throw Corrupt("a skippable frame is truncated.");

                position += 8 + (int)size;
                continue;
            }

            if (magic != FrameMagic)
                throw Corrupt("a frame does not start with the Zstandard magic number.");

            position += 4;
            position += DecodeFrame(source[position..], destination, ref written);
            frames++;
        }

        if (frames == 0)
            throw Corrupt("the data have no frame.");

        if (written != destination.Length)
            throw Corrupt(string.Create(CultureInfo.InvariantCulture, $"the frames produce {written} bytes; {destination.Length} are expected."));
    }

    private static InvalidDataException Corrupt(string message) => new("The Zstandard data are invalid: " + message);

    /// <summary>Decodes one frame (section 3.1.1) after its magic number.</summary>
    /// <returns>The number of bytes of the frame.</returns>
    private int DecodeFrame(ReadOnlySpan<byte> frame, Span<byte> output, ref int written)
    {
        if (frame.IsEmpty)
            throw Corrupt("a frame header is truncated.");

        // Section 3.1.1.1.1: Frame_Header_Descriptor
        var descriptor = frame[0];
        var contentSizeFlag = descriptor >> 6;
        var singleSegment = (descriptor & 0x20) != 0;
        if ((descriptor & 0x08) != 0)
            throw Corrupt("the reserved bit of a frame header is set.");

        var hasChecksum = (descriptor & 0x04) != 0;
        var dictionaryIdSize = (descriptor & 3) switch { 0 => 0, 1 => 1, 2 => 2, _ => 4 };
        var contentSizeSize = contentSizeFlag switch { 0 => singleSegment ? 1 : 0, 1 => 2, 2 => 4, _ => 8 };
        var headerLength = 1 + (singleSegment ? 0 : 1) + dictionaryIdSize + contentSizeSize;
        if (frame.Length < headerLength)
            throw Corrupt("a frame header is truncated.");

        var position = 1;
        ulong windowSize = 0;
        if (!singleSegment)
        {
            // Section 3.1.1.1.2: Window_Descriptor
            var exponent = frame[position] >> 3;
            var mantissa = frame[position] & 7;
            var windowBase = 1UL << (10 + exponent);
            windowSize = windowBase + ((windowBase / 8) * (ulong)mantissa);
            position++;
        }

        ulong dictionaryId = 0;
        for (var i = 0; i < dictionaryIdSize; i++)
        {
            dictionaryId |= (ulong)frame[position + i] << (8 * i);
        }

        position += dictionaryIdSize;
        if (dictionaryId != 0)
            throw Corrupt("a frame needs a dictionary.");

        ulong? contentSize = null;
        if (contentSizeSize > 0)
        {
            ulong value = 0;
            for (var i = 0; i < contentSizeSize; i++)
            {
                value |= (ulong)frame[position + i] << (8 * i);
            }

            contentSize = contentSizeSize == 2 ? value + 256 : value;
            position += contentSizeSize;
        }

        if (singleSegment)
        {
            windowSize = contentSize!.Value;
        }

        var frameStart = written;
        var blockMaximumSize = (int)Math.Min(windowSize, MaxBlockSize);
        var window = (long)Math.Min(windowSize, int.MaxValue);

        // The state of the frame: the Huffman table and the FSE tables are repeated from earlier blocks of the frame only
        _huffmanMaxBits = -1;
        _literalsLengths.IsDefined = false;
        _offsets.IsDefined = false;
        _matchLengths.IsDefined = false;
        Span<int> repeatedOffsets = [1, 4, 8];
        while (true)
        {
            // Section 3.1.1.2: Block_Header
            if (frame.Length - position < 3)
                throw Corrupt("a block header is truncated.");

            var blockHeader = frame[position] | (frame[position + 1] << 8) | (frame[position + 2] << 16);
            position += 3;
            var isLast = (blockHeader & 1) != 0;
            var blockType = (blockHeader >> 1) & 3;
            var blockSize = blockHeader >> 3;
            if (blockSize > blockMaximumSize)
                throw Corrupt("a block is larger than the maximum block size of its frame.");

            switch (blockType)
            {
                case 0:
                    if (frame.Length - position < blockSize)
                        throw Corrupt("a raw block is truncated.");

                    if (output.Length - written < blockSize)
                        throw Corrupt("the frames produce more bytes than expected.");

                    frame.Slice(position, blockSize).CopyTo(output[written..]);
                    written += blockSize;
                    position += blockSize;
                    break;

                case 1:
                    if (frame.Length - position < 1)
                        throw Corrupt("an RLE block is truncated.");

                    if (output.Length - written < blockSize)
                        throw Corrupt("the frames produce more bytes than expected.");

                    output.Slice(written, blockSize).Fill(frame[position]);
                    written += blockSize;
                    position++;
                    break;

                case 2:
                    if (frame.Length - position < blockSize)
                        throw Corrupt("a compressed block is truncated.");

                    var start = written;
                    DecodeCompressedBlock(frame.Slice(position, blockSize), output, ref written, frameStart, window, repeatedOffsets);
                    if (written - start > blockMaximumSize)
                        throw Corrupt("a block produces more bytes than the maximum block size of its frame.");

                    position += blockSize;
                    break;

                default:
                    throw Corrupt("a block has the reserved type.");
            }

            if (isLast)
                break;
        }

        if (contentSize is { } expected && (ulong)(written - frameStart) != expected)
            throw Corrupt("a frame does not produce the content size of its header.");

        if (hasChecksum)
        {
            if (frame.Length - position < 4)
                throw Corrupt("the checksum of a frame is truncated.");

            var checksum = BinaryPrimitives.ReadUInt32LittleEndian(frame[position..]);
            if ((uint)XxHash64.Compute(output[frameStart..written]) != checksum)
                throw Corrupt("the content of a frame does not match its checksum.");

            position += 4;
        }

        return position;
    }

    /// <summary>Decodes a compressed block (section 3.1.1.3).</summary>
    private void DecodeCompressedBlock(ReadOnlySpan<byte> block, Span<byte> output, ref int written, int frameStart, long window, Span<int> repeatedOffsets)
    {
        var literalsSectionLength = DecodeLiterals(block, out var literalCount);
        var literals = _literals.AsSpan(0, literalCount);
        var sequences = block[literalsSectionLength..];
        var sequenceCount = DecodeSequences(sequences);

        // Section 3.1.1.4: Sequence Execution
        var literalPosition = 0;
        var values = _sequences.AsSpan(0, 3 * sequenceCount);
        for (var i = 0; i < values.Length; i += 3)
        {
            var literalLength = values[i];
            var offsetValue = values[i + 1];
            var matchLength = values[i + 2];
            if (literalLength > literals.Length - literalPosition)
                throw Corrupt("a sequence copies more literals than the block has.");

            if (literalLength > output.Length - written)
                throw Corrupt("the frames produce more bytes than expected.");

            literals.Slice(literalPosition, literalLength).CopyTo(output[written..]);
            literalPosition += literalLength;
            written += literalLength;

            // Section 3.1.1.5: Repeat Offsets
            int offset;
            if (offsetValue > 3)
            {
                offset = offsetValue - 3;
                repeatedOffsets[2] = repeatedOffsets[1];
                repeatedOffsets[1] = repeatedOffsets[0];
                repeatedOffsets[0] = offset;
            }
            else
            {
                var index = offsetValue - 1 + (literalLength == 0 ? 1 : 0);
                switch (index)
                {
                    case 0:
                        offset = repeatedOffsets[0];
                        break;
                    case 1:
                        offset = repeatedOffsets[1];
                        repeatedOffsets[1] = repeatedOffsets[0];
                        repeatedOffsets[0] = offset;
                        break;
                    case 2:
                        offset = repeatedOffsets[2];
                        repeatedOffsets[2] = repeatedOffsets[1];
                        repeatedOffsets[1] = repeatedOffsets[0];
                        repeatedOffsets[0] = offset;
                        break;
                    default:
                        offset = repeatedOffsets[0] - 1;
                        repeatedOffsets[2] = repeatedOffsets[1];
                        repeatedOffsets[1] = repeatedOffsets[0];
                        repeatedOffsets[0] = offset;
                        break;
                }
            }

            if (offset <= 0 || offset > written - frameStart || offset > window)
                throw Corrupt("a match copies bytes from before the start of the frame or the window.");

            if (matchLength > output.Length - written)
                throw Corrupt("the frames produce more bytes than expected.");

            var source = written - offset;
            if (offset >= matchLength)
            {
                output.Slice(source, matchLength).CopyTo(output[written..]);
            }
            else
            {
                // The match overlaps the bytes it produces
                for (var j = 0; j < matchLength; j++)
                {
                    output[written + j] = output[source + j];
                }
            }

            written += matchLength;
        }

        var rest = literals.Length - literalPosition;
        if (rest > output.Length - written)
            throw Corrupt("the frames produce more bytes than expected.");

        literals[literalPosition..].CopyTo(output[written..]);
        written += rest;
    }

    /// <summary>Decodes the literals section (section 3.1.1.3.1) into <see cref="_literals"/>.</summary>
    /// <returns>The length of the section.</returns>
    private int DecodeLiterals(ReadOnlySpan<byte> block, out int literalCount)
    {
        if (block.IsEmpty)
            throw Corrupt("a literals section is truncated.");

        var type = block[0] & 3;
        var sizeFormat = (block[0] >> 2) & 3;
        if (type is 0 or 1)
        {
            int headerLength;
            switch (sizeFormat)
            {
                case 0 or 2:
                    headerLength = 1;
                    literalCount = block[0] >> 3;
                    break;
                case 1:
                    if (block.Length < 2)
                        throw Corrupt("a literals section header is truncated.");

                    headerLength = 2;
                    literalCount = (block[0] >> 4) + (block[1] << 4);
                    break;
                default:
                    if (block.Length < 3)
                        throw Corrupt("a literals section header is truncated.");

                    headerLength = 3;
                    literalCount = (block[0] >> 4) + (block[1] << 4) + (block[2] << 12);
                    break;
            }

            if (literalCount > MaxBlockSize)
                throw Corrupt("a block has more literals than the maximum block size.");

            if (type == 0)
            {
                if (block.Length - headerLength < literalCount)
                    throw Corrupt("raw literals are truncated.");

                block.Slice(headerLength, literalCount).CopyTo(_literals);
                return headerLength + literalCount;
            }

            if (block.Length - headerLength < 1)
                throw Corrupt("RLE literals are truncated.");

            _literals.AsSpan(0, literalCount).Fill(block[headerLength]);
            return headerLength + 1;
        }

        // Compressed_Literals_Block and Treeless_Literals_Block
        int length;
        int compressedSize;
        var streamCount = sizeFormat == 0 ? 1 : 4;
        switch (sizeFormat)
        {
            case 0 or 1:
            {
                if (block.Length < 3)
                    throw Corrupt("a literals section header is truncated.");

                var header = block[0] | (block[1] << 8) | (block[2] << 16);
                literalCount = (header >> 4) & 0x3FF;
                compressedSize = (header >> 14) & 0x3FF;
                length = 3;
                break;
            }

            case 2:
            {
                if (block.Length < 4)
                    throw Corrupt("a literals section header is truncated.");

                var header = BinaryPrimitives.ReadUInt32LittleEndian(block);
                literalCount = (int)((header >> 4) & 0x3FFF);
                compressedSize = (int)((header >> 18) & 0x3FFF);
                length = 4;
                break;
            }

            default:
            {
                if (block.Length < 5)
                    throw Corrupt("a literals section header is truncated.");

                var header = BinaryPrimitives.ReadUInt32LittleEndian(block) | ((ulong)block[4] << 32);
                literalCount = (int)((header >> 4) & 0x3FFFF);
                compressedSize = (int)((header >> 22) & 0x3FFFF);
                length = 5;
                break;
            }
        }

        if (literalCount > MaxBlockSize)
            throw Corrupt("a block has more literals than the maximum block size.");

        if (block.Length - length < compressedSize)
            throw Corrupt("compressed literals are truncated.");

        var content = block.Slice(length, compressedSize);
        if (type == 2)
        {
            content = content[ReadHuffmanTable(content)..];
        }
        else if (_huffmanMaxBits < 0)
        {
            throw Corrupt("treeless literals have no Huffman table to repeat.");
        }

        var literals = _literals.AsSpan(0, literalCount);
        if (streamCount == 1)
        {
            DecodeHuffmanStream(content, literals);
        }
        else
        {
            // Section 3.1.1.3.1.6: Jump_Table
            if (content.Length < 6)
                throw Corrupt("the jump table of the literals is truncated.");

            var size1 = BinaryPrimitives.ReadUInt16LittleEndian(content);
            var size2 = BinaryPrimitives.ReadUInt16LittleEndian(content[2..]);
            var size3 = BinaryPrimitives.ReadUInt16LittleEndian(content[4..]);
            var streams = content[6..];
            if (size1 + size2 + size3 > streams.Length)
                throw Corrupt("the jump table of the literals gives streams larger than the literals.");

            var segment = (literalCount + 3) / 4;
            if (3 * segment > literalCount)
                throw Corrupt("the literals are too few for four streams.");

            DecodeHuffmanStream(streams[..size1], literals[..segment]);
            DecodeHuffmanStream(streams.Slice(size1, size2), literals.Slice(segment, segment));
            DecodeHuffmanStream(streams.Slice(size1 + size2, size3), literals.Slice(2 * segment, segment));
            DecodeHuffmanStream(streams[(size1 + size2 + size3)..], literals[(3 * segment)..]);
        }

        return length + compressedSize;
    }

    /// <summary>Reads a Huffman tree description (section 4.2.1) and builds the decoding table.</summary>
    /// <returns>The length of the description.</returns>
    private int ReadHuffmanTable(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            throw Corrupt("a Huffman tree description is truncated.");

        var weights = _huffmanWeights.AsSpan();
        var headerByte = content[0];
        int weightCount;
        int length;
        if (headerByte >= 128)
        {
            // Direct representation: 4 bits per weight
            weightCount = headerByte - 127;
            length = 1 + ((weightCount + 1) / 2);
            if (content.Length < length)
                throw Corrupt("a Huffman tree description is truncated.");

            for (var i = 0; i < weightCount; i++)
            {
                var value = content[1 + (i / 2)];
                weights[i] = (byte)((i & 1) == 0 ? value >> 4 : value & 0xF);
            }
        }
        else
        {
            // Section 4.2.1.2: FSE compression of the weights, two interleaved states
            length = 1 + headerByte;
            if (content.Length < length || headerByte == 0)
                throw Corrupt("a Huffman tree description is truncated.");

            var data = content.Slice(1, headerByte);
            var tableLength = ReadFseTable(data, _weights, maxSymbol: 255, maxAccuracyLog: 6);
            var reader = new BackwardBitReader(data[tableLength..]);
            var state1 = (int)reader.ReadBits(_weights.AccuracyLog);
            var state2 = (int)reader.ReadBits(_weights.AccuracyLog);
            weightCount = 0;
            while (true)
            {
                if (weightCount > 253)
                    throw Corrupt("a Huffman tree description has too many weights.");

                weights[weightCount++] = _weights.Symbols[state1];
                state1 = _weights.Baselines[state1] + (int)reader.ReadBits(_weights.Bits[state1]);
                if (reader.IsOverflowed)
                {
                    weights[weightCount++] = _weights.Symbols[state2];
                    break;
                }

                weights[weightCount++] = _weights.Symbols[state2];
                state2 = _weights.Baselines[state2] + (int)reader.ReadBits(_weights.Bits[state2]);
                if (reader.IsOverflowed)
                {
                    weights[weightCount++] = _weights.Symbols[state1];
                    break;
                }
            }
        }

        if (weightCount > 255)
            throw Corrupt("a Huffman tree description has too many weights.");

        // The weight of the last symbol completes the sum of 2^(weight - 1) to a power of two
        var total = 0;
        for (var i = 0; i < weightCount; i++)
        {
            if (weights[i] > MaxHuffmanBits)
                throw Corrupt("a Huffman weight is larger than the maximum code length.");

            if (weights[i] > 0)
            {
                total += 1 << (weights[i] - 1);
            }
        }

        if (total == 0)
            throw Corrupt("a Huffman tree has no symbol.");

        var maxBits = BitOperations.Log2((uint)total) + 1;
        if (maxBits > MaxHuffmanBits)
            throw Corrupt("a Huffman tree is deeper than the maximum code length.");

        var rest = (1 << maxBits) - total;
        if (!BitOperations.IsPow2(rest))
            throw Corrupt("the Huffman weights do not complete to a power of two.");

        weights[weightCount] = (byte)(BitOperations.Log2((uint)rest) + 1);
        var symbolCount = weightCount + 1;

        // Section 4.2.1.3: from the lowest weight, each symbol in its order takes 2^(weight - 1) entries of the table
        var position = 0;
        for (var weight = 1; weight <= maxBits; weight++)
        {
            var entries = 1 << (weight - 1);
            var bits = (byte)(maxBits + 1 - weight);
            for (var symbol = 0; symbol < symbolCount; symbol++)
            {
                if (weights[symbol] != weight)
                    continue;

                _huffmanSymbols.AsSpan(position, entries).Fill((byte)symbol);
                _huffmanLengths.AsSpan(position, entries).Fill(bits);
                position += entries;
            }
        }

        _huffmanMaxBits = maxBits;
        return length;
    }

    /// <summary>Decodes a Huffman-coded stream (section 4.2.2) that must produce exactly <paramref name="destination"/>.</summary>
    private void DecodeHuffmanStream(ReadOnlySpan<byte> stream, Span<byte> destination)
    {
        var reader = new BackwardBitReader(stream);
        var maxBits = _huffmanMaxBits;
        var symbols = _huffmanSymbols;
        var lengths = _huffmanLengths;
        for (var i = 0; i < destination.Length; i++)
        {
            var index = (int)reader.PeekBits(maxBits);
            destination[i] = symbols[index];
            reader.Skip(lengths[index]);
            if (reader.IsOverflowed)
                throw Corrupt("a Huffman-coded stream is truncated.");
        }

        if (!reader.IsEmpty)
            throw Corrupt("a Huffman-coded stream has bits left after its literals.");
    }

    /// <summary>Decodes the sequences section (section 3.1.1.3.2) into <see cref="_sequences"/>.</summary>
    /// <returns>The number of sequences.</returns>
    private int DecodeSequences(ReadOnlySpan<byte> section)
    {
        if (section.IsEmpty)
            throw Corrupt("a sequences section is truncated.");

        int count;
        int position;
        var byte0 = section[0];
        if (byte0 == 0)
        {
            if (section.Length != 1)
                throw Corrupt("a block has data after a sequences section without sequences.");

            return 0;
        }

        if (byte0 < 128)
        {
            count = byte0;
            position = 1;
        }
        else if (byte0 < 255)
        {
            if (section.Length < 2)
                throw Corrupt("a sequences section header is truncated.");

            count = ((byte0 - 128) << 8) + section[1];
            position = 2;
        }
        else
        {
            if (section.Length < 3)
                throw Corrupt("a sequences section header is truncated.");

            count = section[1] + (section[2] << 8) + 0x7F00;
            position = 3;
        }

        if (section.Length <= position)
            throw Corrupt("a sequences section header is truncated.");

        if (3 * count > _sequences.Length)
            throw Corrupt("a block has more sequences than its maximum size allows.");

        var modes = section[position++];
        if ((modes & 3) != 0)
            throw Corrupt("the reserved bits of the symbol compression modes are set.");

        position += SetUpTable(section[position..], (modes >> 6) & 3, _literalsLengths, DefaultLiteralsLengthDistribution, 6, MaxLiteralsLengthSymbol);
        position += SetUpTable(section[position..], (modes >> 4) & 3, _offsets, DefaultOffsetDistribution, 5, MaxOffsetSymbol);
        position += SetUpTable(section[position..], (modes >> 2) & 3, _matchLengths, DefaultMatchLengthDistribution, 6, MaxMatchLengthSymbol);

        // Section 3.1.1.3.2.1.2: Decoding Sequences
        var reader = new BackwardBitReader(section[position..]);
        var literalsLengthState = (int)reader.ReadBits(_literalsLengths.AccuracyLog);
        var offsetState = (int)reader.ReadBits(_offsets.AccuracyLog);
        var matchLengthState = (int)reader.ReadBits(_matchLengths.AccuracyLog);
        var values = _sequences.AsSpan();
        var literalsLengthBaselines = LiteralsLengthBaselines;
        var literalsLengthBits = LiteralsLengthBits;
        var matchLengthBaselines = MatchLengthBaselines;
        var matchLengthBits = MatchLengthBits;
        for (var i = 0; i < count; i++)
        {
            var offsetCode = _offsets.Symbols[offsetState];
            var matchLengthCode = _matchLengths.Symbols[matchLengthState];
            var literalsLengthCode = _literalsLengths.Symbols[literalsLengthState];
            if (offsetCode > MaxOffsetSymbol)
                throw Corrupt("an offset code is too large.");

            var offsetValue = (int)((1u << offsetCode) + (uint)reader.ReadBits(offsetCode));
            var matchLength = matchLengthBaselines[matchLengthCode] + (int)reader.ReadBits(matchLengthBits[matchLengthCode]);
            var literalsLength = literalsLengthBaselines[literalsLengthCode] + (int)reader.ReadBits(literalsLengthBits[literalsLengthCode]);
            if (offsetValue <= 0)
                throw Corrupt("an offset is too large.");

            values[3 * i] = literalsLength;
            values[(3 * i) + 1] = offsetValue;
            values[(3 * i) + 2] = matchLength;
            if (i + 1 < count)
            {
                literalsLengthState = _literalsLengths.Baselines[literalsLengthState] + (int)reader.ReadBits(_literalsLengths.Bits[literalsLengthState]);
                matchLengthState = _matchLengths.Baselines[matchLengthState] + (int)reader.ReadBits(_matchLengths.Bits[matchLengthState]);
                offsetState = _offsets.Baselines[offsetState] + (int)reader.ReadBits(_offsets.Bits[offsetState]);
            }

            if (reader.IsOverflowed)
                throw Corrupt("a sequences bitstream is truncated.");
        }

        if (!reader.IsEmpty)
            throw Corrupt("a sequences bitstream has bits left after its sequences.");

        return count;
    }

    /// <summary>Sets up the FSE table of one symbol type from its compression mode (section 3.1.1.3.2.1).</summary>
    /// <returns>The number of bytes of the table description.</returns>
    private int SetUpTable(ReadOnlySpan<byte> data, int mode, FseTable table, ReadOnlySpan<short> defaultDistribution, int defaultAccuracyLog, int maxSymbol)
    {
        switch (mode)
        {
            case 0:
                defaultDistribution.CopyTo(_distribution);
                table.Build(_distribution.AsSpan(0, defaultDistribution.Length), defaultAccuracyLog);
                return 0;

            case 1:
                if (data.IsEmpty)
                    throw Corrupt("an RLE table description is truncated.");

                if (data[0] > maxSymbol)
                    throw Corrupt("an RLE symbol is too large.");

                table.SetRle(data[0]);
                return 1;

            case 2:
                return ReadFseTable(data, table, maxSymbol, table.MaxAccuracyLog);

            default:
                if (!table.IsDefined)
                    throw Corrupt("a table is repeated before it is defined.");

                return 0;
        }
    }

    /// <summary>Reads an FSE table description (section 4.1.1) and builds the decoding table.</summary>
    /// <returns>The number of bytes of the description.</returns>
    private int ReadFseTable(ReadOnlySpan<byte> data, FseTable table, int maxSymbol, int maxAccuracyLog)
    {
        var reader = new ForwardBitReader(data);
        var accuracyLog = (int)reader.ReadBits(4) + 5;
        if (accuracyLog > maxAccuracyLog)
            throw Corrupt("an FSE table has a larger accuracy log than allowed.");

        var remaining = (1 << accuracyLog) + 1;
        var symbol = 0;
        var distribution = _distribution.AsSpan();
        while (remaining > 1)
        {
            if (symbol > maxSymbol)
                throw Corrupt("an FSE table has too many symbols.");

            // Values from 0 to remaining: the small ones use one bit less
            var bits = BitOperations.Log2((uint)remaining) + 1;
            var threshold = (1 << bits) - 1 - remaining;
            var low = (int)reader.PeekBits(bits - 1);
            int value;
            if (low < threshold)
            {
                value = low;
                reader.Skip(bits - 1);
            }
            else
            {
                value = (int)reader.PeekBits(bits);
                if (value >= (1 << (bits - 1)))
                {
                    value -= threshold;
                }

                reader.Skip(bits);
            }

            var probability = value - 1;
            distribution[symbol++] = (short)probability;
            remaining -= probability < 0 ? 1 : probability;
            if (probability == 0)
            {
                // Repeat flags: more symbols of probability zero
                while (true)
                {
                    var repeat = (int)reader.ReadBits(2);
                    if (symbol + repeat > maxSymbol + 1)
                        throw Corrupt("an FSE table has too many symbols.");

                    distribution.Slice(symbol, repeat).Clear();
                    symbol += repeat;
                    if (repeat != 3)
                        break;
                }
            }

            if (reader.IsOverflowed)
                throw Corrupt("an FSE table description is truncated.");
        }

        if (remaining != 1)
            throw Corrupt("the probabilities of an FSE table do not sum to its size.");

        table.Build(distribution[..symbol], accuracyLog);
        return reader.BytesUsed;
    }

    /// <summary>An FSE decoding table (section 4.1): the symbol, the number of bits and the baseline of each state.</summary>
    private sealed class FseTable
    {
        private readonly ushort[] _next = new ushort[256];

        public FseTable(int maxAccuracyLog)
        {
            MaxAccuracyLog = maxAccuracyLog;
            Symbols = new byte[1 << maxAccuracyLog];
            Bits = new byte[1 << maxAccuracyLog];
            Baselines = new int[1 << maxAccuracyLog];
        }

        public int MaxAccuracyLog { get; }

        public int AccuracyLog { get; private set; }

        public bool IsDefined { get; set; }

        public byte[] Symbols { get; }

        public byte[] Bits { get; }

        public int[] Baselines { get; }

        public void SetRle(byte symbol)
        {
            AccuracyLog = 0;
            Symbols[0] = symbol;
            Bits[0] = 0;
            Baselines[0] = 0;
            IsDefined = true;
        }

        public void Build(ReadOnlySpan<short> distribution, int accuracyLog)
        {
            var size = 1 << accuracyLog;
            var high = size - 1;
            var symbols = Symbols.AsSpan(0, size);

            // "Less than 1" symbols take one cell each from the end of the table
            for (var s = 0; s < distribution.Length; s++)
            {
                if (distribution[s] == -1)
                {
                    symbols[high--] = (byte)s;
                    _next[s] = 1;
                }
                else
                {
                    _next[s] = (ushort)distribution[s];
                }
            }

            // The other symbols are spread over the remaining cells
            var step = (size >> 1) + (size >> 3) + 3;
            var mask = size - 1;
            var position = 0;
            for (var s = 0; s < distribution.Length; s++)
            {
                for (var i = 0; i < distribution[s]; i++)
                {
                    symbols[position] = (byte)s;
                    do
                    {
                        position = (position + step) & mask;
                    }
                    while (position > high);
                }
            }

            if (position != 0)
                throw Corrupt("an FSE distribution does not fill its table.");

            // The states of a symbol, in their natural order, give the next states from its count up
            for (var state = 0; state < size; state++)
            {
                var symbol = symbols[state];
                var next = _next[symbol]++;
                var bits = accuracyLog - BitOperations.Log2(next);
                Bits[state] = (byte)bits;
                Baselines[state] = (next << bits) - size;
            }

            AccuracyLog = accuracyLog;
            IsDefined = true;
        }
    }

    /// <summary>
    /// Reads a bitstream backward (sections 4.1 and 4.2.2): from the bit below the final 1 bit of its last byte down to
    /// its first bit, each value little-endian. Reading past the start gives zeros and marks the reader overflowed.
    /// </summary>
    private ref struct BackwardBitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private long _position;

        public BackwardBitReader(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty || data[^1] == 0)
                throw Corrupt("a bitstream does not end with its final bit.");

            _data = data;
            _position = (8L * (data.Length - 1)) + BitOperations.Log2(data[^1]);
        }

        public readonly bool IsOverflowed => _position < 0;

        public readonly bool IsEmpty => _position == 0;

        public ulong ReadBits(int count)
        {
            var value = PeekBits(count);
            _position -= count;
            return value;
        }

        public void Skip(int count) => _position -= count;

        /// <summary>Gets the next <paramref name="count"/> bits (at most 32), the first one as the most significant.</summary>
        public readonly ulong PeekBits(int count)
        {
            if (count == 0)
                return 0;

            var start = _position - count;
            if (start >= 0)
                return Extract(start, count);

            // Below the first bit of the stream: zeros
            if (_position <= 0)
                return 0;

            return Extract(0, (int)_position) << (int)-start;
        }

        private readonly ulong Extract(long bit, int count)
        {
            var index = (int)(bit >> 3);
            var shift = (int)(bit & 7);
            ulong word;
            if (_data.Length - index >= 8)
            {
                word = BinaryPrimitives.ReadUInt64LittleEndian(_data[index..]);
            }
            else
            {
                word = 0;
                for (var i = index; i < _data.Length; i++)
                {
                    word |= (ulong)_data[i] << (8 * (i - index));
                }
            }

            return (word >> shift) & ((1UL << count) - 1);
        }
    }

    /// <summary>Reads a bitstream forward, each value little-endian (section 4.1.1). Reading past the end gives zeros and marks the reader overflowed.</summary>
    private ref struct ForwardBitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private long _position;

        public readonly bool IsOverflowed => _position > 8L * _data.Length;

        public readonly int BytesUsed => (int)((_position + 7) >> 3);

        public ulong ReadBits(int count)
        {
            var value = PeekBits(count);
            _position += count;
            return value;
        }

        public void Skip(int count) => _position += count;

        public readonly ulong PeekBits(int count)
        {
            if (count == 0)
                return 0;

            var index = (int)(_position >> 3);
            var shift = (int)(_position & 7);
            ulong word = 0;
            for (var i = 0; i < 8 && index + i < _data.Length; i++)
            {
                word |= (ulong)_data[index + i] << (8 * i);
            }

            return (word >> shift) & ((1UL << count) - 1);
        }
    }
}
