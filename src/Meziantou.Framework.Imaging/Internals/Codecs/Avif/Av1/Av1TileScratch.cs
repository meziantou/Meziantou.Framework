namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The working buffers of the block decoding process of a tile: the left context of a superblock row, the coefficients and
/// the residual of a transform block, the color maps of a palette block and the flags of the blocks decoded in a
/// superblock, of a size that does not depend on the frame, and the above context of the tiles
/// (<see cref="PrepareAboveContext"/>), one entry per 4x4 column of the frame. Rented once by the decoder and reused by
/// every tile; each worker that decodes tiles at the same time as the others has its own.
/// </summary>
internal sealed class Av1TileScratch : IDisposable
{
    /// <summary>The number of entries that an above context line has beyond the frame, for the blocks that cross its right edge.</summary>
    public const int AboveContextMargin = 32;

    /// <summary>The number of 4x4 blocks of a side of the largest superblock.</summary>
    public const int SuperblockSize4 = 32;

    /// <summary>The number of entries of a row of <see cref="GetBlockDecoded"/>: columns -1 to 32.</summary>
    public const int BlockDecodedStride = SuperblockSize4 + 2;

    /// <summary>The number of columns to the right and of rows below the coefficients in <see cref="LevelMap"/>.</summary>
    public const int LevelMapPadding = 4;

    private const int MaxPaletteBlockSize = 64;

    private readonly Av1PooledArray<int> _coefficients;
    private readonly Av1PooledArray<byte> _bytes;
    private readonly Av1PooledArray<ushort> _palette;
    private readonly Av1PooledArray<byte>?[] _aboveLevelContext = new Av1PooledArray<byte>?[3];
    private readonly Av1PooledArray<byte>?[] _aboveDcContext = new Av1PooledArray<byte>?[3];
    private Av1PooledArray<ushort>? _abovePaletteColors;
    private Av1PooledArray<byte>? _aboveSegPredContext;
    private int _aboveMiCols = -1;

    public Av1TileScratch(AllocationScope scope)
    {
        _coefficients = Av1PooledArray<int>.Rent(scope, QuantLength + TransformLength + CflLength);
        try
        {
            _bytes = Av1PooledArray<byte>.Rent(scope, (2 * ColorMapLength) + (3 * BlockDecodedLength) + (6 * SuperblockSize4) + SuperblockSize4 + TxTypesLength + LevelMapLength);
            _palette = Av1PooledArray<ushort>.Rent(scope, (2 * SuperblockSize4 * Av1Constants.PaletteColors) + (3 * Av1Constants.PaletteColors));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Gets or sets the coding tools used by the blocks coded with this scratch so far: a diagnostic.</summary>
    public Av1Tools Tools { get; set; }

    private static int QuantLength => 1024;

    private static int TransformLength => 1024 + 4096;

    private static int CflLength => 1024;

    private static int ColorMapLength => MaxPaletteBlockSize * MaxPaletteBlockSize;

    private static int BlockDecodedLength => BlockDecodedStride * BlockDecodedStride;

    private static int TxTypesLength => SuperblockSize4 * SuperblockSize4;

    private static int LevelMapLength => (32 + LevelMapPadding) * (32 + LevelMapPadding);

    /// <summary>Gets <c>Quant</c>: the coefficients of the transform block being read, Min(32, width) per row.</summary>
    public Span<int> Quant => _coefficients.Span[..QuantLength];

    /// <summary>Gets the scratch of <see cref="Av1Reconstruction.Reconstruct"/>.</summary>
    public Span<int> Transform => _coefficients.Span.Slice(QuantLength, TransformLength);

    /// <summary>Gets the scratch of <see cref="Av1Reconstruction.PredictChromaFromLuma"/>.</summary>
    public Span<int> Cfl => _coefficients.Span.Slice(QuantLength + TransformLength, CflLength);

    /// <summary>Gets <c>ColorMapY</c> (<paramref name="plane"/> 0) or <c>ColorMapUV</c> (<paramref name="plane"/> 1): 64 entries per row.</summary>
    public Span<byte> GetColorMap(int plane) => _bytes.Span.Slice(plane * ColorMapLength, ColorMapLength);

    /// <summary>
    /// Gets <c>BlockDecoded[ plane ]</c> for the superblock being decoded: rows and columns -1 to 32, stored from index 0
    /// with <see cref="BlockDecodedStride"/> entries per row.
    /// </summary>
    public Span<byte> GetBlockDecoded(int plane) => _bytes.Span.Slice((2 * ColorMapLength) + (plane * BlockDecodedLength), BlockDecodedLength);

    /// <summary>Gets <c>LeftLevelContext[ plane ]</c> for the superblock row being decoded, indexed by the 4x4 row of the plane modulo 32.</summary>
    public Span<byte> GetLeftLevelContext(int plane) => _bytes.Span.Slice((2 * ColorMapLength) + (3 * BlockDecodedLength) + (plane * SuperblockSize4), SuperblockSize4);

    /// <summary>Gets <c>LeftDcContext[ plane ]</c> for the superblock row being decoded.</summary>
    public Span<byte> GetLeftDcContext(int plane) => _bytes.Span.Slice((2 * ColorMapLength) + (3 * BlockDecodedLength) + ((3 + plane) * SuperblockSize4), SuperblockSize4);

    /// <summary>Gets <c>LeftSegPredContext</c> for the superblock row being decoded.</summary>
    public Span<byte> LeftSegPredContext => _bytes.Span.Slice((2 * ColorMapLength) + (3 * BlockDecodedLength) + (6 * SuperblockSize4), SuperblockSize4);

    /// <summary>Gets <c>TxTypes</c> for the superblock being decoded: 32 entries per row of 4x4 luma blocks.</summary>
    public Span<byte> TxTypes => _bytes.Span.Slice((2 * ColorMapLength) + (3 * BlockDecodedLength) + (7 * SuperblockSize4), TxTypesLength);

    /// <summary>
    /// Gets the levels of the transform block being read for the contexts of <c>coeff_base</c> and <c>coeff_br</c>: rows of
    /// Min(32, width) + <see cref="LevelMapPadding"/> entries, followed by <see cref="LevelMapPadding"/> rows. A block clears
    /// its map before its coefficients are read: the neighbors of a coefficient are read without testing whether they are
    /// in the block.
    /// </summary>
    public Span<byte> LevelMap => _bytes.Span.Slice((2 * ColorMapLength) + (3 * BlockDecodedLength) + (7 * SuperblockSize4) + TxTypesLength, LevelMapLength);

    /// <summary>
    /// Gets the palette colors of the blocks of the column to the left: for the luma (<paramref name="plane"/> 0) or the U
    /// (<paramref name="plane"/> 1) palette, 8 colors per 4x4 luma row modulo 32.
    /// </summary>
    public Span<ushort> GetLeftPaletteColors(int plane) => _palette.Span.Slice(plane * SuperblockSize4 * Av1Constants.PaletteColors, SuperblockSize4 * Av1Constants.PaletteColors);

    /// <summary>Gets the palette of the block being decoded for a plane: 8 colors.</summary>
    public Span<ushort> GetPaletteColors(int plane) => _palette.Span.Slice((2 * SuperblockSize4 * Av1Constants.PaletteColors) + (plane * Av1Constants.PaletteColors), Av1Constants.PaletteColors);

    /// <summary>Gets <c>AboveLevelContext[ plane ]</c>, indexed by the 4x4 column of the plane.</summary>
    public Span<byte> GetAboveLevelContext(int plane) => GetAbove(_aboveLevelContext[plane]);

    /// <summary>Gets <c>AboveDcContext[ plane ]</c>, indexed by the 4x4 column of the plane.</summary>
    public Span<byte> GetAboveDcContext(int plane) => GetAbove(_aboveDcContext[plane]);

    /// <summary>Gets <c>AboveSegPredContext</c>.</summary>
    public Span<byte> AboveSegPredContext => GetAbove(_aboveSegPredContext);

    /// <summary>
    /// Gets the palette colors of the blocks of the row above: for the luma then the U palette, 8 colors per 4x4 luma column.
    /// The palette colors are only read from the block above and the block to the left: they are stored for one row of
    /// blocks, and one column of a superblock (<see cref="GetLeftPaletteColors"/>).
    /// </summary>
    public Span<ushort> AbovePaletteColors => GetAbove(_abovePaletteColors);

    /// <summary>
    /// Rents the above context of the tiles of a frame of <paramref name="miCols"/> 4x4 luma columns, unless it has that size
    /// already: each line has <see cref="AboveContextMargin"/> more entries, for the blocks that cross the right edge of the
    /// frame. A tile clears the columns it reads before its first block, so a tile only reads what it wrote.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the live-allocation limit.</exception>
    public void PrepareAboveContext(AllocationScope scope, int miCols)
    {
        if (miCols == _aboveMiCols)
            return;

        DisposeAboveContext();
        var length = miCols + AboveContextMargin;
        _abovePaletteColors = Av1PooledArray<ushort>.Rent(scope, 2L * Av1Constants.PaletteColors * length);
        _aboveSegPredContext = Av1PooledArray<byte>.Rent(scope, length);
        for (var plane = 0; plane < 3; plane++)
        {
            _aboveLevelContext[plane] = Av1PooledArray<byte>.Rent(scope, length);
            _aboveDcContext[plane] = Av1PooledArray<byte>.Rent(scope, length);
        }

        _aboveMiCols = miCols;
    }

    public void Dispose()
    {
        _coefficients?.Dispose();
        _bytes?.Dispose();
        _palette?.Dispose();
        DisposeAboveContext();
    }

    private static Span<T> GetAbove<T>(Av1PooledArray<T>? array)
        where T : unmanaged
    {
        return (array ?? throw new ObjectDisposedException(nameof(Av1TileScratch))).Span;
    }

    private void DisposeAboveContext()
    {
        _abovePaletteColors?.Dispose();
        _aboveSegPredContext?.Dispose();
        _abovePaletteColors = null;
        _aboveSegPredContext = null;
        for (var plane = 0; plane < 3; plane++)
        {
            _aboveLevelContext[plane]?.Dispose();
            _aboveDcContext[plane]?.Dispose();
            _aboveLevelContext[plane] = _aboveDcContext[plane] = null;
        }

        _aboveMiCols = -1;
    }
}
