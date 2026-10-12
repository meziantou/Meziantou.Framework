using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Collects the values of one entropy-coded stream, then writes its description and the values (ISO/IEC 18181-1, section
/// D.3): no LZ77, a simple context map, one hybrid integer configuration for every cluster, and for each cluster either
/// the lengths of a canonical prefix code built from the tokens it codes, or their distribution for the ANS coder.
/// </summary>
/// <remarks>
/// A prefix code spends at least one bit per value; the ANS coder spends a fraction of a bit on a value that is almost
/// certain, and nothing on the values of a cluster that only holds one token. Its state is written in the reverse order of
/// the values, so a stream is only written once it is complete.
/// </remarks>
internal sealed class JpegXlEntropyEncoder : IDisposable
{
    /// <summary>The largest number of clusters a simple context map can name.</summary>
    public const int MaxClusters = 8;

    private const int MaxAlphabetSize = 1 << JpegXlPrefixCode.MaxLength;

    /// <summary>The base-2 logarithm of the alphabet size of the ANS distributions that are written: 256 tokens.</summary>
    internal const int AnsLogAlphabetSize = 8;

    private const int RefillFlag = 1 << 16;

    private readonly AllocationScope _scope;
    private readonly int _contextCount;
    private readonly int _clusterCount;
    private readonly JpegXlHybridUintConfig _config;
    private readonly bool _useAns;
    private readonly uint[][] _histograms;
    private Av1PooledArray<ulong>? _values;
    private int _count;

    /// <summary>Starts a stream.</summary>
    /// <param name="contextCount">The number of contexts of the stream.</param>
    /// <param name="config">The hybrid integer configuration of every cluster.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="useAns"><see langword="true"/> to write the values with the ANS coder; <see langword="false"/> to write them with prefix codes.</param>
    public JpegXlEntropyEncoder(int contextCount, JpegXlHybridUintConfig config, AllocationScope scope, bool useAns = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(contextCount, 1);
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
        _contextCount = contextCount;
        _clusterCount = Math.Min(contextCount, MaxClusters);
        _config = config;
        _useAns = useAns;

        // The largest token of a 32-bit value: the split, then one group of tokens per number of raw bits
        var alphabetSize = (1 << config.SplitExponent) + ((32 - config.SplitExponent) << (config.MsbInToken + config.LsbInToken));
        if (useAns && (alphabetSize > 1 << AnsLogAlphabetSize || config.SplitExponent > AnsLogAlphabetSize))
            throw new ArgumentException("The tokens of the configuration do not fit the alphabet of the ANS coder.", nameof(config));

        _histograms = new uint[_clusterCount][];
        for (var i = 0; i < _histograms.Length; i++)
        {
            _histograms[i] = new uint[alphabetSize];
        }
    }

    /// <summary>Adds a value to the stream.</summary>
    public void Add(int context, uint value)
    {
        if (_values is null || _count == _values.Length)
        {
            var larger = Av1PooledArray<ulong>.Rent(_scope, Math.Max(1024, (long)_count * 2), clear: false);
            _values?.Span.CopyTo(larger.Span);
            _values?.Dispose();
            _values = larger;
        }

        // A value is kept as its cluster, its token, the number of raw bits and the raw bits
        var cluster = context % MaxClusters;
        var (token, bitCount, bits) = GetToken(value);
        _histograms[cluster][token]++;
        _values.Span[_count++] = ((ulong)cluster << 56) | ((ulong)token << 40) | ((ulong)bitCount << 32) | bits;
    }

    /// <summary>Writes the description of the stream, then its values.</summary>
    public void Write(LsbBitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // LZ77 is not used
        writer.WriteBool(false);
        if (_contextCount > 1)
        {
            // A simple context map: the cluster of each context, on a fixed number of bits
            var bitsPerEntry = JpegXlHybridUintConfig.CeilingLog2(_clusterCount);
            writer.WriteBool(true);
            writer.WriteBits((uint)bitsPerEntry, 2);
            for (var i = 0; i < _contextCount; i++)
            {
                writer.WriteBits((uint)(i % MaxClusters), bitsPerEntry);
            }
        }

        // Prefix codes or ANS, and the same configuration of hybrid integers for every cluster
        writer.WriteBool(!_useAns);
        var logAlphabetSize = JpegXlPrefixCode.MaxLength;
        if (_useAns)
        {
            logAlphabetSize = AnsLogAlphabetSize;
            writer.WriteBits(AnsLogAlphabetSize - 5, 2);
        }

        for (var i = 0; i < _clusterCount; i++)
        {
            WriteConfig(writer, _config, logAlphabetSize);
        }

        if (_useAns)
        {
            WriteAnsValues(writer);
            return;
        }

        var alphabetSizes = new int[_clusterCount];
        for (var i = 0; i < _clusterCount; i++)
        {
            var size = _histograms[i].Length;
            while (size > 1 && _histograms[i][size - 1] == 0)
            {
                size--;
            }

            alphabetSizes[i] = size;
            WriteVarLenUint16(writer, size - 1);
        }

        var codes = new ushort[_clusterCount][];
        var lengths = new byte[_clusterCount][];
        for (var i = 0; i < _clusterCount; i++)
        {
            lengths[i] = new byte[alphabetSizes[i]];
            codes[i] = new ushort[alphabetSizes[i]];
            if (alphabetSizes[i] > 1)
            {
                WritePrefixCode(writer, _histograms[i].AsSpan(0, alphabetSizes[i]), lengths[i], codes[i]);
            }
        }

        var values = _values is null ? default : _values.Span[.._count];
        foreach (var value in values)
        {
            var cluster = (int)(value >> 56);
            var token = (int)((value >> 40) & 0xFFFF);
            writer.WriteBits(codes[cluster][token], lengths[cluster][token]);
            writer.WriteBits((uint)value, (int)((value >> 32) & 0xFF));
        }
    }

    public void Dispose()
    {
        _values?.Dispose();
        _values = null;
    }

    /// <summary>Writes the distribution of each cluster, the state of the coder, then the values.</summary>
    private void WriteAnsValues(LsbBitWriter writer)
    {
        const int TableSize = JpegXlAnsDistribution.TableSize;
        const int BucketCount = 1 << AnsLogAlphabetSize;

        // For each cluster: the probability of each token, where the positions of each token start once sorted by token, and
        // the state the decoder maps to each of these positions (the inverse of its alias table)
        using var distributions = Av1PooledArray<int>.Rent(_scope, (long)_clusterCount * BucketCount, clear: true);
        using var starts = Av1PooledArray<int>.Rent(_scope, (long)_clusterCount * BucketCount, clear: false);
        using var positions = Av1PooledArray<ushort>.Rent(_scope, (long)_clusterCount * TableSize, clear: false);
        for (var cluster = 0; cluster < _clusterCount; cluster++)
        {
            var distribution = distributions.Span.Slice(cluster * BucketCount, BucketCount);
            Normalize(_histograms[cluster], distribution);
            WriteDistribution(writer, distribution);
            BuildAnsEncodingTable(distribution, starts.Span.Slice(cluster * BucketCount, BucketCount), positions.Span.Slice(cluster * TableSize, TableSize));
        }

        var values = _values is null ? default : _values.Span[.._count];
        using var refills = Av1PooledArray<int>.Rent(_scope, Math.Max(1, _count), clear: false);
        WriteAnsStream(writer, values, distributions.Span, starts.Span, positions.Span, refills.Span);
    }

    /// <summary>Writes the configuration of the hybrid integers of a cluster.</summary>
    internal static void WriteConfig(LsbBitWriter writer, JpegXlHybridUintConfig config, int logAlphabetSize)
    {
        writer.WriteBits((uint)config.SplitExponent, JpegXlHybridUintConfig.CeilingLog2(logAlphabetSize + 1));
        if (config.SplitExponent != logAlphabetSize)
        {
            writer.WriteBits((uint)config.MsbInToken, JpegXlHybridUintConfig.CeilingLog2(config.SplitExponent + 1));
            writer.WriteBits((uint)config.LsbInToken, JpegXlHybridUintConfig.CeilingLog2(config.SplitExponent - config.MsbInToken + 1));
        }
    }

    /// <summary>
    /// Computes, for a distribution of 256 tokens, where the positions of each token start once sorted by token, and the
    /// state the decoder maps to each of these positions (the inverse of its alias table).
    /// </summary>
    internal static void BuildAnsEncodingTable(ReadOnlySpan<int> distribution, Span<int> starts, Span<ushort> positions)
    {
        const int BucketCount = 1 << AnsLogAlphabetSize;
        const int LogBucketSize = JpegXlAnsDistribution.LogTableSize - AnsLogAlphabetSize;
        Span<JpegXlAnsDistribution.AliasEntry> aliases = stackalloc JpegXlAnsDistribution.AliasEntry[BucketCount];
        var sum = 0;
        for (var i = 0; i < BucketCount; i++)
        {
            starts[i] = sum;
            sum += distribution[i];
        }

        JpegXlAnsDistribution.BuildAliasTable(distribution, aliases);
        for (var index = 0; index < JpegXlAnsDistribution.TableSize; index++)
        {
            var bucket = index >> LogBucketSize;
            var offset = index & ((1 << LogBucketSize) - 1);
            ref readonly var entry = ref aliases[bucket];
            if (offset < entry.Cutoff)
            {
                positions[starts[bucket] + offset] = (ushort)index;
            }
            else
            {
                positions[starts[entry.Symbol] + entry.Offset + offset] = (ushort)index;
            }
        }
    }

    /// <summary>
    /// Writes the state of the ANS coder, then the values of a stream. The decoder reads the values first to last and ends
    /// in the initial state: the states are computed last to first. When a state would not fit 32 bits, its low 16 bits are
    /// what the decoder reads after the value.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="values">The values: the cluster, the token, the number of raw bits and the raw bits of each.</param>
    /// <param name="distributions">The distributions of the clusters, 256 values each.</param>
    /// <param name="starts">The starts of the clusters, as <see cref="BuildAnsEncodingTable"/> computes them.</param>
    /// <param name="positions">The positions of the clusters, as <see cref="BuildAnsEncodingTable"/> computes them.</param>
    /// <param name="refills">Scratch memory of the length of <paramref name="values"/>.</param>
    internal static void WriteAnsStream(LsbBitWriter writer, ReadOnlySpan<ulong> values, ReadOnlySpan<int> distributions, ReadOnlySpan<int> starts, ReadOnlySpan<ushort> positions, Span<int> refills)
    {
        const int TableSize = JpegXlAnsDistribution.TableSize;
        const int BucketCount = 1 << AnsLogAlphabetSize;
        var state = JpegXlAnsDistribution.InitialState;
        for (var i = values.Length - 1; i >= 0; i--)
        {
            var cluster = (int)(values[i] >> 56);
            var token = (int)((values[i] >> 40) & 0xFFFF);
            var frequency = (uint)distributions[(cluster * BucketCount) + token];
            var refill = 0;
            if ((state >> (32 - JpegXlAnsDistribution.LogTableSize)) >= frequency)
            {
                refill = RefillFlag | (int)(state & 0xFFFF);
                state >>= 16;
            }

            refills[i] = refill;
            var (quotient, remainder) = Math.DivRem(state, frequency);
            state = (quotient << JpegXlAnsDistribution.LogTableSize) + positions[(cluster * TableSize) + starts[(cluster * BucketCount) + token] + (int)remainder];
        }

        writer.WriteBits(state & 0xFFFF, 16);
        writer.WriteBits(state >> 16, 16);
        for (var i = 0; i < values.Length; i++)
        {
            var refill = refills[i];
            if (refill != 0)
            {
                writer.WriteBits((uint)refill & 0xFFFF, 16);
            }

            writer.WriteBits((uint)values[i], (int)((values[i] >> 32) & 0xFF));
        }
    }

    /// <summary>
    /// Scales the counts of the tokens of a cluster to probabilities whose sum is 4096, each token that occurs keeping a
    /// probability; a cluster without value is given the token 0.
    /// </summary>
    internal static void Normalize(ReadOnlySpan<uint> histogram, Span<int> distribution)
    {
        const int TableSize = JpegXlAnsDistribution.TableSize;
        distribution.Clear();
        ulong total = 0;
        foreach (var count in histogram)
        {
            total += count;
        }

        if (total == 0)
        {
            distribution[0] = TableSize;
            return;
        }

        var sum = 0;
        var largest = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            if (histogram[i] == 0)
                continue;

            distribution[i] = (int)Math.Max(1, ((histogram[i] * (ulong)TableSize) + (total / 2)) / total);
            sum += distribution[i];
            if (distribution[i] > distribution[largest])
            {
                largest = i;
            }
        }

        // The rounding error goes to the most probable tokens, which it changes the least
        while (sum != TableSize)
        {
            var step = sum > TableSize ? -1 : 1;
            var change = Math.Min(Math.Abs(TableSize - sum), Math.Max(1, distribution[largest] / 2));
            if (step < 0 && distribution[largest] - change < 1)
            {
                change = distribution[largest] - 1;
            }

            distribution[largest] += step * change;
            sum += step * change;
            if (sum != TableSize)
            {
                // The largest token cannot absorb everything: continue with the one that is now the largest
                largest = 0;
                for (var i = 1; i < distribution.Length; i++)
                {
                    if (distribution[i] > distribution[largest])
                    {
                        largest = i;
                    }
                }
            }
        }
    }

    /// <summary>Writes a distribution (section D.3.4), exactly: with one or two tokens, or as the logarithm and the lower bits of each probability.</summary>
    internal static void WriteDistribution(LsbBitWriter writer, ReadOnlySpan<int> distribution)
    {
        var used = 0;
        var first = -1;
        var second = -1;
        var last = 0;
        for (var i = 0; i < distribution.Length; i++)
        {
            if (distribution[i] == 0)
                continue;

            used++;
            last = i;
            if (first < 0)
            {
                first = i;
            }
            else if (second < 0)
            {
                second = i;
            }
        }

        if (used <= 2)
        {
            writer.WriteBool(true);
            writer.WriteBool(used == 2);
            WriteVarLenUint8(writer, first);
            if (used == 2)
            {
                WriteVarLenUint8(writer, second);
                writer.WriteBits((uint)distribution[first], JpegXlAnsDistribution.LogTableSize);
            }

            return;
        }

        // Not simple, not flat; the largest precision (13), which stores every bit of a probability
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBits(0b111, 3);
        writer.WriteBits(6, 3);
        var length = last + 1;
        WriteVarLenUint8(writer, length - 3);

        // The logarithm of each probability plus one, 0 for a token that does not occur
        var omitted = -1;
        var omittedLog = -1;
        for (var i = 0; i < length; i++)
        {
            var log = distribution[i] == 0 ? -1 : BitOperations.Log2((uint)distribution[i]);
            WriteLogCount(writer, log + 1);
            if (log > omittedLog)
            {
                omittedLog = log;
                omitted = i;
            }
        }

        // The first of the most probable tokens is not written: it is what remains
        for (var i = 0; i < length; i++)
        {
            if (i == omitted || distribution[i] <= 1)
                continue;

            var log = BitOperations.Log2((uint)distribution[i]);
            writer.WriteBits((uint)(distribution[i] - (1 << log)), log);
        }
    }

    /// <summary>Writes a logarithm plus one (0 to 12) with the fixed prefix code of the format.</summary>
    private static void WriteLogCount(LsbBitWriter writer, int value)
    {
        var (bits, length) = value switch
        {
            0 => (0b10001u, 5),
            1 => (0b1011u, 4),
            2 => (0b1111u, 4),
            3 => (0b0011u, 4),
            4 => (0b1001u, 4),
            5 => (0b0111u, 4),
            6 => (0b100u, 3),
            7 => (0b010u, 3),
            8 => (0b101u, 3),
            9 => (0b110u, 3),
            10 => (0b000u, 3),
            11 => (0b100001u, 6),
            _ => (0b0000001u, 7),
        };

        writer.WriteBits(bits, length);
    }

    private static void WriteVarLenUint8(LsbBitWriter writer, int value)
    {
        if (value == 0)
        {
            writer.WriteBool(false);
            return;
        }

        writer.WriteBool(true);
        var log = BitOperations.Log2((uint)value);
        writer.WriteBits((uint)log, 3);
        writer.WriteBits((uint)(value - (1 << log)), log);
    }

    private (int Token, int BitCount, uint Bits) GetToken(uint value) => GetToken(_config, value);

    /// <summary>Splits a value into its token and its raw bits (the inverse of <see cref="JpegXlHybridUintConfig.ReadValue"/>).</summary>
    internal static (int Token, int BitCount, uint Bits) GetToken(JpegXlHybridUintConfig config, uint value)
    {
        var split = 1u << config.SplitExponent;
        if (value < split)
            return ((int)value, 0, 0);

        var msb = config.MsbInToken;
        var lsb = config.LsbInToken;
        var log = BitOperations.Log2(value);
        var mantissa = value - (1u << log);
        var token = split + (((uint)(log - config.SplitExponent)) << (msb + lsb)) + ((mantissa >> (log - msb)) << lsb) + (mantissa & ((1u << lsb) - 1));
        var bitCount = log - msb - lsb;
        var bits = (mantissa >> lsb) & (uint)((1UL << bitCount) - 1);
        return ((int)token, bitCount, bits);
    }

    internal static void WriteVarLenUint16(LsbBitWriter writer, int value)
    {
        if (value == 0)
        {
            writer.WriteBool(false);
            return;
        }

        writer.WriteBool(true);
        var log = BitOperations.Log2((uint)value);
        writer.WriteBits((uint)log, 4);
        writer.WriteBits((uint)(value - (1 << log)), log);
    }

    /// <summary>Writes the prefix code of an alphabet of at least two symbols (section D.2) and computes its codes.</summary>
    internal static void WritePrefixCode(LsbBitWriter writer, ReadOnlySpan<uint> histogram, Span<byte> lengths, Span<ushort> codes)
    {
        if (histogram.Length > MaxAlphabetSize)
            throw new InvalidOperationException("The alphabet of a prefix code is too large.");

        var used = 0;
        var single = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            if (histogram[i] != 0)
            {
                used++;
                single = i;
            }
        }

        if (used <= 1)
        {
            // A simple code of one symbol, which takes no bit
            writer.WriteBits(1, 2);
            writer.WriteBits(0, 2);
            writer.WriteBits((uint)single, JpegXlHybridUintConfig.CeilingLog2(histogram.Length));
            lengths.Clear();
            codes.Clear();
            return;
        }

        CanonicalHuffmanBuilder.BuildLengths(histogram, JpegXlPrefixCode.MaxLength, lengths);
        CanonicalHuffmanBuilder.BuildCodes(lengths, codes);

        // The lengths are coded up to the last symbol that has a code: the decoder stops when the code is complete
        var last = lengths.Length - 1;
        while (lengths[last] == 0)
        {
            last--;
        }

        Span<uint> lengthHistogram = stackalloc uint[18];
        lengthHistogram.Clear();
        for (var i = 0; i <= last; i++)
        {
            lengthHistogram[lengths[i]]++;
        }

        Span<byte> lengthLengths = stackalloc byte[18];
        Span<ushort> lengthCodes = stackalloc ushort[18];
        CanonicalHuffmanBuilder.BuildLengths(lengthHistogram, 5, lengthLengths);
        CanonicalHuffmanBuilder.BuildCodes(lengthLengths, lengthCodes);
        var lengthSymbolCount = 0;
        foreach (var length in lengthLengths)
        {
            lengthSymbolCount += length != 0 ? 1 : 0;
        }

        // No length of the code of the lengths is skipped; each is written with a fixed code, until that code is complete
        writer.WriteBits(0, 2);
        ReadOnlySpan<byte> order = [1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15];
        var space = 32;
        for (var i = 0; i < order.Length && space > 0; i++)
        {
            var length = lengthLengths[order[i]];
            switch (length)
            {
                case 0:
                    writer.WriteBits(0b00, 2);
                    break;
                case 1:
                    writer.WriteBits(0b0111, 4);
                    break;
                case 2:
                    writer.WriteBits(0b011, 3);
                    break;
                case 3:
                    writer.WriteBits(0b10, 2);
                    break;
                case 4:
                    writer.WriteBits(0b01, 2);
                    break;
                default:
                    writer.WriteBits(0b1111, 4);
                    break;
            }

            if (length != 0)
            {
                space -= 32 >> length;
            }
        }

        // A code of the lengths that has a single symbol takes no bit
        if (lengthSymbolCount > 1)
        {
            for (var i = 0; i <= last; i++)
            {
                writer.WriteBits(lengthCodes[lengths[i]], lengthLengths[lengths[i]]);
            }
        }
    }
}
