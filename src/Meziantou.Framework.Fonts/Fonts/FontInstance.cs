namespace Meziantou.Framework.Fonts;

/// <summary>An instance of a variable font: a value for each axis, and what only depends on these values.</summary>
/// <remarks>The scalars of the regions of each variation store are computed once, when the instance is created.</remarks>
public sealed class FontInstance
{
    /// <summary>The normalized value 1, the end of an axis, as a 16.16 number.</summary>
    private const int One = 1 << 16;

    /// <summary>The number of bits between a 2.14 number and a 16.16 number.</summary>
    public const int TableShift = 2;

    /// <summary>The value 1 as a 2.14 fixed-point number: the largest normalized value of an axis.</summary>
    public const int TableOne = One >> TableShift;

    private readonly int[] _userCoordinates;
    private readonly int[] _coordinates;

    internal FontInstance(int[] userCoordinates, int[] coordinates)
    {
        _userCoordinates = userCoordinates;
        _coordinates = coordinates;
    }

    /// <summary>Gets the value of each axis, in the units of the axis, as 16.16 fixed-point numbers.</summary>
    public ReadOnlySpan<int> UserCoordinates => _userCoordinates;

    /// <summary>Gets the normalized value of each axis, from -1 to 1, as 2.14 fixed-point numbers: the form of the variation tables.</summary>
    public ReadOnlySpan<int> Coordinates => _coordinates;

    /// <summary>Gets the scalars of the store of the <c>HVAR</c> table.</summary>
    internal double[] AdvanceScalars { get; set; } = [];

    /// <summary>Gets or sets the scalar of each region of the variation store of the vertical metrics (<c>VVAR</c>).</summary>
    internal double[] VerticalScalars { get; set; } = [];

    /// <summary>Gets or sets the scalars of the store of the <c>BASE</c> table, which the coordinates of the baselines refer to.</summary>
    internal double[] BaselineScalars { get; set; } = [];

    /// <summary>Gets the scalars of the store of the <c>GDEF</c> table, which the positions of the <c>GPOS</c> table refer to.</summary>
    internal double[] LayoutScalars { get; set; } = [];

    /// <summary>Gets the scalars of the store of the <c>CFF2</c> table.</summary>
    internal double[] OutlineScalars { get; set; } = [];

    /// <summary>Gets the scalars of the store of the <c>COLR</c> table.</summary>
    internal double[] ColorScalars { get; set; } = [];

    /// <summary>
    /// Normalizes the value of an axis: -1 at its minimum, 0 at its default, 1 at its maximum. OpenType specification,
    /// "Coordinate scales and normalization".
    /// </summary>
    /// <param name="axis">The axis.</param>
    /// <param name="value">The value, as a 16.16 fixed-point number, within the range of the axis.</param>
    /// <returns>
    /// The normalized value, as a 16.16 fixed-point number: the <c>avar</c> table of the font maps it with this
    /// precision, and <see cref="ToTableValue"/> then gives the 2.14 number the variation tables are compared with.
    /// </returns>
    public static int Normalize(in FontAxisRecord axis, int value)
    {
        long numerator;
        long denominator;
        if (value < axis.Default)
        {
            numerator = (long)value - axis.Default;
            denominator = (long)axis.Default - axis.Minimum;
        }
        else if (value > axis.Default)
        {
            numerator = (long)value - axis.Default;
            denominator = (long)axis.Maximum - axis.Default;
        }
        else
        {
            return 0;
        }

        // Rounded to the nearest; both sides of the default are rounded the same way.
        return (int)Math.Clamp(Math.Round((double)numerator / denominator * 65536, MidpointRounding.AwayFromZero), -One, One);
    }

    /// <summary>
    /// Converts a normalized 16.16 number to the 2.14 number of the variation tables, as the specification says: by
    /// adding 2 and dropping two bits.
    /// </summary>
    public static int ToTableValue(int value) => Math.Clamp((value + (1 << (TableShift - 1))) >> TableShift, -(One >> TableShift), One >> TableShift);
}
