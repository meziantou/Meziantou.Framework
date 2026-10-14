namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What the decoder and the encoder of the coding passes share (T.800, Annex D): the flags of a coefficient, the context
/// tables, and the update of the flags when a coefficient becomes significant.
/// </summary>
/// <remarks>
/// Each coefficient of a code-block has a word of flags, in an array with a border of one coefficient on each side: the
/// significance of its eight neighbors, the sign of the four nearest ones, and its own state. A coefficient that becomes
/// significant updates the flags of its neighbors, so that a context is one table lookup.
/// </remarks>
internal static class Jpeg2000Tier1Tables
{
    /// <summary>The sub-band holds horizontally low-pass coefficients: LL and LH.</summary>
    public const int OrientationLowHorizontal = 0;

    /// <summary>The HL sub-band: horizontally high-pass, vertically low-pass.</summary>
    public const int OrientationHighLow = 1;

    /// <summary>The HH sub-band.</summary>
    public const int OrientationHighHigh = 2;

    /// <summary>The largest number of coefficients of a code-block.</summary>
    public const int MaxSamples = 4096;

    /// <summary>The largest number of flags of a code-block with its border: a code-block is at most 1024 coefficients wide or high.</summary>
    public const int MaxFlags = MaxSamples + (2 * (1024 + 4)) + 4;

    public const uint West = 1u << 0;
    public const uint East = 1u << 1;
    public const uint North = 1u << 2;
    public const uint South = 1u << 3;
    public const uint NorthWest = 1u << 4;
    public const uint NorthEast = 1u << 5;
    public const uint SouthWest = 1u << 6;
    public const uint SouthEast = 1u << 7;

    /// <summary>The significance of the eight neighbors.</summary>
    public const uint NeighborMask = 0xFF;

    /// <summary>The position of the four bits that tell whether the west, east, north and south neighbors are negative.</summary>
    public const int NegativeShift = 8;

    /// <summary>The coefficient is significant.</summary>
    public const uint Significant = 1u << 12;

    /// <summary>The coefficient was coded by the significance propagation pass of the current bit-plane.</summary>
    public const uint Visited = 1u << 13;

    /// <summary>The coefficient was refined by a magnitude refinement pass.</summary>
    public const uint Refined = 1u << 14;

    /// <summary>The first of the five sign contexts (Table D.3).</summary>
    public const int ContextSign = 9;

    /// <summary>The first of the three magnitude refinement contexts (Table D.4).</summary>
    public const int ContextMagnitude = 14;

    /// <summary>The run-length context (D.3.4).</summary>
    public const int ContextRunLength = 17;

    /// <summary>The context of the uniform distribution (D.3.4 and D.5).</summary>
    public const int ContextUniform = 18;

    /// <summary>The number of contexts.</summary>
    public const int ContextCount = 19;

    // Table D.1: the context of a significance decision, by the significance of the eight neighbors
    private static readonly byte[] ZeroCodingContexts = CreateZeroCodingContexts();

    // Tables D.2 and D.3: the context of a sign and the bit it is exclusive-ORed with, by the significance and the sign of
    // the four nearest neighbors
    private static readonly byte[] SignContexts = CreateSignContexts();

    /// <summary>Gets the significance contexts of a sub-band (Table D.1), indexed by the significance of the eight neighbors.</summary>
    public static ReadOnlySpan<byte> GetZeroCodingContexts(int orientation) => ZeroCodingContexts.AsSpan(orientation * 256, 256);

    /// <summary>Gets the sign context of a coefficient (Table D.3): the context in the seven low bits, the bit the sign is exclusive-ORed with in the high one.</summary>
    public static byte GetSignContext(uint flags) => SignContexts[(int)((flags & 0x0F) | ((flags >> (NegativeShift - 4)) & 0xF0))];

    /// <summary>Sets the contexts to their initial states (Table D.7): the state 0 with 0 as the more probable symbol, but for three of them.</summary>
    public static void ResetContexts(Span<byte> contexts)
    {
        contexts.Clear();
        contexts[0] = Jpeg2000MqTables.ZeroNeighborState << 1;
        contexts[ContextRunLength] = Jpeg2000MqTables.RunLengthState << 1;
        contexts[ContextUniform] = Jpeg2000MqTables.UniformState << 1;
    }

    /// <summary>Records that a coefficient became significant, in its flags and in those of its neighbors.</summary>
    /// <param name="flags">The flags of the code-block.</param>
    /// <param name="index">The index of the coefficient in <paramref name="flags"/>.</param>
    /// <param name="stride">The width of the code-block with its border.</param>
    /// <param name="negative">Whether the coefficient is negative.</param>
    /// <param name="updateNorth">
    /// <see langword="false"/> for a coefficient of the first row of a stripe when the contexts are vertically causal (D.7):
    /// the coefficients of the stripe above ignore it.
    /// </param>
    public static void SetSignificant(Span<uint> flags, int index, int stride, bool negative, bool updateNorth)
    {
        var sign = negative ? 1u : 0u;
        flags[index] |= Significant;
        flags[index - 1] |= East | (sign << (NegativeShift + 1));
        flags[index + 1] |= West | (sign << NegativeShift);
        flags[index + stride] |= North | (sign << (NegativeShift + 2));
        flags[index + stride - 1] |= NorthEast;
        flags[index + stride + 1] |= NorthWest;
        if (!updateNorth)
            return;

        flags[index - stride] |= South | (sign << (NegativeShift + 3));
        flags[index - stride - 1] |= SouthEast;
        flags[index - stride + 1] |= SouthWest;
    }

    private static byte[] CreateZeroCodingContexts()
    {
        var table = new byte[3 * 256];
        for (var mask = 0u; mask < 256; mask++)
        {
            var horizontal = Count(mask, West) + Count(mask, East);
            var vertical = Count(mask, North) + Count(mask, South);
            var diagonal = Count(mask, NorthWest) + Count(mask, NorthEast) + Count(mask, SouthWest) + Count(mask, SouthEast);
            table[(OrientationLowHorizontal * 256) + mask] = (byte)GetLowPassContext(horizontal, vertical, diagonal);
            table[(OrientationHighLow * 256) + mask] = (byte)GetLowPassContext(vertical, horizontal, diagonal);
            table[(OrientationHighHigh * 256) + mask] = (byte)GetDiagonalContext(horizontal + vertical, diagonal);
        }

        return table;

        static int Count(uint mask, uint bit) => (mask & bit) != 0 ? 1 : 0;

        // The columns "LL and LH sub-bands" of Table D.1; the HL sub-band exchanges the horizontal and vertical neighbors
        static int GetLowPassContext(int horizontal, int vertical, int diagonal) => horizontal switch
        {
            2 => 8,
            1 => vertical >= 1 ? 7 : diagonal >= 1 ? 6 : 5,
            _ => vertical switch
            {
                2 => 4,
                1 => 3,
                _ => diagonal >= 2 ? 2 : diagonal,
            },
        };

        // The column "HH sub-band" of Table D.1
        static int GetDiagonalContext(int nearest, int diagonal) => diagonal switch
        {
            >= 3 => 8,
            2 => nearest >= 1 ? 7 : 6,
            1 => nearest >= 2 ? 5 : nearest == 1 ? 4 : 3,
            _ => nearest >= 2 ? 2 : nearest,
        };
    }

    private static byte[] CreateSignContexts()
    {
        // The index holds the significance of the west, east, north and south neighbors, then their signs
        var table = new byte[256];
        for (var index = 0; index < 256; index++)
        {
            var horizontal = Math.Clamp(Contribution(index, 0) + Contribution(index, 1), -1, 1);
            var vertical = Math.Clamp(Contribution(index, 2) + Contribution(index, 3), -1, 1);

            // Table D.3: the context depends on the magnitudes of the contributions, the exclusive-OR bit on their signs
            var flip = horizontal < 0 || (horizontal == 0 && vertical < 0);
            if (flip)
            {
                horizontal = -horizontal;
                vertical = -vertical;
            }

            var context = horizontal == 1 ? ContextSign + 3 + vertical : ContextSign + (vertical == 0 ? 0 : 1);
            table[index] = (byte)(context | (flip ? 0x80 : 0));
        }

        return table;

        // Table D.2: a significant neighbor contributes 1 when it is positive and -1 when it is negative
        static int Contribution(int index, int neighbor)
        {
            if ((index & (1 << neighbor)) == 0)
                return 0;

            return (index & (1 << (neighbor + 4))) != 0 ? -1 : 1;
        }
    }
}
