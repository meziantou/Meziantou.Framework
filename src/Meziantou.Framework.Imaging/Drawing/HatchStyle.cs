namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The tiles of 8 by 8 pixels a hatch <see cref="PatternBrush"/> repeats: lines, or a fraction of the pixels.</summary>
/// <remarks>
/// The pixel <c>(x, y)</c> of the tile, from its top-left corner, has the foreground color where the style says so,
/// and the background color elsewhere. The percentages light the pixels of lowest rank in the ordered dither matrix
/// of 8 by 8 (Bayer): <c>round(64 p / 100)</c> pixels for <c>p</c> percent, spread evenly over the tile, each one also
/// lit by every higher percentage.
/// </remarks>
public enum HatchStyle
{
    /// <summary>A horizontal line: the pixels of the row 0.</summary>
    Horizontal,

    /// <summary>A vertical line: the pixels of the column 0.</summary>
    Vertical,

    /// <summary>A horizontal line and a vertical line: the pixels of the row 0 and of the column 0.</summary>
    Cross,

    /// <summary>A line from the top-left corner to the bottom-right corner: the pixels where <c>x = y</c>.</summary>
    ForwardDiagonal,

    /// <summary>A line from the top-right corner to the bottom-left corner: the pixels where <c>x + y = 7</c>.</summary>
    BackwardDiagonal,

    /// <summary>Both diagonals: the pixels of <see cref="ForwardDiagonal"/> and of <see cref="BackwardDiagonal"/>.</summary>
    DiagonalCross,

    /// <summary>6 pixels of the 64 (9.4%).</summary>
    Percent10,

    /// <summary>13 pixels of the 64 (20.3%).</summary>
    Percent20,

    /// <summary>19 pixels of the 64 (29.7%).</summary>
    Percent30,

    /// <summary>26 pixels of the 64 (40.6%).</summary>
    Percent40,

    /// <summary>32 pixels of the 64, a checkerboard (50%).</summary>
    Percent50,

    /// <summary>38 pixels of the 64 (59.4%).</summary>
    Percent60,

    /// <summary>45 pixels of the 64 (70.3%).</summary>
    Percent70,

    /// <summary>51 pixels of the 64 (79.7%).</summary>
    Percent80,

    /// <summary>58 pixels of the 64 (90.6%).</summary>
    Percent90,
}
