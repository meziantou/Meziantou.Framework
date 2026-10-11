using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The distributions of the ANS entropy coder (ISO/IEC 18181-1, section D.3): reading a distribution of 4096 units over an
/// alphabet, and building the alias table that maps a state to a symbol in constant time (section D.3.2).
/// </summary>
internal static class JpegXlAnsDistribution
{
    /// <summary>The base-2 logarithm of the sum of a distribution.</summary>
    public const int LogTableSize = 12;

    /// <summary>The sum of a distribution.</summary>
    public const int TableSize = 1 << LogTableSize;

    /// <summary>The initial and final state of an ANS stream.</summary>
    public const uint InitialState = 0x130000;

    /// <summary>One bucket of an alias table.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AliasEntry
    {
        /// <summary>The number of positions of the bucket that map to the symbol of the same index.</summary>
        public ushort Cutoff;

        /// <summary>The symbol of the other positions.</summary>
        public ushort Symbol;

        /// <summary>The offset added to the position, for the other positions.</summary>
        public ushort Offset;

        /// <summary>The probability of the symbol of the same index.</summary>
        public ushort Frequency;

        /// <summary>The probability of <see cref="Symbol"/>.</summary>
        public ushort SymbolFrequency;
    }

    /// <summary>Reads a distribution.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="distribution">Receives the probability of each symbol, in units of 1/4096; its length is the alphabet size allowed by the code (32 to 256).</param>
    public static void Read(JpegXlBitReader reader, Span<int> distribution)
    {
        distribution.Clear();
        if (reader.ReadBool())
        {
            // One symbol, or two symbols and the probability of the first one
            var count = (int)reader.ReadBits(1) + 1;
            var first = ReadVarLenUint8(reader);
            if (count == 1)
            {
                EnsureSymbol(first, distribution.Length);
                distribution[first] = TableSize;
                return;
            }

            var second = ReadVarLenUint8(reader);
            EnsureSymbol(first, distribution.Length);
            EnsureSymbol(second, distribution.Length);
            if (first == second)
                throw JpegXlFormat.Invalid("an ANS distribution names a symbol twice.");

            distribution[first] = (int)reader.ReadBits(LogTableSize);
            distribution[second] = TableSize - distribution[first];
            return;
        }

        if (reader.ReadBool())
        {
            // A flat distribution over the first symbols
            var alphabetSize = ReadVarLenUint8(reader) + 1;
            if (alphabetSize > distribution.Length)
                throw JpegXlFormat.Invalid("an ANS distribution is larger than its alphabet.");

            var value = TableSize / alphabetSize;
            var rest = TableSize % alphabetSize;
            for (var i = 0; i < alphabetSize; i++)
            {
                distribution[i] = value + (i < rest ? 1 : 0);
            }

            return;
        }

        // The base-2 logarithm of each probability, with a prefix code, then the bits below the leading one
        var log = 0;
        while (log < 3 && reader.ReadBool())
        {
            log++;
        }

        var shift = (int)((reader.ReadBits(log) | (1u << log)) - 1);
        if (shift > LogTableSize + 1)
            throw JpegXlFormat.Invalid("the precision of an ANS distribution is not valid.");

        var length = ReadVarLenUint8(reader) + 3;
        if (length > distribution.Length)
            throw JpegXlFormat.Invalid("an ANS distribution is larger than its alphabet.");

        Span<sbyte> logCounts = stackalloc sbyte[length];
        Span<short> same = stackalloc short[length];
        same.Clear();
        var omitLog = -1;
        var omitPosition = -1;
        for (var i = 0; i < length; i++)
        {
            var logCount = ReadLogCount(reader) - 1;
            logCounts[i] = (sbyte)logCount;
            if (logCount == LogTableSize)
            {
                // A run: the previous probability is repeated
                var runLength = ReadVarLenUint8(reader);
                same[i] = (short)(runLength + 5);
                i += runLength + 3;
                continue;
            }

            if (logCount > omitLog)
            {
                omitLog = logCount;
                omitPosition = i;
            }
        }

        // The first of the largest probabilities is not coded: it is what remains
        if (omitPosition < 0 || (omitPosition + 1 < length && logCounts[omitPosition + 1] == LogTableSize))
            throw JpegXlFormat.Invalid("an ANS distribution has no implicit probability.");

        var total = 0;
        var previous = 0;
        var sameCount = 0;
        for (var i = 0; i < length; i++)
        {
            if (same[i] != 0)
            {
                sameCount = same[i] - 1;
                previous = i > 0 ? distribution[i - 1] : 0;
            }

            if (sameCount > 0)
            {
                distribution[i] = previous;
                sameCount--;
            }
            else
            {
                var code = (int)logCounts[i];
                if (i == omitPosition || code < 0)
                    continue;

                if (shift == 0 || code == 0)
                {
                    distribution[i] = 1 << code;
                }
                else
                {
                    var bitCount = Math.Max(0, Math.Min(code, shift - ((LogTableSize - code) >> 1)));
                    distribution[i] = (1 << code) + ((int)reader.ReadBits(bitCount) << (code - bitCount));
                }
            }

            total += distribution[i];
        }

        distribution[omitPosition] = TableSize - total;
        if (distribution[omitPosition] <= 0)
            throw JpegXlFormat.Invalid("the probabilities of an ANS distribution exceed their sum.");
    }

    /// <summary>Builds the alias table of a distribution.</summary>
    /// <param name="distribution">The probability of each symbol; the length is the number of buckets, a power of two.</param>
    /// <param name="table">Receives one entry per bucket.</param>
    public static void BuildAliasTable(ReadOnlySpan<int> distribution, Span<AliasEntry> table)
    {
        var tableSize = distribution.Length;
        var bucketSize = TableSize / tableSize;
        for (var i = 0; i < tableSize; i++)
        {
            if (distribution[i] == TableSize)
            {
                // A single symbol: every position maps to it, and the state does not change
                for (var j = 0; j < tableSize; j++)
                {
                    table[j] = new AliasEntry { Cutoff = 0, Symbol = (ushort)i, Offset = (ushort)(bucketSize * j), Frequency = 0, SymbolFrequency = TableSize };
                }

                return;
            }
        }

        // Buckets that hold more than their size give their excess to the ones that hold less, most recently found first
        Span<int> cutoffs = stackalloc int[tableSize];
        Span<short> underfull = stackalloc short[tableSize];
        Span<short> overfull = stackalloc short[tableSize];
        var underfullCount = 0;
        var overfullCount = 0;
        for (var i = 0; i < tableSize; i++)
        {
            cutoffs[i] = distribution[i];
            if (cutoffs[i] > bucketSize)
            {
                overfull[overfullCount++] = (short)i;
            }
            else if (cutoffs[i] < bucketSize)
            {
                underfull[underfullCount++] = (short)i;
            }
        }

        table.Clear();
        while (overfullCount > 0)
        {
            var over = overfull[--overfullCount];
            if (underfullCount == 0)
                throw JpegXlFormat.Invalid("an ANS distribution does not sum to 4096.");

            var under = underfull[--underfullCount];
            var missing = bucketSize - cutoffs[under];
            cutoffs[over] -= missing;
            table[under].Symbol = (ushort)over;
            table[under].Offset = (ushort)cutoffs[over];
            if (cutoffs[over] < bucketSize)
            {
                underfull[underfullCount++] = over;
            }
            else if (cutoffs[over] > bucketSize)
            {
                overfull[overfullCount++] = over;
            }
        }

        for (var i = 0; i < tableSize; i++)
        {
            if (cutoffs[i] == bucketSize)
            {
                table[i].Symbol = (ushort)i;
                table[i].Offset = 0;
                table[i].Cutoff = 0;
            }
            else
            {
                table[i].Offset = (ushort)(table[i].Offset - cutoffs[i]);
                table[i].Cutoff = (ushort)cutoffs[i];
            }

            table[i].Frequency = (ushort)distribution[i];
            table[i].SymbolFrequency = (ushort)distribution[table[i].Symbol];
        }
    }

    private static void EnsureSymbol(int symbol, int alphabetSize)
    {
        if (symbol >= alphabetSize)
            throw JpegXlFormat.Invalid("an ANS distribution names a symbol outside its alphabet.");
    }

    private static int ReadVarLenUint8(JpegXlBitReader reader)
    {
        if (!reader.ReadBool())
            return 0;

        var count = (int)reader.ReadBits(3);
        return (int)reader.ReadBits(count) + (1 << count);
    }

    /// <summary>Reads a logarithm plus one with its fixed prefix code: values 0 to 13, 13 announcing a run.</summary>
    private static int ReadLogCount(JpegXlBitReader reader)
    {
        var bits = reader.PeekBits(7);
        int value;
        int length;
        switch (bits & 7)
        {
            case 0:
                (value, length) = (10, 3);
                break;
            case 2:
                (value, length) = (7, 3);
                break;
            case 4:
                (value, length) = (6, 3);
                break;
            case 5:
                (value, length) = (8, 3);
                break;
            case 6:
                (value, length) = (9, 3);
                break;
            case 3:
                (value, length) = (bits & 8) == 0 ? (3, 4) : (1, 4);
                break;
            case 7:
                (value, length) = (bits & 8) == 0 ? (5, 4) : (2, 4);
                break;
            default:
                if ((bits & 8) != 0)
                {
                    (value, length) = (4, 4);
                }
                else if ((bits & 16) != 0)
                {
                    (value, length) = (0, 5);
                }
                else if ((bits & 32) != 0)
                {
                    (value, length) = (11, 6);
                }
                else
                {
                    (value, length) = (bits & 64) == 0 ? (12, 7) : (13, 7);
                }

                break;
        }

        reader.Consume(length);
        return value;
    }
}
