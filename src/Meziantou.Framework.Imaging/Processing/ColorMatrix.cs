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

    /// <summary>
    /// Gets the matrix that makes the luminance of the color the alpha of a black: <c>luminanceToAlpha</c> of the
    /// <c>feColorMatrix</c> filter of SVG, <c>A' = 0.2125 R + 0.7154 G + 0.0721 B</c>, the colors zero. It turns an image
    /// into a mask: white is opaque, black transparent.
    /// </summary>
    public static ColorMatrix LuminanceToAlpha { get; } = new(
    [
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        0.2125, 0.7154, 0.0721, 0, 0,
    ]);

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

    /// <summary>Creates the matrix that adds a constant to the colors, lighter or darker: <c>c' = c + amount</c>.</summary>
    /// <remarks>
    /// Unlike <see cref="CreateBrightness(double)"/>, which multiplies the colors and keeps black black, the lightness
    /// moves every color by the same amount: the <c>linear</c> transfer function of the <c>feComponentTransfer</c> filter
    /// of SVG with a slope of 1 and an intercept of <paramref name="amount"/>. The alpha is kept.
    /// </remarks>
    /// <param name="amount">The fraction of the range added to each color: from -1 (black) to 1 (white), 0 to change nothing.</param>
    /// <returns>The matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not between -1 and 1.</exception>
    public static ColorMatrix CreateLightness(double amount)
    {
        if (!(amount >= -1 && amount <= 1))
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The amount must be between -1 and 1.");

        if (amount == 0)
            return Identity;

        return new ColorMatrix(
        [
            1, 0, 0, 0, amount,
            0, 1, 0, 0, amount,
            0, 0, 1, 0, amount,
            0, 0, 0, 1, 0,
        ]);
    }

    /// <summary>
    /// Gets the matrix that gives the colors the cool, saturated look of an instant photograph: the <c>polaroid()</c>
    /// preset of the <c>ColorMatrixFilter</c> of PixiJS 8.6.6 (MIT license), written as published.
    /// </summary>
    public static ColorMatrix Polaroid { get; } = new(
    [
        1.438, -0.062, -0.062, 0, 0,
        -0.122, 1.378, -0.122, 0, 0,
        -0.016, -0.016, 1.483, 0, 0,
        0, 0, 0, 1, 0,
    ]);

    /// <summary>
    /// Gets the matrix that gives the colors the warm contrast of a Kodachrome slide: the <c>kodachrome()</c> preset of the
    /// <c>ColorMatrixFilter</c> of PixiJS 8.6.6 (MIT license), whose constants are published on the 0 to 255 scale and are
    /// divided by 255 here, as PixiJS does.
    /// </summary>
    public static ColorMatrix Kodachrome { get; } = new(
    [
        1.1285582396593525, -0.3967382283601348, -0.03992559172921793, 0, 63.72958762196502 / 255,
        -0.16404339962244616, 1.0835251566291304, -0.05498805115633132, 0, 24.732407896706203 / 255,
        -0.16786010706155763, -0.5603416277695248, 1.6014850761964943, 0, 35.62982807460946 / 255,
        0, 0, 0, 1, 0,
    ]);

    /// <summary>
    /// Gets the matrix that gives the colors the strong, shifted tones of a Lomography camera: each color scaled and
    /// moved on its own, <c>R' = 1.5 R - 0.1</c>, <c>G' = 1.45 G + 0.05</c>, <c>B' = 1.09 B - 0.08</c>. It is the
    /// <c>Lomograph</c> matrix of ImageProcessor (James Jackson-South, Apache License 2.0).
    /// </summary>
    /// <remarks>Apply <see cref="ImageProcessingExtensions.Vignette(Image, Rgba64, double, double, Rectangle?, CancellationToken)"/> too for the dark corners of the look.</remarks>
    public static ColorMatrix Lomograph { get; } = new(
    [
        1.5, 0, 0, 0, -0.1,
        0, 1.45, 0, 0, 0.05,
        0, 0, 1.09, 0, -0.08,
        0, 0, 0, 1, 0,
    ]);

    /// <summary>
    /// Gets the matrix that makes a high-contrast black and white image: every color becomes <c>1.5 (R + G + B) - 1</c>,
    /// so that most pixels are black or white and only the middle tones stay gray. It is the <c>BlackWhite</c> matrix of
    /// ImageProcessor (James Jackson-South, Apache License 2.0). For gray levels that keep the tones, see
    /// <see cref="CreateGrayscale(double)"/>; for pixels that are only black or white, see
    /// <see cref="ImageProcessingExtensions.Threshold(Image, ThresholdOptions?, CancellationToken)"/>.
    /// </summary>
    public static ColorMatrix BlackWhite { get; } = new(
    [
        1.5, 1.5, 1.5, 0, -1,
        1.5, 1.5, 1.5, 0, -1,
        1.5, 1.5, 1.5, 0, -1,
        0, 0, 0, 1, 0,
    ]);

    /// <summary>
    /// Creates the matrix that shows the colors as a person with a color vision deficiency sees them: a dichromacy, or an
    /// anomalous trichromacy of severity 0.5.
    /// </summary>
    /// <remarks>See <see cref="CreateColorBlindness(ColorBlindnessType, double)"/> for the model and its coefficients.</remarks>
    /// <param name="type">The deficiency.</param>
    /// <returns>The matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a defined value.</exception>
    public static ColorMatrix CreateColorBlindness(ColorBlindnessType type)
    {
        return type switch
        {
            ColorBlindnessType.Protanopia or ColorBlindnessType.Deuteranopia or ColorBlindnessType.Tritanopia => CreateMachadoMatrix(type, 1),
            ColorBlindnessType.Protanomaly or ColorBlindnessType.Deuteranomaly or ColorBlindnessType.Tritanomaly => CreateMachadoMatrix(type, DefaultAnomalySeverity),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The color blindness type is not valid."),
        };
    }

    /// <summary>
    /// Creates the matrix that shows the colors as a person with an anomalous trichromacy of a given severity sees them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The coefficients are those of Table 1 of G. M. Machado, M. M. Oliveira and L. A. F. Fernandes, "A
    /// Physiologically-based Model for Simulation of Color Vision Deficiency", IEEE Transactions on Visualization and
    /// Computer Graphics 15(6), 2009, pp. 1291-1298, published for the severities 0, 0.1, 0.2 ... 1: a severity between
    /// two of them is interpolated linearly between their matrices, as the authors recommend. A severity of 0 changes
    /// nothing, and a severity of 1 is the dichromacy (<see cref="ColorBlindnessType.Protanopia"/> for
    /// <see cref="ColorBlindnessType.Protanomaly"/>, and so on). The alpha is kept.
    /// </para>
    /// <para>
    /// The model works on linear RGB. Like every matrix, this one is applied to the stored sample values: it is exact on a
    /// linear-light image (an <see cref="RgbaFloat"/> image converted from the sRGB colors), and an approximation on
    /// sRGB-encoded samples, which is how most simulators apply it.
    /// </para>
    /// </remarks>
    /// <param name="type">The deficiency: <see cref="ColorBlindnessType.Protanomaly"/>, <see cref="ColorBlindnessType.Deuteranomaly"/> or <see cref="ColorBlindnessType.Tritanomaly"/>.</param>
    /// <param name="severity">How far the sensitivity of the cones is shifted: from 0 (normal vision) to 1 (the dichromacy).</param>
    /// <returns>The matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a defined value, or <paramref name="severity"/> is not between 0 and 1.</exception>
    /// <exception cref="ArgumentException"><paramref name="type"/> is a dichromacy, which has no severity: use <see cref="CreateColorBlindness(ColorBlindnessType)"/>.</exception>
    public static ColorMatrix CreateColorBlindness(ColorBlindnessType type, double severity)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type), type, "The color blindness type is not valid.");

        if (type is ColorBlindnessType.Protanopia or ColorBlindnessType.Deuteranopia or ColorBlindnessType.Tritanopia)
            throw new ArgumentException("A dichromacy has no severity: use the anomalous trichromacy of the same cones, or the overload without a severity.", nameof(type));

        ValidateAmount(severity, 1, nameof(severity));
        return CreateMachadoMatrix(type, severity);
    }

    private const double DefaultAnomalySeverity = 0.5;

    private static ColorMatrix CreateMachadoMatrix(ColorBlindnessType type, double severity)
    {
        if (severity == 0)
            return Identity;

        var table = type switch
        {
            ColorBlindnessType.Protanopia or ColorBlindnessType.Protanomaly => MachadoProtan,
            ColorBlindnessType.Deuteranopia or ColorBlindnessType.Deuteranomaly => MachadoDeutan,
            _ => MachadoTritan,
        };

        // The rows of the table are 0.1 apart: a severity close to one of them (0.3 is 2.9999... tenths) takes it as it is
        var position = severity * 10;
        var nearest = Math.Round(position, MidpointRounding.ToEven);
        if (Math.Abs(position - nearest) < 1e-9)
        {
            position = nearest;
        }

        var lower = Math.Min((int)position, 9);
        var weight = position - lower;
        Span<double> values = stackalloc double[RowCount * ColumnCount];
        values.Clear();
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var low = table[(lower * 9) + (row * 3) + column];
                var high = table[((lower + 1) * 9) + (row * 3) + column];
                values[(row * ColumnCount) + column] = weight == 0 ? low : low + ((high - low) * weight);
            }
        }

        values[(3 * ColumnCount) + 3] = 1;
        return new ColorMatrix(values);
    }

    // Table 1 of Machado, Oliveira and Fernandes (2009): the 3x3 matrices of the severities 0, 0.1 ... 1, row by row
    private static ReadOnlySpan<double> MachadoProtan =>
    [
        1.000000, 0.000000, -0.000000, 0.000000, 1.000000, 0.000000, -0.000000, -0.000000, 1.000000,
        0.856167, 0.182038, -0.038205, 0.029342, 0.955115, 0.015544, -0.002880, -0.001563, 1.004443,
        0.734766, 0.334872, -0.069637, 0.051840, 0.919198, 0.028963, -0.004928, -0.004209, 1.009137,
        0.630323, 0.465641, -0.095964, 0.069181, 0.890046, 0.040773, -0.006308, -0.007724, 1.014032,
        0.539009, 0.579343, -0.118352, 0.082546, 0.866121, 0.051332, -0.007136, -0.011959, 1.019095,
        0.458064, 0.679578, -0.137642, 0.092785, 0.846313, 0.060902, -0.007494, -0.016807, 1.024301,
        0.385450, 0.769005, -0.154455, 0.100526, 0.829802, 0.069673, -0.007442, -0.022190, 1.029632,
        0.319627, 0.849633, -0.169261, 0.106241, 0.815969, 0.077790, -0.007025, -0.028051, 1.035076,
        0.259411, 0.923008, -0.182420, 0.110296, 0.804340, 0.085364, -0.006276, -0.034346, 1.040622,
        0.203876, 0.990338, -0.194214, 0.112975, 0.794542, 0.092483, -0.005222, -0.041043, 1.046265,
        0.152286, 1.052583, -0.204868, 0.114503, 0.786281, 0.099216, -0.003882, -0.048116, 1.051998,
    ];

    private static ReadOnlySpan<double> MachadoDeutan =>
    [
        1.000000, 0.000000, -0.000000, 0.000000, 1.000000, 0.000000, -0.000000, -0.000000, 1.000000,
        0.866435, 0.177704, -0.044139, 0.049567, 0.939063, 0.011370, -0.003453, 0.007233, 0.996220,
        0.760729, 0.319078, -0.079807, 0.090568, 0.889315, 0.020117, -0.006027, 0.013325, 0.992702,
        0.675425, 0.433850, -0.109275, 0.125303, 0.847755, 0.026942, -0.007950, 0.018572, 0.989378,
        0.605511, 0.528560, -0.134071, 0.155318, 0.812366, 0.032316, -0.009376, 0.023176, 0.986200,
        0.547494, 0.607765, -0.155259, 0.181692, 0.781742, 0.036566, -0.010410, 0.027275, 0.983136,
        0.498864, 0.674741, -0.173604, 0.205199, 0.754872, 0.039929, -0.011131, 0.030969, 0.980162,
        0.457771, 0.731899, -0.189670, 0.226409, 0.731012, 0.042579, -0.011595, 0.034333, 0.977261,
        0.422823, 0.781057, -0.203881, 0.245752, 0.709602, 0.044646, -0.011843, 0.037423, 0.974421,
        0.392952, 0.823610, -0.216562, 0.263559, 0.690210, 0.046232, -0.011910, 0.040281, 0.971630,
        0.367322, 0.860646, -0.227968, 0.280085, 0.672501, 0.047413, -0.011820, 0.042940, 0.968881,
    ];

    private static ReadOnlySpan<double> MachadoTritan =>
    [
        1.000000, 0.000000, -0.000000, 0.000000, 1.000000, 0.000000, -0.000000, -0.000000, 1.000000,
        0.926670, 0.092514, -0.019184, 0.021191, 0.964503, 0.014306, 0.008437, 0.054813, 0.936750,
        0.895720, 0.133330, -0.029050, 0.029997, 0.945400, 0.024603, 0.013027, 0.104707, 0.882266,
        0.905871, 0.127791, -0.033662, 0.026856, 0.941251, 0.031893, 0.013410, 0.148296, 0.838294,
        0.948035, 0.089490, -0.037526, 0.014364, 0.946792, 0.038844, 0.010853, 0.193991, 0.795156,
        1.017277, 0.027029, -0.044306, -0.006113, 0.958479, 0.047634, 0.006379, 0.248708, 0.744913,
        1.104996, -0.046633, -0.058363, -0.032137, 0.971635, 0.060503, 0.001336, 0.317922, 0.680742,
        1.193214, -0.109812, -0.083402, -0.058496, 0.979410, 0.079086, -0.002346, 0.403492, 0.598854,
        1.257728, -0.139648, -0.118081, -0.078003, 0.975409, 0.102594, -0.003316, 0.501214, 0.502102,
        1.278864, -0.125333, -0.153531, -0.084748, 0.957674, 0.127074, -0.000989, 0.601151, 0.399838,
        1.255528, -0.076749, -0.178779, -0.078411, 0.930809, 0.147602, 0.004733, 0.691367, 0.303900,
    ];

    // Written so that a value that is not a number is rejected too
    private static void ValidateAmount(double amount, double max, string paramName)
    {
        if (!(amount >= 0 && amount <= max))
            throw new ArgumentOutOfRangeException(paramName, amount, max == 1 ? "The amount must be between 0 and 1." : "The amount must be zero or a positive finite number.");
    }
}
