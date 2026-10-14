using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// An immutable matrix that gives each pixel a new color from its own one: brighter, grayer, tinted, inverted, more
/// transparent. It is applied to an image by <see cref="ImageProcessingExtensions.ApplyColorMatrix(Image, ColorMatrix, CancellationToken)"/>,
/// and to a drawing by <see cref="DrawingCanvas.Filter(ColorMatrix, DrawingAction)"/>.
/// </summary>
/// <remarks>
/// <para>
/// The matrix has four rows, for the red, the green, the blue and the alpha of the result, and five columns, for the
/// red, the green, the blue and the alpha of the pixel and for a constant: with the samples as fractions of one and
/// straight alpha, <c>R' = m00 R + m01 G + m02 B + m03 A + m04</c>, and so on for each row. This is the matrix of the
/// <c>feColorMatrix</c> filter of SVG, and the ready-made matrices are those of the filter functions of CSS
/// (<c>grayscale()</c>, <c>sepia()</c>, <c>saturate()</c>, <c>hue-rotate()</c>, <c>invert()</c>, <c>opacity()</c>,
/// <c>brightness()</c>, <c>contrast()</c>), from "Filter Effects Module Level 1" of the W3C.
/// </para>
/// <para>
/// <b>Arithmetic.</b> The matrix is applied to the stored sample values of the pixel, at their precision: with
/// <c>M</c> the largest sample value, a sample of the result is <c>floor(m0 R + m1 G + m2 B + m3 A + m4 M + 1/2 + 2^-20)</c>,
/// the terms added in that order in double precision, kept between 0 and <c>M</c>. Only multiplications and additions
/// are involved: the result does not depend on the platform. A pixel format without alpha has an alpha of <c>M</c> and
/// gets the color of the result over black (each color multiplied by the alpha of the result); a grayscale format
/// has its gray level as red, green and blue, and gets the Rec. 709 luma of the result.
/// </para>
/// <para>
/// A color matrix is thread-safe: create it once and use it as many times as needed. <see cref="Then"/> makes one
/// matrix of two, which is applied once per pixel instead of twice, without the rounding in between.
/// </para>
/// </remarks>
public sealed class ColorMatrix
{
    /// <summary>The number of rows: red, green, blue and alpha.</summary>
    internal const int RowCount = 4;

    /// <summary>The number of columns: red, green, blue, alpha and the constant.</summary>
    internal const int ColumnCount = 5;

    // The luminance coefficients of the filter functions of CSS (Filter Effects Module Level 1): those of the
    // grayscale() function are the Rec. 709 luma coefficients of the library, those of saturate() and hue-rotate()
    // are the rounded ones of feColorMatrix
    private const double LumaR = 0.2126;
    private const double LumaG = 0.7152;
    private const double LumaB = 0.0722;

    private readonly double[] _values;

    /// <summary>Initializes a new instance of the <see cref="ColorMatrix"/> class.</summary>
    /// <param name="values">
    /// The twenty coefficients, row by row: the row of the red of the result first, then green, blue and alpha; in
    /// each row, the coefficients of the red, the green, the blue and the alpha of the pixel, then the constant, as a
    /// fraction of the largest sample value. They are copied.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="values"/> does not hold exactly twenty values, or a value is not finite.</exception>
    public ColorMatrix(ReadOnlySpan<double> values)
    {
        if (values.Length != RowCount * ColumnCount)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"A color matrix has {RowCount * ColumnCount} coefficients, but {values.Length} were provided."), nameof(values));

        foreach (var value in values)
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("The coefficients of a color matrix must be finite.", nameof(values));
        }

        _values = values.ToArray();
        IsIdentity = _values.AsSpan().SequenceEqual(IdentityValues);
    }

    private ColorMatrix(double[] values, bool isIdentity)
    {
        _values = values;
        IsIdentity = isIdentity;
    }

    private static ReadOnlySpan<double> IdentityValues =>
    [
        1, 0, 0, 0, 0,
        0, 1, 0, 0, 0,
        0, 0, 1, 0, 0,
        0, 0, 0, 1, 0,
    ];

    /// <summary>Gets the matrix that changes nothing.</summary>
    public static ColorMatrix Identity { get; } = new(IdentityValues.ToArray(), isIdentity: true);

    /// <summary>Gets a value indicating whether the matrix changes nothing: every coefficient is the one of <see cref="Identity"/>.</summary>
    public bool IsIdentity { get; }

    /// <summary>Gets the coefficients, row by row.</summary>
    internal ReadOnlySpan<double> Values => _values;

    /// <summary>Gets a coefficient.</summary>
    /// <param name="row">The row: 0 for the red of the result, 1 for its green, 2 for its blue, 3 for its alpha.</param>
    /// <param name="column">The column: 0 to 3 for the red, the green, the blue and the alpha of the pixel, 4 for the constant.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="row"/> or <paramref name="column"/> is out of range.</exception>
    public double this[int row, int column]
    {
        get
        {
            if ((uint)row >= RowCount)
                throw new ArgumentOutOfRangeException(nameof(row), row, "The row is out of range.");

            if ((uint)column >= ColumnCount)
                throw new ArgumentOutOfRangeException(nameof(column), column, "The column is out of range.");

            return _values[(row * ColumnCount) + column];
        }
    }

    /// <summary>Creates the matrix that applies this one, then another one, to the color this one gives.</summary>
    /// <remarks>
    /// The result is the product of the two matrices: applying it is applying both without rounding or clamping the
    /// color in between. A color that the first matrix takes out of range is therefore not clamped before the second
    /// one, as it is when two matrices are applied one after the other.
    /// </remarks>
    /// <param name="next">The matrix that is applied second.</param>
    /// <returns>The combined matrix.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="next"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coefficient of the result is not a finite number.</exception>
    public ColorMatrix Then(ColorMatrix next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (IsIdentity)
            return next;

        if (next.IsIdentity)
            return this;

        var first = _values;
        var second = next._values;
        var result = new double[RowCount * ColumnCount];
        for (var row = 0; row < RowCount; row++)
        {
            for (var column = 0; column < ColumnCount; column++)
            {
                double sum = 0;
                for (var k = 0; k < RowCount; k++)
                {
                    sum += second[(row * ColumnCount) + k] * first[(k * ColumnCount) + column];
                }

                // The constant of the first matrix goes through the second one, whose own constant is added
                if (column == ColumnCount - 1)
                {
                    sum += second[(row * ColumnCount) + column];
                }

                result[(row * ColumnCount) + column] = sum;
            }
        }

        return new ColorMatrix(result);
    }

    /// <summary>Creates the matrix that multiplies the colors by a factor: <c>brightness()</c> in CSS.</summary>
    /// <param name="amount">The factor: 1 changes nothing, 0 gives black, more than 1 brightens.</param>
    /// <returns>The matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative or not a finite number.</exception>
    public static ColorMatrix CreateBrightness(double amount)
    {
        ValidateAmount(amount, double.MaxValue, nameof(amount));
        return new ColorMatrix(
        [
            amount, 0, 0, 0, 0,
            0, amount, 0, 0, 0,
            0, 0, amount, 0, 0,
            0, 0, 0, 1, 0,
        ]);
    }

    /// <summary>Creates the matrix that moves the colors away from the middle gray, or toward it: <c>contrast()</c> in CSS.</summary>
    /// <param name="amount">The factor: 1 changes nothing, 0 gives the middle gray, more than 1 increases the contrast.</param>
    /// <returns>The matrix: each color <c>c</c> becomes <c>amount c + (1 - amount) / 2</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative or not a finite number.</exception>
    public static ColorMatrix CreateContrast(double amount)
    {
        ValidateAmount(amount, double.MaxValue, nameof(amount));
        var offset = 0.5 - (0.5 * amount);
        return new ColorMatrix(
        [
            amount, 0, 0, 0, offset,
            0, amount, 0, 0, offset,
            0, 0, amount, 0, offset,
            0, 0, 0, 1, 0,
        ]);
    }

    /// <summary>Creates the matrix that makes the colors more or less vivid: <c>saturate()</c> in CSS.</summary>
    /// <param name="amount">The factor: 1 changes nothing, 0 leaves no color, more than 1 makes the colors more vivid.</param>
    /// <returns>The matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative or not a finite number.</exception>
    public static ColorMatrix CreateSaturation(double amount)
    {
        ValidateAmount(amount, double.MaxValue, nameof(amount));
        if (amount == 1)
            return Identity;

        return new ColorMatrix(
        [
            0.213 + (0.787 * amount), 0.715 - (0.715 * amount), 0.072 - (0.072 * amount), 0, 0,
            0.213 - (0.213 * amount), 0.715 + (0.285 * amount), 0.072 - (0.072 * amount), 0, 0,
            0.213 - (0.213 * amount), 0.715 - (0.715 * amount), 0.072 + (0.928 * amount), 0, 0,
            0, 0, 0, 1, 0,
        ]);
    }

    /// <summary>Creates the matrix that turns the hues around the color wheel: <c>hue-rotate()</c> in CSS.</summary>
    /// <remarks>The sine and the cosine of the angle do not come from the platform: the matrix is the same everywhere, and a multiple of a full turn gives <see cref="Identity"/>.</remarks>
    /// <param name="degrees">The angle, in degrees: 180 gives each color its opposite hue.</param>
    /// <returns>The matrix.</returns>
    /// <exception cref="ArgumentException"><paramref name="degrees"/> is not a finite number.</exception>
    public static ColorMatrix CreateHueRotation(double degrees)
    {
        if (!double.IsFinite(degrees))
            throw new ArgumentException("The angle must be a finite number.", nameof(degrees));

        DeterministicAngle.SinCos(degrees / 180, out var sin, out var cos);
        if (sin == 0 && cos == 1)
            return Identity;

        return new ColorMatrix(
        [
            0.213 + (cos * 0.787) - (sin * 0.213), 0.715 - (cos * 0.715) - (sin * 0.715), 0.072 - (cos * 0.072) + (sin * 0.928), 0, 0,
            0.213 - (cos * 0.213) + (sin * 0.143), 0.715 + (cos * 0.285) + (sin * 0.140), 0.072 - (cos * 0.072) - (sin * 0.283), 0, 0,
            0.213 - (cos * 0.213) - (sin * 0.787), 0.715 - (cos * 0.715) + (sin * 0.715), 0.072 + (cos * 0.928) + (sin * 0.072), 0, 0,
            0, 0, 0, 1, 0,
        ]);
    }

    /// <summary>Creates the matrix that replaces the colors by their gray level, entirely or in part: <c>grayscale()</c> in CSS.</summary>
    /// <remarks>
    /// With an amount of 1, the gray level is the Rec. 709 luma of the stored sample values, with the rounding of
    /// <see cref="ImageProcessingExtensions.Grayscale(Image, CancellationToken)"/>: both give the same pixels.
    /// </remarks>
    /// <param name="amount">How much of the color is replaced: 1 (the default) for gray levels, 0 to change nothing.</param>
    /// <returns>The matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not between 0 and 1.</exception>
    public static ColorMatrix CreateGrayscale(double amount = 1)
    {
        ValidateAmount(amount, 1, nameof(amount));
        if (amount == 0)
            return Identity;

        var keep = 1 - amount;
        return new ColorMatrix(
        [
            LumaR + ((1 - LumaR) * keep), LumaG - (LumaG * keep), LumaB - (LumaB * keep), 0, 0,
            LumaR - (LumaR * keep), LumaG + ((1 - LumaG) * keep), LumaB - (LumaB * keep), 0, 0,
            LumaR - (LumaR * keep), LumaG - (LumaG * keep), LumaB + ((1 - LumaB) * keep), 0, 0,
            0, 0, 0, 1, 0,
        ]);
    }

    /// <summary>Creates the matrix that gives the colors the brown tones of an old photograph, entirely or in part: <c>sepia()</c> in CSS.</summary>
    /// <param name="amount">How much of the color is replaced: 1 (the default) for sepia tones, 0 to change nothing.</param>
    /// <returns>The matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not between 0 and 1.</exception>
    public static ColorMatrix CreateSepia(double amount = 1)
    {
        ValidateAmount(amount, 1, nameof(amount));
        if (amount == 0)
            return Identity;

        var keep = 1 - amount;
        return new ColorMatrix(
        [
            0.393 + (0.607 * keep), 0.769 - (0.769 * keep), 0.189 - (0.189 * keep), 0, 0,
            0.349 - (0.349 * keep), 0.686 + (0.314 * keep), 0.168 - (0.168 * keep), 0, 0,
            0.272 - (0.272 * keep), 0.534 - (0.534 * keep), 0.131 + (0.869 * keep), 0, 0,
            0, 0, 0, 1, 0,
        ]);
    }

    /// <summary>Creates the matrix that inverts the colors, entirely or in part: <c>invert()</c> in CSS.</summary>
    /// <param name="amount">How far each color goes to its opposite: 1 (the default) for the negative, 0.5 for the middle gray, 0 to change nothing.</param>
    /// <returns>The matrix: each color <c>c</c> becomes <c>(1 - 2 amount) c + amount</c>. The alpha is kept.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not between 0 and 1.</exception>
    public static ColorMatrix CreateInvert(double amount = 1)
    {
        ValidateAmount(amount, 1, nameof(amount));
        if (amount == 0)
            return Identity;

        var slope = 1 - (2 * amount);
        return new ColorMatrix(
        [
            slope, 0, 0, 0, amount,
            0, slope, 0, 0, amount,
            0, 0, slope, 0, amount,
            0, 0, 0, 1, 0,
        ]);
    }

    /// <summary>Creates the matrix that multiplies the alpha by a factor: <c>opacity()</c> in CSS.</summary>
    /// <param name="amount">The factor: 1 changes nothing, 0 makes every pixel transparent.</param>
    /// <returns>The matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not between 0 and 1.</exception>
    public static ColorMatrix CreateOpacity(double amount)
    {
        ValidateAmount(amount, 1, nameof(amount));
        return new ColorMatrix(
        [
            1, 0, 0, 0, 0,
            0, 1, 0, 0, 0,
            0, 0, 1, 0, 0,
            0, 0, 0, amount, 0,
        ]);
    }

    /// <summary>
    /// Creates the matrix that gives every pixel one color and keeps its alpha: an icon or a glyph that was drawn in
    /// one color is drawn in another one.
    /// </summary>
    /// <param name="color">The color, with straight alpha: its alpha multiplies the alpha of the pixels. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <returns>The matrix.</returns>
    public static ColorMatrix CreateTint(Rgba64 color)
    {
        return new ColorMatrix(
        [
            0, 0, 0, 0, color.R / (double)ushort.MaxValue,
            0, 0, 0, 0, color.G / (double)ushort.MaxValue,
            0, 0, 0, 0, color.B / (double)ushort.MaxValue,
            0, 0, 0, color.A / (double)ushort.MaxValue, 0,
        ]);
    }

    /// <summary>
    /// Creates the matrix that draws an image with two inks: each pixel gets the color between <paramref name="dark"/>
    /// and <paramref name="light"/> that its gray level says.
    /// </summary>
    /// <remarks>
    /// The gray level is the Rec. 709 luma of the stored sample values: black becomes <paramref name="dark"/>, white
    /// becomes <paramref name="light"/>, and a gray the color that far between them, each color sample apart. The
    /// alpha of the pixels is kept, and the alpha of the two colors is not used.
    /// </remarks>
    /// <param name="dark">The color of black pixels. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <param name="light">The color of white pixels. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <returns>The matrix.</returns>
    public static ColorMatrix CreateDuotone(Rgba64 dark, Rgba64 light)
    {
        Span<double> values = stackalloc double[RowCount * ColumnCount];
        values.Clear();
        ReadOnlySpan<ushort> from = [dark.R, dark.G, dark.B];
        ReadOnlySpan<ushort> to = [light.R, light.G, light.B];
        for (var row = 0; row < 3; row++)
        {
            var start = from[row] / (double)ushort.MaxValue;
            var range = (to[row] / (double)ushort.MaxValue) - start;
            values[row * ColumnCount] = range * LumaR;
            values[(row * ColumnCount) + 1] = range * LumaG;
            values[(row * ColumnCount) + 2] = range * LumaB;
            values[(row * ColumnCount) + 4] = start;
        }

        values[(3 * ColumnCount) + 3] = 1;
        return new ColorMatrix(values);
    }

    // Written so that a value that is not a number is rejected too
    private static void ValidateAmount(double amount, double max, string paramName)
    {
        if (!(amount >= 0 && amount <= max))
            throw new ArgumentOutOfRangeException(paramName, amount, max == 1 ? "The amount must be between 0 and 1." : "The amount must be zero or a positive finite number.");
    }
}
