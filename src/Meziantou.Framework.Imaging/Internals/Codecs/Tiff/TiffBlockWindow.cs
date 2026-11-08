using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The strips or tiles of a page that an area of it meets (<see cref="ImageDecodeRequest.Region"/>): the columns and the
/// rows of blocks, end excluded. A strip is a block row of one column.
/// </summary>
/// <param name="FirstColumn">The first column of blocks.</param>
/// <param name="FirstRow">The first row of blocks.</param>
/// <param name="ColumnEnd">The column of blocks after the last one.</param>
/// <param name="RowEnd">The row of blocks after the last one.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TiffBlockWindow(int FirstColumn, int FirstRow, int ColumnEnd, int RowEnd)
{
    /// <summary>Gets the blocks that an area of a page meets.</summary>
    /// <param name="page">The page.</param>
    /// <param name="area">An area of the page, not empty.</param>
    /// <returns>The blocks.</returns>
    public static TiffBlockWindow Create(TiffPage page, Rectangle area)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new TiffBlockWindow(
            area.X / page.BlockWidth,
            area.Y / page.BlockHeight,
            ((area.Right - 1) / page.BlockWidth) + 1,
            ((area.Bottom - 1) / page.BlockHeight) + 1);
    }

    /// <summary>Gets the number of blocks of the window, the planes of a planar page included.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The number of blocks.</returns>
    public int GetBlockCount(TiffPage page) => (ColumnEnd - FirstColumn) * (RowEnd - FirstRow) * page.PlaneCount;

    /// <summary>Determines whether the block at a position of the decoding order (<see cref="TiffPage.GetBlockIndex"/>) is in the window.</summary>
    /// <param name="page">The page.</param>
    /// <param name="position">The position of the block in the decoding order.</param>
    /// <returns><see langword="true"/> when the block is decoded.</returns>
    public bool Contains(TiffPage page, int position)
    {
        var index = page.GetBlockIndex(position) % page.BlocksPerPlane;
        var row = index / page.BlocksAcross;
        var column = index % page.BlocksAcross;
        return row >= FirstRow && row < RowEnd && column >= FirstColumn && column < ColumnEnd;
    }
}
