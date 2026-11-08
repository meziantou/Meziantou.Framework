using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The tiles of a grid of a file of the HEIF family that an area of its coded image needs (<see cref="ImageDecodeRequest.Region"/>):
/// the columns and the rows of tiles, end excluded. The planes of the image then hold these tiles only, from the top left of
/// the first one (<see cref="GetX"/>, <see cref="GetY"/>).
/// </summary>
/// <remarks>
/// The conversion of a pixel reads the chroma samples next to its own one (<see cref="HeifYuvConverter.UpsampleChromaRow"/>):
/// the area is widened by <see cref="Margin"/> pixels on each side, so that the pixels of the area are those of the whole
/// image, and the edges of the planes, where the samples are replicated, are outside of it.
/// </remarks>
/// <param name="FirstColumn">The first column of tiles.</param>
/// <param name="FirstRow">The first row of tiles.</param>
/// <param name="ColumnEnd">The column of tiles after the last one.</param>
/// <param name="RowEnd">The row of tiles after the last one.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct HeifGridWindow(int FirstColumn, int FirstRow, int ColumnEnd, int RowEnd)
{
    /// <summary>The pixels around the area whose tiles are decoded too: the chroma samples that the upsampling of the pixels of its edges reads.</summary>
    public const int Margin = 2;

    /// <summary>Gets the window of every tile of an image (a coded image that is not a grid is a grid of one tile).</summary>
    /// <param name="image">The coded image.</param>
    /// <returns>The window.</returns>
    public static HeifGridWindow All(HeifCodedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new HeifGridWindow(0, 0, image.Columns, image.Rows);
    }

    /// <summary>Gets the tiles of a grid that an area of its coded image needs.</summary>
    /// <param name="image">The coded image.</param>
    /// <param name="area">An area of the coded image (the clean aperture and the region), not empty.</param>
    /// <returns>The window.</returns>
    public static HeifGridWindow Create(HeifCodedImage image, Rectangle area)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!image.IsGrid)
            return All(image);

        var tile = image.TileSize;
        var x0 = Math.Max(0, area.X - Margin);
        var y0 = Math.Max(0, area.Y - Margin);
        var x1 = Math.Min(image.Size.Width, area.Right + Margin);
        var y1 = Math.Min(image.Size.Height, area.Bottom + Margin);
        return new HeifGridWindow(x0 / tile.Width, y0 / tile.Height, ((x1 - 1) / tile.Width) + 1, ((y1 - 1) / tile.Height) + 1);
    }

    /// <summary>Gets the number of columns of tiles.</summary>
    public int Columns => ColumnEnd - FirstColumn;

    /// <summary>Gets the number of rows of tiles.</summary>
    public int Rows => RowEnd - FirstRow;

    /// <summary>Determines whether the window holds every tile of an image.</summary>
    /// <param name="image">The coded image.</param>
    /// <returns><see langword="true"/> for a whole image.</returns>
    public bool IsAll(HeifCodedImage image) => this == All(image);

    /// <summary>Gets the indexes of the tiles of the window, in the order of the tiles of the grid.</summary>
    /// <param name="image">The coded image.</param>
    /// <returns>The indexes.</returns>
    public int[] GetTiles(HeifCodedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var tiles = new int[Columns * Rows];
        var i = 0;
        for (var row = FirstRow; row < RowEnd; row++)
        {
            for (var column = FirstColumn; column < ColumnEnd; column++)
            {
                tiles[i++] = (row * image.Columns) + column;
            }
        }

        return tiles;
    }

    /// <summary>Gets the column of the coded image that the first column of the planes is.</summary>
    /// <param name="image">The coded image.</param>
    /// <returns>The column.</returns>
    public int GetX(HeifCodedImage image) => FirstColumn * image.TileSize.Width;

    /// <summary>Gets the row of the coded image that the first row of the planes is.</summary>
    /// <param name="image">The coded image.</param>
    /// <returns>The row.</returns>
    public int GetY(HeifCodedImage image) => FirstRow * image.TileSize.Height;

    /// <summary>Gets the size of the part of the coded image that the tiles of the window cover.</summary>
    /// <param name="image">The coded image.</param>
    /// <returns>The size, at most the one of the coded image.</returns>
    public Size GetCodedSize(HeifCodedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new Size(
            Math.Min(image.Size.Width, ColumnEnd * image.TileSize.Width) - GetX(image),
            Math.Min(image.Size.Height, RowEnd * image.TileSize.Height) - GetY(image));
    }

    /// <summary>Gets an area of the coded image in the coordinates of the planes of the window.</summary>
    /// <param name="image">The coded image.</param>
    /// <param name="area">The area, inside the window.</param>
    /// <returns>The area, from the top left of the first tile of the window.</returns>
    public Rectangle ToPlanes(HeifCodedImage image, Rectangle area) => new(area.X - GetX(image), area.Y - GetY(image), area.Width, area.Height);
}
