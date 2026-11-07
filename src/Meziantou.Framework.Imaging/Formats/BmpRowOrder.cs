namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The order of the rows written by <see cref="BmpEncoder"/>.</summary>
public enum BmpRowOrder
{
    /// <summary>The bottom row first (a positive <c>biHeight</c>). This is the default and the order every BMP reader supports.</summary>
    BottomUp = 0,

    /// <summary>
    /// The top row first (a negative <c>biHeight</c>). The decoded pixels are identical to <see cref="BottomUp"/>; some old
    /// readers do not support it, and run-length encoded bitmaps cannot use it.
    /// </summary>
    TopDown = 1,
}
