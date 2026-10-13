namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The quantized coefficients of a JPEG frame: for each component, 64 values per block, in the order of the rows of the
/// block (not in zig-zag order), for every block of the component, the ones that only pad the last minimum coded units
/// included.
/// </summary>
internal sealed class JpegCoefficientStore : IDisposable
{
    private readonly Av1PooledArray<short>?[] _coefficients;
    private readonly int[] _blocksWide;
    private readonly int[] _blocksHigh;

    /// <param name="blocksWide">The number of blocks of a row of each component.</param>
    /// <param name="blocksHigh">The number of rows of blocks of each component.</param>
    /// <param name="scope">The scope the coefficients are rented from.</param>
    public JpegCoefficientStore(ReadOnlySpan<int> blocksWide, ReadOnlySpan<int> blocksHigh, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _blocksWide = blocksWide.ToArray();
        _blocksHigh = blocksHigh.ToArray();
        _coefficients = new Av1PooledArray<short>?[blocksWide.Length];
        try
        {
            for (var c = 0; c < _coefficients.Length; c++)
            {
                _coefficients[c] = Av1PooledArray<short>.Rent(scope, (long)blocksWide[c] * blocksHigh[c] * 64, clear: true);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Gets the number of components.</summary>
    public int ComponentCount => _coefficients.Length;

    /// <summary>Gets the number of blocks of a row of a component.</summary>
    public int GetBlocksWide(int component) => _blocksWide[component];

    /// <summary>Gets the number of rows of blocks of a component.</summary>
    public int GetBlocksHigh(int component) => _blocksHigh[component];

    /// <summary>Gets the 64 coefficients of a block.</summary>
    public Span<short> GetBlock(int component, int x, int y) => _coefficients[component]!.Span.Slice(((y * _blocksWide[component]) + x) * 64, 64);

    public void Dispose()
    {
        for (var c = 0; c < _coefficients.Length; c++)
        {
            _coefficients[c]?.Dispose();
            _coefficients[c] = null;
        }
    }
}
