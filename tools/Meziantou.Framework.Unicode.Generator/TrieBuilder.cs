#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using System.Diagnostics.CodeAnalysis;

/// <summary>A three-stage table over the code points U+0000..U+10FFFF (The Unicode Standard, section 5.1).</summary>
/// <param name="Shift1">The number of low bits of a code point that the first stage ignores.</param>
/// <param name="Shift2">The number of low bits of a code point that select a value inside a block of the third stage.</param>
/// <param name="Stage1">For each group of <c>1 &lt;&lt; Shift1</c> code points, the index of a block of the second stage.</param>
/// <param name="Stage2">Blocks of <c>1 &lt;&lt; (Shift1 - Shift2)</c> indexes of blocks of the third stage.</param>
/// <param name="Stage3">Blocks of <c>1 &lt;&lt; Shift2</c> values.</param>
internal sealed record Trie(int Shift1, int Shift2, ushort[] Stage1, ushort[] Stage2, byte[] Stage3)
{
    public const int CodePointCount = 0x110000;

    public int ByteCount => (Stage1.Length * 2) + (Stage2.Length * 2) + Stage3.Length;

    public byte Lookup(int codePoint)
    {
        var block2 = Stage1[codePoint >> Shift1];
        var block3 = Stage2[(block2 << (Shift1 - Shift2)) + ((codePoint >> Shift2) & ((1 << (Shift1 - Shift2)) - 1))];
        return Stage3[(block3 << Shift2) + (codePoint & ((1 << Shift2) - 1))];
    }
}

internal static class TrieBuilder
{
    /// <summary>Builds the smallest table among the block sizes tried, and checks it against the values.</summary>
    public static Trie Build(byte[] values)
    {
        if (values.Length != Trie.CodePointCount)
            throw new ArgumentException("One value per code point is expected", nameof(values));

        Trie? best = null;
        for (var shift2 = 3; shift2 <= 8; shift2++)
        {
            for (var bits2 = 2; bits2 <= 9; bits2++)
            {
                var shift1 = shift2 + bits2;
                if (shift1 > 16)
                    continue;

                var candidate = Build(values, shift1, shift2);
                if (candidate is not null && (best is null || candidate.ByteCount < best.ByteCount))
                {
                    best = candidate;
                }
            }
        }

        if (best is null)
            throw new InvalidOperationException("No block size fits the table");

        for (var codePoint = 0; codePoint < values.Length; codePoint++)
        {
            if (best.Lookup(codePoint) != values[codePoint])
                throw new InvalidOperationException($"The table disagrees with its source at U+{codePoint:X4}");
        }

        return best;
    }

    private static Trie? Build(byte[] values, int shift1, int shift2)
    {
        var block3Size = 1 << shift2;
        var block2Size = 1 << (shift1 - shift2);

        var stage3 = new List<byte>();
        var block3Indexes = new Dictionary<BlockKey<byte>, int>();
        var stage2Entries = new ushort[values.Length >> shift2];
        for (var block = 0; block < stage2Entries.Length; block++)
        {
            var key = new BlockKey<byte>(values, block * block3Size, block3Size);
            if (!block3Indexes.TryGetValue(key, out var index))
            {
                index = block3Indexes.Count;
                block3Indexes.Add(key, index);
                stage3.AddRange(values.AsSpan(block * block3Size, block3Size));
            }

            if (index > ushort.MaxValue)
                return null;

            stage2Entries[block] = (ushort)index;
        }

        var stage2 = new List<ushort>();
        var block2Indexes = new Dictionary<BlockKey<ushort>, int>();
        var stage1 = new ushort[stage2Entries.Length / block2Size];
        if (stage1.Length * block2Size != stage2Entries.Length)
            return null;

        for (var block = 0; block < stage1.Length; block++)
        {
            var key = new BlockKey<ushort>(stage2Entries, block * block2Size, block2Size);
            if (!block2Indexes.TryGetValue(key, out var index))
            {
                index = block2Indexes.Count;
                block2Indexes.Add(key, index);
                stage2.AddRange(stage2Entries.AsSpan(block * block2Size, block2Size));
            }

            if (index > ushort.MaxValue)
                return null;

            stage1[block] = (ushort)index;
        }

        return new Trie(shift1, shift2, stage1, [.. stage2], [.. stage3]);
    }

    /// <summary>Formats numbers as the elements of a collection expression, wrapped to keep the lines short.</summary>
    public static string FormatElements<T>(ReadOnlySpan<T> values, string indentation)
        where T : unmanaged, IFormattable
    {
        var result = new StringBuilder();
        var lineLength = 0;
        foreach (var value in values)
        {
            var text = value.ToString(format: null, CultureInfo.InvariantCulture);
            if (lineLength == 0 || lineLength + text.Length + 1 > 120)
            {
                result.Append('\n').Append(indentation);
                lineLength = indentation.Length;
            }

            result.Append(text).Append(',');
            lineLength += text.Length + 1;
        }

        return result.ToString();
    }

    private readonly struct BlockKey<T> : IEquatable<BlockKey<T>>
        where T : unmanaged, IEquatable<T>
    {
        private readonly T[] _values;
        private readonly int _start;
        private readonly int _length;

        public BlockKey(T[] values, int start, int length)
        {
            _values = values;
            _start = start;
            _length = length;
        }

        private ReadOnlySpan<T> Span => _values.AsSpan(_start, _length);

        public bool Equals(BlockKey<T> other) => Span.SequenceEqual(other.Span);

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is BlockKey<T> other && Equals(other);

        public override int GetHashCode()
        {
            var hash = default(HashCode);
            foreach (var value in Span)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }
}
