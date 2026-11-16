namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The order of the chunks of an OpenEXR file written by <see cref="OpenExrEncoder"/> (the <c>lineOrder</c> attribute).</summary>
public enum OpenExrLineOrder
{
    /// <summary>Chunks from the top of the image to the bottom (<c>INCREASING_Y</c>). This is the default.</summary>
    IncreasingY = 0,

    /// <summary>Chunks from the bottom of the image to the top (<c>DECREASING_Y</c>); for tiles, the rows of tiles of each level from the bottom.</summary>
    DecreasingY = 1,

    /// <summary>
    /// Tiles in an order the reader must not rely on (<c>RANDOM_Y</c>): valid for tiled output only
    /// (<see cref="OpenExrEncoder.Tiling"/>). The encoder writes them in the order of <see cref="IncreasingY"/>.
    /// </summary>
    RandomY = 2,
}
