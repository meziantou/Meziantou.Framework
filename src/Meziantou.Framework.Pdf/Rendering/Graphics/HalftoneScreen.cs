namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>
/// The halftone screen of one color component (ISO 32000-2, 10.6): a threshold for each pixel of device space. The
/// thresholds of a cell are repeated over device space by a lattice, so that a threshold array (types 6, 10 and 16) and
/// the cell of a spot function (type 1) are the same thing: the cells of a lattice and a threshold for each residue.
/// </summary>
/// <remarks>
/// A threshold <c>T</c> is on the scale of 16 bits: a component <c>c</c> from 0 to 1 is white (fully on) where
/// <c>65535 × c ≥ T − 0.5</c>, and black (off) elsewhere.
/// </remarks>
internal sealed class HalftoneScreen
{
    // The largest number of pixels of the box a cell is stored in: 64 MB of thresholds
    private const long MaxBoxPixels = 32L * 1024 * 1024;

    // The side of the largest cell of a spot function, in pixels
    private const double MaxSpotCellSide = 256;

    // The basis of the lattice and its determinant, the number of pixels of a cell
    private readonly long _u1;
    private readonly long _u2;
    private readonly long _v1;
    private readonly long _v2;
    private readonly long _cellPixels;

    // The cell of the lattice that starts at the origin, { s × u + t × v | 0 ≤ s, t < 1 }, is stored in its bounding box
    private readonly long _boxX;
    private readonly long _boxY;
    private readonly int _boxWidth;
    private readonly ushort[] _thresholds;

    private HalftoneScreen(long u1, long u2, long v1, long v2)
    {
        _u1 = u1;
        _u2 = u2;
        _v1 = v1;
        _v2 = v2;
        _cellPixels = (u1 * v2) - (u2 * v1);
        _boxX = Math.Min(Math.Min(0, u1), Math.Min(v1, u1 + v1));
        _boxY = Math.Min(Math.Min(0, u2), Math.Min(v2, u2 + v2));
        _boxWidth = (int)(Math.Max(Math.Max(0, u1), Math.Max(v1, u1 + v1)) - _boxX);
        var boxHeight = (int)(Math.Max(Math.Max(0, u2), Math.Max(v2, u2 + v2)) - _boxY);
        _thresholds = new ushort[_boxWidth * boxHeight];

        // The lattice holds (n / gcd(u2, v2), 0) and (0, n / gcd(u1, v1)): the thresholds repeat with these periods
        PeriodX = _cellPixels / GreatestCommonDivisor(u2, v2);
        PeriodY = _cellPixels / GreatestCommonDivisor(u1, v1);
    }

    /// <summary>Gets the horizontal period of the thresholds, in pixels.</summary>
    public long PeriodX { get; }

    /// <summary>Gets the vertical period of the thresholds, in pixels.</summary>
    public long PeriodY { get; }

    /// <summary>
    /// Creates the screen of a threshold array of one or two rectangles (10.6.5.3 to 10.6.5.5): the second rectangle is
    /// below the first and starts in the same column, and both tile device space. The squares of a type 10 halftone are
    /// rectangles of this kind.
    /// </summary>
    /// <param name="width">The width of the first rectangle.</param>
    /// <param name="height">The height of the first rectangle.</param>
    /// <param name="width2">The width of the second rectangle, or 0 for none.</param>
    /// <param name="height2">The height of the second rectangle, or 0 for none.</param>
    /// <param name="getThreshold">The threshold of an index of the array: the first rectangle row by row, then the second.</param>
    /// <returns>The screen, or <see langword="null"/> when the sizes are not valid or too large.</returns>
    public static HalftoneScreen? FromRectangles(int width, int height, int width2, int height2, Func<int, ushort> getThreshold)
    {
        if (width <= 0 || height <= 0 || width2 < 0 || height2 < 0)
            return null;

        if (width2 == 0 || height2 == 0)
        {
            width2 = 0;
            height2 = 0;
        }

        // The first rectangle at the origin and the second below it repeat with (W, -H2) and (W2, H): the second rectangle
        // moved by the first vector is on the right of the first one, and the first one moved by the second is on the
        // right of the second one
        var screen = Create(width, -height2, width2, height);
        if (screen is null)
            return null;

        var index = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                screen.SetThreshold(x, y, getThreshold(index++));
            }
        }

        for (var y = 0; y < height2; y++)
        {
            for (var x = 0; x < width2; x++)
            {
                screen.SetThreshold(x, height + y, getThreshold(index++));
            }
        }

        return screen;
    }

    /// <summary>
    /// Creates the screen of a frequency, an angle and a spot function (10.6.3): the cell is a square of the size the
    /// frequency gives, turned by the angle, whose corners are pixels (the ordinary halftoning of the specification: the
    /// frequency and the angle are rounded so that the cells are made of whole pixels). The pixels of a cell whiten in
    /// the order of the values the spot function gives at their centers, the lowest first.
    /// </summary>
    /// <param name="cellSide">The side of a cell, in pixels: the resolution divided by the frequency.</param>
    /// <param name="angle">The angle of the screen, in degrees counterclockwise in device space.</param>
    /// <param name="spotFunction">The spot function, of the coordinates of a pixel in its cell, from -1 to 1.</param>
    public static HalftoneScreen? FromSpotFunction(double cellSide, double angle, Func<double, double, double> spotFunction)
    {
        if (!double.IsFinite(cellSide) || cellSide <= 0 || !double.IsFinite(angle))
            return null;

        cellSide = Math.Min(cellSide, MaxSpotCellSide);
        var radians = Math.IEEERemainder(angle, 360) * Math.PI / 180;
        var a = (long)Math.Round(cellSide * Math.Cos(radians), MidpointRounding.AwayFromZero);
        var b = (long)Math.Round(cellSide * Math.Sin(radians), MidpointRounding.AwayFromZero);
        if (a == 0 && b == 0)
        {
            a = 1;
        }

        // The cell is spanned by (a, b) and (-b, a): the image of the unit square turned by the angle
        var screen = Create(a, b, -b, a);
        if (screen is null)
            return null;

        var n = screen._cellPixels;
        var cell = new List<(double Value, int Index)>((int)n);
        var boxHeight = screen._thresholds.Length / screen._boxWidth;
        for (var row = 0; row < boxHeight; row++)
        {
            for (var column = 0; column < screen._boxWidth; column++)
            {
                var x = screen._boxX + column;
                var y = screen._boxY + row;
                if (!screen.IsInCell(x, y))
                    continue;

                // The coordinates of the center of the pixel in the cell, from -1 to 1
                var s = (((x + 0.5) * a) + ((y + 0.5) * b)) / n;
                var t = (((y + 0.5) * a) - ((x + 0.5) * b)) / n;
                var value = spotFunction((2 * (s - Math.Floor(s))) - 1, (2 * (t - Math.Floor(t))) - 1);
                cell.Add((double.IsNaN(value) ? 0 : value, (row * screen._boxWidth) + column));
            }
        }

        // The pixel of rank r is white from the gray level (r + 1) / n: a gray level g makes floor(g × n) pixels white
        // (10.6.2), g rounded to 8 bits as the colors of the canvas are (half a step below the level). Pixels of the
        // same value are in the order of the rows, which the specification leaves arbitrary.
        cell.Sort((left, right) => left.Value != right.Value ? left.Value.CompareTo(right.Value) : left.Index.CompareTo(right.Index));
        for (var rank = 0; rank < cell.Count; rank++)
        {
            screen._thresholds[cell[rank].Index] = (ushort)Math.Max(1, Math.Round(65535.0 * (rank + 1) / n, MidpointRounding.AwayFromZero) - 128);
        }

        return screen;
    }

    /// <summary>Gets the threshold of an 8-bit value of a threshold array: 0 is 1 (10.6.4), and a level is white from half a step below it.</summary>
    public static ushort FromThreshold8(byte value) => (ushort)((257 * Math.Max(value, (byte)1)) - 128);

    /// <summary>Gets the threshold of a 16-bit value of a threshold array: 0 is 1 (10.6.4).</summary>
    public static ushort FromThreshold16(ushort value) => Math.Max(value, (ushort)1);

    /// <summary>Gets the threshold of a pixel, relative to the halftone origin.</summary>
    public ushort GetThreshold(long x, long y)
    {
        var (column, row) = Reduce(x, y);
        return _thresholds[(int)((row * _boxWidth) + column)];
    }

    private static HalftoneScreen? Create(long u1, long u2, long v1, long v2)
    {
        var determinant = (u1 * v2) - (u2 * v1);
        var boxWidth = Math.Max(Math.Max(0, u1), Math.Max(v1, u1 + v1)) - Math.Min(Math.Min(0, u1), Math.Min(v1, u1 + v1));
        var boxHeight = Math.Max(Math.Max(0, u2), Math.Max(v2, u2 + v2)) - Math.Min(Math.Min(0, u2), Math.Min(v2, u2 + v2));
        if (determinant <= 0 || boxWidth * boxHeight > MaxBoxPixels)
            return null;

        return new HalftoneScreen(u1, u2, v1, v2);
    }

    /// <summary>Gets whether a pixel is in the cell that starts at the origin.</summary>
    private bool IsInCell(long x, long y)
    {
        var s = (x * _v2) - (y * _v1);
        var t = (y * _u1) - (x * _u2);
        return s >= 0 && s < _cellPixels && t >= 0 && t < _cellPixels;
    }

    /// <summary>Gets the position in the box of the pixel of the cell at the origin that a lattice vector moves to a pixel.</summary>
    private (long Column, long Row) Reduce(long x, long y)
    {
        // The coordinates of the pixel in the basis, times n: their integer parts are the lattice vector
        var s = FloorDivide((x * _v2) - (y * _v1), _cellPixels);
        var t = FloorDivide((y * _u1) - (x * _u2), _cellPixels);
        return (x - (s * _u1) - (t * _v1) - _boxX, y - (s * _u2) - (t * _v2) - _boxY);
    }

    private void SetThreshold(long x, long y, ushort threshold)
    {
        var (column, row) = Reduce(x, y);
        _thresholds[(int)((row * _boxWidth) + column)] = threshold;
    }

    private static long FloorDivide(long value, long divisor)
    {
        var quotient = Math.DivRem(value, divisor, out var remainder);
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static long GreatestCommonDivisor(long a, long b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
