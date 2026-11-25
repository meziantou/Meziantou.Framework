namespace Meziantou.Framework.Compression.Internals.BZip2;

/// <summary>
/// Compresses a block of bzip2: the Burrows-Wheeler transform (the rotations are sorted with a suffix array, built with
/// SA-IS, of the smallest rotation of the block), the move-to-front transform and the encoding of the runs of zeros, and up to 6 Huffman tables chosen
/// for each group of 50 symbols, refined 4 times as bzip2 does.
/// </summary>
internal sealed class BZip2BlockCompressor
{
    private const int Iterations = 4;
    private const int MaxEncodedCodeLength = 17;
    private const byte LesserCost = 0;
    private const byte GreaterCost = 15;
    private const int CostBits = 10;
    private const ulong CostMask = (1 << CostBits) - 1;

    private int[] _suffixArray = [];
    private ushort[] _text = [];
    private byte[] _transformed = [];
    private ushort[] _symbols = [];
    private readonly byte[] _selectors = new byte[BZip2Constants.MaxSelectors];
    private readonly byte[][] _lengths = CreateTables<byte>();
    private readonly int[][] _frequencies = CreateTables<int>();
    private readonly uint[][] _codes = CreateTables<uint>();

    /// <summary>Writes the block, from its magic number to its last symbol.</summary>
    public void Compress(byte[] block, int length, uint crc, bool randomized, BZip2BitWriter writer)
    {
        var origin = Transform(block, length);
        var used = new bool[256];
        foreach (var value in _transformed.AsSpan(0, length))
        {
            used[value] = true;
        }

        var symbolCount = EncodeMoveToFront(length, used, out var usedCount);
        var alphabetSize = usedCount + 2;

        writer.Write(24, (uint)(BZip2Constants.BlockMagic >> 24));
        writer.Write(24, (uint)(BZip2Constants.BlockMagic & 0xFFFFFF));
        writer.Write(32, crc);
        writer.Write(1, randomized ? 1u : 0u);
        writer.Write(24, (uint)origin);

        // The bytes used in the block, in groups of 16
        uint groups = 0;
        for (var i = 0; i < 16; i++)
        {
            if (used.AsSpan(i * 16, 16).Contains(true))
            {
                groups |= 0x8000u >> i;
            }
        }

        writer.Write(16, groups);
        for (var i = 0; i < 16; i++)
        {
            if ((groups & (0x8000u >> i)) is 0)
                continue;

            uint bits = 0;
            for (var j = 0; j < 16; j++)
            {
                if (used[(i * 16) + j])
                {
                    bits |= 0x8000u >> j;
                }
            }

            writer.Write(16, bits);
        }

        WriteHuffmanData(symbolCount, alphabetSize, writer);
    }

    /// <summary>Sorts the rotations of the block and writes the last column to <see cref="_transformed"/>.</summary>
    /// <returns>The row of the block itself.</returns>
    /// <remarks>
    /// The block is rotated to start at its smallest rotation: when the block is not periodic, this rotation is a Lyndon
    /// word, whose rotations are in the order of its suffixes (a suffix that is a prefix of another one is the smaller),
    /// so that a suffix array of the rotated block and a sentinel sorts the rotations. A periodic block, whose rotations
    /// are not all different, is sorted with the suffixes of the block written twice.
    /// </remarks>
    private int Transform(byte[] block, int length)
    {
        if (_transformed.Length < length)
        {
            _transformed = new byte[length];
        }

        var start = FindSmallestRotation(block, length, out var periodic);
        var copies = periodic ? 2 : 1;
        var textLength = (copies * length) + 1;
        if (_suffixArray.Length < textLength)
        {
            _suffixArray = new int[textLength];
        }

        if (_text.Length < textLength)
        {
            _text = new ushort[textLength];
        }

        // The symbols are the bytes plus one, and the sentinel is 0
        var text = _text;
        for (var i = 0; i < copies * length; i++)
        {
            var position = start + i;
            while (position >= length)
            {
                position -= length;
            }

            text[i] = (ushort)(block[position] + 1);
        }

        text[copies * length] = 0;
        SuffixArray.Sort(new SuffixArray.UInt16Text(text), _suffixArray, textLength, 257);

        var origin = 0;
        var row = 0;
        var suffixes = _suffixArray;
        var transformed = _transformed;
        for (var i = 1; i < textLength; i++)
        {
            var suffix = suffixes[i];
            if (suffix >= length)
                continue;

            // The rotation of the block that starts at this suffix of the rotated block
            var rotation = suffix + start;
            if (rotation >= length)
            {
                rotation -= length;
            }

            if (rotation is 0)
            {
                origin = row;
                transformed[row++] = block[length - 1];
            }
            else
            {
                transformed[row++] = block[rotation - 1];
            }
        }

        return origin;
    }

    /// <summary>Finds the start of the smallest rotation of the block (the two-pointer algorithm of the minimal string rotation).</summary>
    /// <param name="block">The block.</param>
    /// <param name="length">The length of the block.</param>
    /// <param name="periodic">Whether two rotations are equal: the block is a repetition of a shorter string.</param>
    private static int FindSmallestRotation(byte[] block, int length, out bool periodic)
    {
        periodic = false;
        int i = 0, j = 1, k = 0;
        while (i < length && j < length && k < length)
        {
            var a = i + k;
            if (a >= length)
            {
                a -= length;
            }

            var b = j + k;
            if (b >= length)
            {
                b -= length;
            }

            var difference = block[a] - block[b];
            if (difference is 0)
            {
                k++;
                continue;
            }

            if (difference > 0)
            {
                i += k + 1;
            }
            else
            {
                j += k + 1;
            }

            if (i == j)
            {
                j++;
            }

            k = 0;
        }

        // A block of one byte is not periodic; k reaches the length when the rotations i and j are equal
        periodic = k >= length && length > 1;
        return Math.Min(i, j);
    }

    /// <summary>Encodes the last column with move-to-front, the runs of zeros in bijective base 2 (RUNA, RUNB), and the end of the block.</summary>
    /// <returns>The number of symbols.</returns>
    private int EncodeMoveToFront(int length, bool[] used, out int usedCount)
    {
        Span<byte> map = stackalloc byte[256];
        usedCount = 0;
        for (var i = 0; i < 256; i++)
        {
            if (used[i])
            {
                map[i] = (byte)usedCount++;
            }
        }

        if (_symbols.Length < length + 1)
        {
            _symbols = new ushort[length + 1];
        }

        var endOfBlock = usedCount + 1;
        Span<byte> list = stackalloc byte[256];
        for (var i = 0; i < usedCount; i++)
        {
            list[i] = (byte)i;
        }

        var symbols = _symbols;
        var count = 0;
        var zeros = 0;
        foreach (var value in _transformed.AsSpan(0, length))
        {
            var mapped = map[value];
            if (list[0] == mapped)
            {
                zeros++;
                continue;
            }

            if (zeros > 0)
            {
                count = WriteRun(symbols, count, zeros);
                zeros = 0;
            }

            var position = list[1..usedCount].IndexOf(mapped) + 1;
            BZip2Constants.MoveToFront(list, position);
            symbols[count++] = (ushort)(position + 1);
        }

        if (zeros > 0)
        {
            count = WriteRun(symbols, count, zeros);
        }

        symbols[count++] = (ushort)endOfBlock;
        return count;

        static int WriteRun(ushort[] symbols, int count, int run)
        {
            run--;
            while (true)
            {
                symbols[count++] = (run & 1) is 0 ? (ushort)BZip2Constants.RunA : (ushort)BZip2Constants.RunB;
                if (run < 2)
                    break;

                run = (run - 2) / 2;
            }

            return count;
        }
    }

    private void WriteHuffmanData(int symbolCount, int alphabetSize, BZip2BitWriter writer)
    {
        var symbols = _symbols.AsSpan(0, symbolCount);
        var groupCount = symbolCount switch
        {
            < 200 => 2,
            < 600 => 3,
            < 1200 => 4,
            < 2400 => 5,
            _ => 6,
        };

        Span<int> frequencies = stackalloc int[BZip2Constants.MaxAlphabetSize];
        frequencies.Clear();
        foreach (var symbol in symbols)
        {
            frequencies[symbol]++;
        }

        // The initial tables: each one is cheap for a range of symbols of about the same total frequency
        var remaining = symbolCount;
        var start = 0;
        for (var part = groupCount; part > 0; part--)
        {
            var target = remaining / part;
            var end = start - 1;
            var sum = 0;
            while (sum < target && end < alphabetSize - 1)
            {
                end++;
                sum += frequencies[end];
            }

            if (end > start && part != groupCount && part != 1 && ((groupCount - part) % 2) is 1)
            {
                sum -= frequencies[end];
                end--;
            }

            var lengths = _lengths[part - 1];
            for (var symbol = 0; symbol < alphabetSize; symbol++)
            {
                lengths[symbol] = symbol >= start && symbol <= end ? LesserCost : GreaterCost;
            }

            start = end + 1;
            remaining -= sum;
        }

        // Choose the cheapest table of each group, and compute the tables of the groups that chose them
        var selectorCount = 0;
        Span<ulong> packedLengths = stackalloc ulong[BZip2Constants.MaxAlphabetSize];
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            for (var t = 0; t < groupCount; t++)
            {
                Array.Clear(_frequencies[t]);
            }

            // The lengths of each symbol in every table, CostBits bits per table, so that one addition per symbol computes the
            // cost of a group in every table: a group costs at most 50 × 17 bits, which fits in CostBits bits
            for (var symbol = 0; symbol < alphabetSize; symbol++)
            {
                ulong packed = 0;
                for (var t = 0; t < groupCount; t++)
                {
                    packed |= (ulong)_lengths[t][symbol] << (t * CostBits);
                }

                packedLengths[symbol] = packed;
            }

            selectorCount = 0;
            for (var groupStart = 0; groupStart < symbolCount; groupStart += BZip2Constants.GroupSize)
            {
                var group = symbols.Slice(groupStart, Math.Min(BZip2Constants.GroupSize, symbolCount - groupStart));
                ulong costs = 0;
                foreach (var symbol in group)
                {
                    costs += packedLengths[symbol];
                }

                var best = 0;
                var bestCost = costs & CostMask;
                for (var t = 1; t < groupCount; t++)
                {
                    var cost = (costs >> (t * CostBits)) & CostMask;
                    if (cost < bestCost)
                    {
                        best = t;
                        bestCost = cost;
                    }
                }

                _selectors[selectorCount++] = (byte)best;
                var bestFrequencies = _frequencies[best];
                foreach (var symbol in group)
                {
                    bestFrequencies[symbol]++;
                }
            }

            for (var t = 0; t < groupCount; t++)
            {
                BZip2HuffmanLengths.Compute(_frequencies[t].AsSpan(0, alphabetSize), _lengths[t].AsSpan(0, alphabetSize), MaxEncodedCodeLength);
            }
        }

        // The canonical codes
        for (var t = 0; t < groupCount; t++)
        {
            var lengths = _lengths[t];
            var codes = _codes[t];
            uint code = 0;
            for (var length = 1; length <= MaxEncodedCodeLength; length++)
            {
                for (var symbol = 0; symbol < alphabetSize; symbol++)
                {
                    if (lengths[symbol] == length)
                    {
                        codes[symbol] = code++;
                    }
                }

                code <<= 1;
            }
        }

        writer.Write(3, (uint)groupCount);
        writer.Write(15, (uint)selectorCount);

        // The selectors, move-to-front encoded, in unary
        Span<byte> list = stackalloc byte[BZip2Constants.MaxGroups];
        for (var i = 0; i < groupCount; i++)
        {
            list[i] = (byte)i;
        }

        foreach (var selector in _selectors.AsSpan(0, selectorCount))
        {
            var position = list.IndexOf(selector);
            list[..position].CopyTo(list[1..]);
            list[0] = selector;
            for (var i = 0; i < position; i++)
            {
                writer.Write(1, 1);
            }

            writer.Write(1, 0);
        }

        // The lengths of the codes, as deltas
        for (var t = 0; t < groupCount; t++)
        {
            var lengths = _lengths[t];
            int current = lengths[0];
            writer.Write(5, (uint)current);
            for (var symbol = 0; symbol < alphabetSize; symbol++)
            {
                while (current < lengths[symbol])
                {
                    writer.Write(2, 2);
                    current++;
                }

                while (current > lengths[symbol])
                {
                    writer.Write(2, 3);
                    current--;
                }

                writer.Write(1, 0);
            }
        }

        // The symbols
        var selectorIndex = 0;
        for (var groupStart = 0; groupStart < symbolCount; groupStart += BZip2Constants.GroupSize)
        {
            var group = symbols.Slice(groupStart, Math.Min(BZip2Constants.GroupSize, symbolCount - groupStart));
            var table = _selectors[selectorIndex++];
            var lengths = _lengths[table];
            var codes = _codes[table];
            foreach (var symbol in group)
            {
                writer.Write(lengths[symbol], codes[symbol]);
            }
        }
    }

    private static T[][] CreateTables<T>()
    {
        var tables = new T[BZip2Constants.MaxGroups][];
        for (var i = 0; i < tables.Length; i++)
        {
            tables[i] = new T[BZip2Constants.MaxAlphabetSize];
        }

        return tables;
    }
}
