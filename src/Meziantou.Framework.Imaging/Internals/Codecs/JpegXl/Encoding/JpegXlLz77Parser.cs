using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Finds the copies of previous values (ISO/IEC 18181-1, section D.3.7) in the stream of a Modular sub-bitstream: at each
/// value, the longest run that starts at one of the previous positions of the same three values (a hash chain), or at the
/// value on the left or above. A copy is kept when its length and its distance cost fewer bits than the values it replaces,
/// each with the cost of its token in its context; the costs come from the counts of the stream without copies.
/// </summary>
/// <remarks>
/// A distance is written as one of the 120 short distances of a two-dimensional stream (an offset in columns and rows, the
/// rows being <c>distanceMultiplier</c> values long) when it is one of them, otherwise plus 119. The search only depends on
/// the values, so the copies of a stream are the same each time it is parsed.
/// </remarks>
internal sealed class JpegXlLz77Parser
{
    /// <summary>The number of values the decoder keeps: a copy reads no further back.</summary>
    public const int WindowSize = 1 << 20;

    private const int SpecialDistanceCount = 120;
    private const int HashBits = 16;
    private const int MaxLength = 1 << 16;

    /// <summary>The fewest bits of a copy (<see cref="GetCopyCost"/> of the shortest copy at distance 1).</summary>
    private const double MinCopyCost = 7;

    /// <summary>The length above which a match that is not worth a copy stops the search over the values it covers.</summary>
    private const int MinSkippedLength = 32;

    // The offsets (in columns then in rows) of the short distances, as (column + 7) * 16 + row, in the order of their codes
    private static ReadOnlySpan<byte> SpecialDistances =>
    [
        0x71, 0x80, 0x81, 0x61, 0x72, 0x90, 0x82, 0x62, 0x91, 0x51, 0x92, 0x52,
        0x73, 0xa0, 0x83, 0x63, 0xa1, 0x41, 0x93, 0x53, 0xa2, 0x42, 0x74, 0xb0,
        0x84, 0x64, 0xb1, 0x31, 0xa3, 0x43, 0x94, 0x54, 0xb2, 0x32, 0x75, 0xa4,
        0x44, 0xb3, 0x33, 0xc0, 0x85, 0x65, 0xc1, 0x21, 0x95, 0x55, 0xc2, 0x22,
        0xb4, 0x34, 0xa5, 0x45, 0xc3, 0x23, 0x76, 0xd0, 0x86, 0x66, 0xd1, 0x11,
        0x96, 0x56, 0xd2, 0x12, 0xb5, 0x35, 0xc4, 0x24, 0xa6, 0x46, 0xd3, 0x13,
        0x77, 0xe0, 0x87, 0x67, 0xc5, 0x25, 0xe1, 0x01, 0xb6, 0x36, 0xd4, 0x14,
        0x97, 0x57, 0xe2, 0x02, 0xa7, 0x47, 0xe3, 0x03, 0xc6, 0x26, 0xd5, 0x15,
        0xf0, 0xb7, 0x37, 0xe4, 0x04, 0xf1, 0xf2, 0xd6, 0x16, 0xf3, 0xc7, 0x27,
        0xe5, 0x05, 0xf4, 0xd7, 0x17, 0xe6, 0x06, 0xf5, 0xe7, 0x07, 0xf6, 0xf7,
    ];

    private readonly float[] _tokenCosts;
    private readonly int _maxChain;
    private int[] _heads = [];
    private int[] _previous = [];
    private float[] _prefix = [];

    /// <summary>Creates a parser.</summary>
    /// <param name="tokenCosts">For each context, the cost in bits of each token (hybrid integers 4, 1, 0) without its raw bits.</param>
    /// <param name="maxChain">The largest number of previous positions tried at each value.</param>
    public JpegXlLz77Parser(float[] tokenCosts, int maxChain)
    {
        _tokenCosts = tokenCosts;
        _maxChain = maxChain;
    }

    /// <summary>Receives the values and the copies of a stream.</summary>
    internal interface IStreamSink
    {
        void AddValue(int context, uint value);

        void AddCopy(int context, int length, uint distance);
    }

    /// <summary>Computes, for each context, the cost of each token from the counts of the values of a stream without copies.</summary>
    public static float[] GetTokenCosts(JpegXlModularEntropyCode.Counter counter, int contextCount)
    {
        var costs = new float[contextCount * JpegXlTreeLearner.TokenCount];
        Span<long> tokens = stackalloc long[JpegXlTreeLearner.TokenCount];
        for (var context = 0; context < contextCount; context++)
        {
            tokens.Clear();
            long total = 0;
            var fine = counter.GetFine(context);
            for (var fineToken = 0; fineToken < fine.Length; fineToken++)
            {
                if (fine[fineToken] == 0)
                    continue;

                tokens[JpegXlTreeLearner.GetToken(JpegXlModularEntropyCode.GetFineValue(fineToken))] += fine[fineToken];
                total += fine[fineToken];
            }

            for (var token = 0; token < tokens.Length; token++)
            {
                // A token that the context does not have costs as much as one seen once in twice its values
                costs[(context * JpegXlTreeLearner.TokenCount) + token] = tokens[token] == 0
                    ? (float)Math.Log2(2.0 * Math.Max(1, total))
                    : (float)Math.Log2((double)total / tokens[token]);
            }
        }

        return costs;
    }

    /// <summary>Parses the values of a stream (<c>context &lt;&lt; 32 | value</c>) into values and copies.</summary>
    /// <param name="values">The values of the stream.</param>
    /// <param name="distanceMultiplier">The length of a row: the largest width of the channels of the stream.</param>
    /// <param name="sink">Receives the values and the copies, in order.</param>
    public void Parse<TSink>(ReadOnlySpan<ulong> values, int distanceMultiplier, ref TSink sink)
        where TSink : struct, IStreamSink
    {
        var count = values.Length;
        if (_heads.Length == 0)
        {
            _heads = new int[1 << HashBits];
        }

        _heads.AsSpan().Fill(-1);
        if (_previous.Length < count)
        {
            _previous = new int[count];
            _prefix = new float[count + 1];
        }

        // The cost of the values written as they are, summed
        var prefix = _prefix.AsSpan(0, count + 1);
        prefix[0] = 0;
        for (var i = 0; i < count; i++)
        {
            prefix[i + 1] = prefix[i] + GetValueCost(values[i]);
        }

        // The codes of the short distances, the shortest code first
        var specials = new Dictionary<int, int>();
        for (var code = SpecialDistanceCount - 1; code >= 0; code--)
        {
            var special = SpecialDistances[code];
            var offset = Math.Max(1, (long)((special >> 4) - 7) + ((long)distanceMultiplier * (special & 7)));
            if (offset <= int.MaxValue)
            {
                specials[(int)offset] = code;
            }
        }

        var position = 0;
        var searchFrom = 0;
        while (position < count)
        {
            // No copy is tried where even the longest one would cost more than the values it replaces: a long run of values
            // that are almost free (a constant area) is written as it is without measuring every match
            var best = default(Match);
            var reach = Math.Min(count, position + MaxLength);
            var longest = 0;
            if (position >= searchFrom && position + JpegXlModularEntropyCode.Lz77MinLength <= count && prefix[reach] - prefix[position] > MinCopyCost)
            {
                // The value on the left and the one above, then the previous positions of the same three values
                best = Try(values, prefix, position, 1, specials, best, ref longest);
                if (distanceMultiplier > 1)
                {
                    best = Try(values, prefix, position, distanceMultiplier, specials, best, ref longest);
                }

                var candidate = _heads[Hash(values, position)];
                for (var chain = 0; chain < _maxChain && candidate >= 0; chain++)
                {
                    var distance = position - candidate;
                    if (distance > WindowSize)
                        break;

                    if (distance != 1 && distance != distanceMultiplier)
                    {
                        best = Try(values, prefix, position, distance, specials, best, ref longest);
                    }

                    candidate = _previous[candidate];
                }
            }

            if (best.Length >= JpegXlModularEntropyCode.Lz77MinLength)
            {
                sink.AddCopy((int)(values[position] >> 32), best.Length, GetDistanceCode(best.Distance, specials));
                var end = position + best.Length;
                for (; position < end; position++)
                {
                    Insert(values, position);
                }

                continue;
            }

            // A long match that was not worth a copy is a run of values that are almost free: the values it covers are written
            // as they are without a search, which would find the same match shortened
            if (longest > MinSkippedLength)
            {
                searchFrom = position + longest - JpegXlModularEntropyCode.Lz77MinLength;
            }

            sink.AddValue((int)(values[position] >> 32), (uint)values[position]);
            Insert(values, position);
            position++;
        }
    }

    /// <summary>The copy of the values at <paramref name="distance"/> when it saves more bits than <paramref name="best"/>.</summary>
    private static Match Try(ReadOnlySpan<ulong> values, ReadOnlySpan<float> prefix, int position, int distance, Dictionary<int, int> specials, Match best, ref int longest)
    {
        if (distance > position)
            return best;

        // The values of a copy are those of the stream: the contexts do not matter
        var source = position - distance;
        var limit = Math.Min(values.Length - position, MaxLength);
        var length = 0;
        while (length < limit && (uint)values[source + length] == (uint)values[position + length])
        {
            length++;
        }

        longest = Math.Max(longest, length);
        if (length < JpegXlModularEntropyCode.Lz77MinLength || length <= best.Length)
            return best;

        var gain = prefix[position + length] - prefix[position] - GetCopyCost(length, GetDistanceCode(distance, specials));
        return gain > best.Gain ? new Match(length, distance, gain) : best;
    }

    private static uint GetDistanceCode(int distance, Dictionary<int, int> specials) => specials.TryGetValue(distance, out var code) ? (uint)code : (uint)(distance + SpecialDistanceCount - 1);

    private void Insert(ReadOnlySpan<ulong> values, int index)
    {
        if (index + JpegXlModularEntropyCode.Lz77MinLength > values.Length)
            return;

        var hash = Hash(values, index);
        _previous[index] = _heads[hash];
        _heads[hash] = index;
    }

    private static int Hash(ReadOnlySpan<ulong> values, int position)
    {
        var hash = ((uint)values[position] * 0x9E3779B1u) ^ ((uint)values[position + 1] * 0x85EBCA77u) ^ ((uint)values[position + 2] * 0xC2B2AE3Du);
        return (int)(hash >> (32 - HashBits));
    }

    private float GetValueCost(ulong value)
    {
        var token = JpegXlTreeLearner.GetToken((uint)value);
        var context = (int)(value >> 32);
        return _tokenCosts[(context * JpegXlTreeLearner.TokenCount) + token] + JpegXlTreeLearner.GetRawBitCount(token);
    }

    /// <summary>An estimate of the bits of a copy: the token and the raw bits of its length and of its distance.</summary>
    private static double GetCopyCost(int length, uint distanceCode)
    {
        var lengthBits = BitOperations.Log2((uint)(length - JpegXlModularEntropyCode.Lz77MinLength + 1));
        var distanceBits = BitOperations.Log2(distanceCode + 1);
        return 3 + lengthBits + 4 + (1.5 * distanceBits);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct Match(int Length, int Distance, double Gain);
}
