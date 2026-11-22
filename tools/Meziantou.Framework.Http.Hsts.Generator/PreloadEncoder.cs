#pragma warning disable MA0047 // Declare types in namespaces

// Writes the preload list in the form HstsPreloadList searches in place, without decompressing it. See
// src/Meziantou.Framework.Http.Hsts/HstsPreloadList.cs for the reader and the reasons for the layout.
//
// The host names are stored with their labels reversed ("www.example.com" as "com.example.www"), sorted
// ordinally, so that names sharing a domain are next to each other. They are cut into blocks of BlockSize names.
// The first name of a block is stored whole, and every other one as the length of the prefix it shares with the
// name before it and the rest, its suffix. The characters are Huffman coded, with one code per previous character.
// Each suffix is preceded by its length in bits, so a reader can skip a name without decoding it. A name other
// than the first of a block starts with one Huffman-coded symbol for both its prefix length and its suffix bit
// length, so skipping it is a single decode, and the first names have a code of their own for their bit length.
//
// Layout, integers little-endian:
//   int32   format version (1)
//   int32   entry count
//   int32   block size
//   int32   highest label count
//   uint8   symbol count n: symbol 0 marks the end of a name in the reader and is never coded
//   uint8[n - 1]  the characters of the symbols 1 to n - 1, ascending
//   uint8[n * n]  the code lengths of the symbols, n per context: the context is the previous symbol, 0 at the start
//   uint16  first name bit length count
//   uint8[first name bit length count]  the code lengths of the bit lengths of the first names, from 0
//   uint16  length pair count
//   (uint8 prefix length, uint16 suffix bit length, uint8 code length)[length pair count]  the length pairs of the
//           other names, ascending
//   int32   block count
//   int32[block count]  the bit offset of each block in the bit stream
//   int32   bit stream length, in bits
//   uint8[ceil(bits / 8) + 8]  the bit stream, most significant bit first, followed by zeros so it can be read
//                              eight bytes at a time. The first name of a block is its bit length and its symbols,
//                              and the others their length pair and the symbols of their suffix.
//   uint8[ceil(entry count / 8)]  the include_subdomains flags, in the order of the names
internal static class PreloadEncoder
{
    public const int FormatVersion = 1;

    // The reader binary-searches the first names of the blocks and then walks one block, decoding the length codes
    // of the names and only the suffixes that can match. This trades the size of the offsets and of the first names
    // against the names walked per lookup.
    private const int BlockSize = 16;

    // The reader decodes a symbol from a 64-bit window, so a code must fit in it with room for the bit offset
    // within a byte. 32 also lets it compare codes as 32-bit integers.
    private const int MaxCodeLength = 32;

    public static byte[] Encode(IReadOnlyList<Data> entries, int maxLabelCount)
    {
        var ordered = entries
            .Select(entry => (Key: ReverseLabels(entry.Name), entry.IncludeSubdomains))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();

        // The symbols follow the order of their character, so that the order of the symbol sequences is the
        // ordinal order of the names. Symbol 0 is kept for the end of a name, which sorts first.
        var characters = ordered.SelectMany(entry => entry.Key).Distinct().Order().ToArray();
        if (characters.Length + 1 > byte.MaxValue)
            throw new InvalidOperationException("The host names use too many distinct characters");

        var symbolCount = characters.Length + 1;
        var symbolOf = new Dictionary<char, byte>();
        for (var i = 0; i < characters.Length; i++)
        {
            symbolOf.Add(characters[i], (byte)(i + 1));
        }

        var names = ordered.Select(entry => entry.Key.Select(c => symbolOf[c]).ToArray()).ToList();
        var prefixLengths = new int[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            if (i % BlockSize != 0)
            {
                prefixLengths[i] = CommonPrefixLength(names[i - 1], names[i]);
            }
        }

        var symbolFrequencies = new long[symbolCount][];
        for (var context = 0; context < symbolCount; context++)
        {
            symbolFrequencies[context] = new long[symbolCount];
        }

        for (var i = 0; i < names.Count; i++)
        {
            foreach (var (context, symbol) in GetCodedSymbols(names[i], prefixLengths[i]))
            {
                symbolFrequencies[context][symbol]++;
            }
        }

        var symbolCodes = symbolFrequencies.Select(BuildCanonicalCode).ToArray();

        // The bit lengths depend on the codes of the characters, so their own codes are built last
        var suffixBitLengths = new int[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            foreach (var (context, symbol) in GetCodedSymbols(names[i], prefixLengths[i]))
            {
                suffixBitLengths[i] += symbolCodes[context][symbol].Length;
            }
        }

        if (suffixBitLengths.Max() > ushort.MaxValue - 1)
            throw new InvalidOperationException("A host name takes too many bits");

        var firstNameBitLengthFrequencies = new long[Enumerable.Range(0, names.Count).Where(i => i % BlockSize == 0).Max(i => suffixBitLengths[i]) + 1];
        var pairFrequencies = new SortedDictionary<(int PrefixLength, int BitLength), long>();
        for (var i = 0; i < names.Count; i++)
        {
            if (i % BlockSize == 0)
            {
                firstNameBitLengthFrequencies[suffixBitLengths[i]]++;
            }
            else
            {
                var pair = (prefixLengths[i], suffixBitLengths[i]);
                pairFrequencies[pair] = pairFrequencies.GetValueOrDefault(pair) + 1;
            }
        }

        var pairs = pairFrequencies.Keys.ToList();
        if (pairs.Count > ushort.MaxValue)
            throw new InvalidOperationException("The host names have too many length pairs");

        var pairIndex = pairs.Select((pair, index) => (pair, index)).ToDictionary(item => item.pair, item => item.index);
        var firstNameBitLengthCode = BuildCanonicalCode(firstNameBitLengthFrequencies);
        var pairCode = BuildCanonicalCode([.. pairFrequencies.Values]);

        var bits = new BitWriter();
        var blockOffsets = new List<int>();
        for (var i = 0; i < names.Count; i++)
        {
            if (i % BlockSize == 0)
            {
                blockOffsets.Add(bits.Length);
                bits.Write(firstNameBitLengthCode[suffixBitLengths[i]]);
            }
            else
            {
                bits.Write(pairCode[pairIndex[(prefixLengths[i], suffixBitLengths[i])]]);
            }

            var start = bits.Length;
            foreach (var (context, symbol) in GetCodedSymbols(names[i], prefixLengths[i]))
            {
                bits.Write(symbolCodes[context][symbol]);
            }

            if (bits.Length - start != suffixBitLengths[i])
                throw new InvalidOperationException("The suffix does not take the bits it declares");
        }

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write(FormatVersion);
            writer.Write(names.Count);
            writer.Write(BlockSize);
            writer.Write(maxLabelCount);
            writer.Write((byte)symbolCount);
            foreach (var c in characters)
            {
                writer.Write((byte)c);
            }

            foreach (var code in symbolCodes)
            {
                WriteLengths(writer, code);
            }

            writer.Write((ushort)firstNameBitLengthCode.Length);
            WriteLengths(writer, firstNameBitLengthCode);
            writer.Write((ushort)pairs.Count);
            for (var i = 0; i < pairs.Count; i++)
            {
                writer.Write((byte)pairs[i].PrefixLength);
                writer.Write((ushort)pairs[i].BitLength);
                writer.Write((byte)pairCode[i].Length);
            }

            writer.Write(blockOffsets.Count);
            foreach (var offset in blockOffsets)
            {
                writer.Write(offset);
            }

            writer.Write(bits.Length);
            writer.Write(bits.ToArray());
            writer.Write(new byte[8]);

            var flags = new byte[(names.Count + 7) / 8];
            for (var i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].IncludeSubdomains)
                {
                    flags[i >> 3] |= (byte)(1 << (i & 7));
                }
            }

            writer.Write(flags);
        }

        return ms.ToArray();

        static void WriteLengths(BinaryWriter writer, (uint Code, int Length)[] code)
        {
            foreach (var (_, length) in code)
            {
                writer.Write((byte)length);
            }
        }
    }

    public static string ReverseLabels(string name) => string.Join('.', name.Split('.').Reverse());

    // The symbols a name codes after the prefix it shares with the previous one, each with the symbol before it
    private static IEnumerable<(int Context, int Symbol)> GetCodedSymbols(byte[] name, int prefixLength)
    {
        var context = prefixLength == 0 ? 0 : name[prefixLength - 1];
        for (var i = prefixLength; i < name.Length; i++)
        {
            yield return (context, name[i]);
            context = name[i];
        }
    }

    private static int CommonPrefixLength(byte[] x, byte[] y)
    {
        var length = 0;
        while (length < x.Length && length < y.Length && x[length] == y[length])
        {
            length++;
        }

        return length;
    }

    // Returns the code and the length of every symbol, 0 for those that never occur. The lengths are what the
    // file stores: the reader rebuilds the codes from them the same way.
    private static (uint Code, int Length)[] BuildCanonicalCode(long[] frequencies)
    {
        var lengths = BuildCodeLengths(frequencies);
        var result = new (uint Code, int Length)[frequencies.Length];
        var code = 0u;
        for (var length = 1; length <= MaxCodeLength; length++)
        {
            for (var symbol = 0; symbol < frequencies.Length; symbol++)
            {
                if (lengths[symbol] == length)
                {
                    result[symbol] = (code, length);
                    code++;
                }
            }

            code <<= 1;
        }

        return result;
    }

    private static int[] BuildCodeLengths(long[] frequencies)
    {
        var lengths = new int[frequencies.Length];
        var used = Enumerable.Range(0, frequencies.Length).Where(symbol => frequencies[symbol] > 0).ToList();
        if (used.Count == 0)
            return lengths;

        if (used.Count == 1)
        {
            lengths[used[0]] = 1;
            return lengths;
        }

        // Halving the frequencies flattens the tree until the longest code fits. Never needed with the current
        // data, but a rare character in an unusual context must not make the generator fail.
        var weights = frequencies.ToArray();
        while (true)
        {
            // The ties are broken by creation order, so the same list always gives the same file
            var queue = new PriorityQueue<int, (long Weight, int Order)>();
            var parents = new List<int>();
            foreach (var symbol in used)
            {
                queue.Enqueue(parents.Count, (weights[symbol], parents.Count));
                parents.Add(-1);
            }

            while (queue.Count > 1)
            {
                queue.TryDequeue(out var first, out var firstPriority);
                queue.TryDequeue(out var second, out var secondPriority);
                var node = parents.Count;
                parents.Add(-1);
                parents[first] = node;
                parents[second] = node;
                queue.Enqueue(node, (firstPriority.Weight + secondPriority.Weight, node));
            }

            var maxLength = 0;
            for (var i = 0; i < used.Count; i++)
            {
                var length = 0;
                for (var node = i; parents[node] >= 0; node = parents[node])
                {
                    length++;
                }

                lengths[used[i]] = length;
                maxLength = Math.Max(maxLength, length);
            }

            if (maxLength <= MaxCodeLength)
                return lengths;

            foreach (var symbol in used)
            {
                weights[symbol] = (weights[symbol] + 1) / 2;
            }
        }
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];

        public int Length { get; private set; }

        public void Write((uint Code, int Length) code)
        {
            if (code.Length == 0)
                throw new InvalidOperationException("The symbol has no code");

            for (var i = code.Length - 1; i >= 0; i--)
            {
                if ((Length & 7) == 0)
                {
                    _bytes.Add(0);
                }

                if (((code.Code >> i) & 1) != 0)
                {
                    _bytes[^1] |= (byte)(0x80 >> (Length & 7));
                }

                Length++;
            }
        }

        public byte[] ToArray() => [.. _bytes];
    }
}
