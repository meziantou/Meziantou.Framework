namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The SIZ marker segment (T.800, A.5.1): the reference grid, the image area and the tile grid on it, and the components.
/// Coordinates of the reference grid are 32-bit unsigned values and are held in <see cref="long"/>.
/// </summary>
internal sealed class Jpeg2000Siz
{
    public Jpeg2000Siz(int capabilities, long xSize, long ySize, long xOffset, long yOffset, long tileWidth, long tileHeight, long tileXOffset, long tileYOffset, Jpeg2000ComponentSize[] components)
    {
        Capabilities = capabilities;
        XSize = xSize;
        YSize = ySize;
        XOffset = xOffset;
        YOffset = yOffset;
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        TileXOffset = tileXOffset;
        TileYOffset = tileYOffset;
        Components = components;
        TilesAcross = CeilingDivide(xSize - tileXOffset, tileWidth);
        TilesDown = CeilingDivide(ySize - tileYOffset, tileHeight);
    }

    /// <summary>Gets the Rsiz field: the capabilities a decoder needs.</summary>
    public int Capabilities { get; }

    /// <summary>Gets the width of the reference grid (Xsiz).</summary>
    public long XSize { get; }

    /// <summary>Gets the height of the reference grid (Ysiz).</summary>
    public long YSize { get; }

    /// <summary>Gets the horizontal offset of the image area on the reference grid (XOsiz).</summary>
    public long XOffset { get; }

    /// <summary>Gets the vertical offset of the image area on the reference grid (YOsiz).</summary>
    public long YOffset { get; }

    /// <summary>Gets the width of a tile on the reference grid (XTsiz).</summary>
    public long TileWidth { get; }

    /// <summary>Gets the height of a tile on the reference grid (YTsiz).</summary>
    public long TileHeight { get; }

    /// <summary>Gets the horizontal offset of the first tile on the reference grid (XTOsiz).</summary>
    public long TileXOffset { get; }

    /// <summary>Gets the vertical offset of the first tile on the reference grid (YTOsiz).</summary>
    public long TileYOffset { get; }

    public Jpeg2000ComponentSize[] Components { get; }

    /// <summary>Gets the width of the image area on the reference grid.</summary>
    public long Width => XSize - XOffset;

    /// <summary>Gets the height of the image area on the reference grid.</summary>
    public long Height => YSize - YOffset;

    /// <summary>Gets the number of tiles in a row of the tile grid (equation B-5).</summary>
    public long TilesAcross { get; }

    /// <summary>Gets the number of rows of the tile grid (equation B-5).</summary>
    public long TilesDown { get; }

    /// <summary>Gets the number of tiles. It is only meaningful once the grid was checked to hold at most <see cref="Jpeg2000Format.MaxTileCount"/> tiles.</summary>
    public int TileCount => (int)(TilesAcross * TilesDown);

    /// <summary>Gets the area of a tile on the reference grid, clipped to the image area (equations B-7 to B-10).</summary>
    public void GetTileBounds(int tileIndex, out long x0, out long y0, out long x1, out long y1)
    {
        var p = tileIndex % TilesAcross;
        var q = tileIndex / TilesAcross;
        x0 = Math.Max(TileXOffset + (p * TileWidth), XOffset);
        y0 = Math.Max(TileYOffset + (q * TileHeight), YOffset);
        x1 = Math.Min(TileXOffset + ((p + 1) * TileWidth), XSize);
        y1 = Math.Min(TileYOffset + ((q + 1) * TileHeight), YSize);
    }

    /// <summary>Returns the smallest integer that is not less than <paramref name="value"/> / <paramref name="divisor"/>, for non-negative values.</summary>
    public static long CeilingDivide(long value, long divisor) => (value + divisor - 1) / divisor;
}
