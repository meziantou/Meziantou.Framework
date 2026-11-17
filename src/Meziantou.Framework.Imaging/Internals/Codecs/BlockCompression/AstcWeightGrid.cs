namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The bilinear infill of an ASTC weight grid over a footprint (section 23.17), as the decoder computes it
/// (<see cref="AstcBlockDecoder.GetInfill"/>), and its transpose: the texels each grid weight contributes to.
/// </summary>
internal sealed class AstcWeightGrid
{
    public AstcWeightGrid(int blockWidth, int blockHeight, int gridWidth, int gridHeight)
    {
        Width = gridWidth;
        Height = gridHeight;
        Size = gridWidth * gridHeight;
        var count = blockWidth * blockHeight;
        Indices = new byte[count * 4];
        Factors = new byte[count * 4];
        Starts = new short[Size + 1];
        var ds = AstcBlockDecoder.GetInfillScale(blockWidth);
        var dt = AstcBlockDecoder.GetInfillScale(blockHeight);
        for (var y = 0; y < blockHeight; y++)
        {
            for (var x = 0; x < blockWidth; x++)
            {
                var t = x + (y * blockWidth);
                var (v0, w00, w01, w10, w11) = AstcBlockDecoder.GetInfill(x, y, ds, dt, gridWidth, gridHeight);
                Set((t * 4) + 0, v0, w00);
                Set((t * 4) + 1, v0 + 1, w01);
                Set((t * 4) + 2, v0 + gridWidth, w10);
                Set((t * 4) + 3, v0 + gridWidth + 1, w11);
            }
        }

        // Counting sort of the entries by grid weight
        for (var g = 0; g < Size; g++)
        {
            Starts[g + 1] += Starts[g];
        }

        Entries = new short[Starts[Size]];
        var fill = new int[Size];
        for (var g = 0; g < Size; g++)
        {
            fill[g] = Starts[g];
        }

        for (var entry = 0; entry < count * 4; entry++)
        {
            if (Factors[entry] != 0)
            {
                Entries[fill[Indices[entry]]++] = (short)entry;
            }
        }

        void Set(int entry, int index, int factor)
        {
            // A grid position outside the grid only happens with a factor of 0 (section 23.17)
            if (factor == 0 || index >= Size)
                return;

            Indices[entry] = (byte)index;
            Factors[entry] = (byte)factor;
            Starts[index + 1]++;
        }
    }

    /// <summary>Gets the width of the grid.</summary>
    public int Width { get; }

    /// <summary>Gets the height of the grid.</summary>
    public int Height { get; }

    /// <summary>Gets the number of weights of a plane.</summary>
    public int Size { get; }

    /// <summary>Gets, for each texel, the 4 grid weights the decoder sums (texel * 4 + k).</summary>
    public byte[] Indices { get; }

    /// <summary>Gets, for each texel, the factors (in 1/16) of its 4 grid weights; 0 for the unused ones.</summary>
    public byte[] Factors { get; }

    /// <summary>Gets, for each grid weight g, the start of its entries: <c>Entries[Starts[g]..Starts[g + 1]]</c>.</summary>
    public short[] Starts { get; }

    /// <summary>Gets the entries (texel * 4 + k) whose factor is not 0, grouped by grid weight.</summary>
    public short[] Entries { get; }
}
