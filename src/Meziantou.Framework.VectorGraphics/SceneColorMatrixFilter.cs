namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A filter that gives each pixel a new color from its own one, as the <c>feColorMatrix</c> of SVG: the matrix has four
/// rows, for the red, the green, the blue and the alpha of the result, and five columns, for the red, the green, the blue
/// and the alpha of the pixel and a constant, with the values as fractions of one: <c>R' = m0 R + m1 G + m2 B + m3 A + m4</c>.
/// </summary>
/// <remarks>The factories give the matrices of the filter functions of CSS, from "Filter Effects Module Level 1" of the W3C.</remarks>
public sealed class SceneColorMatrixFilter : SceneFilter
{
    /// <summary>Initializes a new instance of the <see cref="SceneColorMatrixFilter"/> class.</summary>
    /// <param name="values">The 20 values of the matrix, by row.</param>
    /// <exception cref="ArgumentException"><paramref name="values"/> does not have 20 values, or one is not finite.</exception>
    public SceneColorMatrixFilter(params ReadOnlySpan<double> values)
    {
        if (values.Length != 20)
            throw new ArgumentException($"A color matrix has 20 values, but {values.Length} were provided.", nameof(values));

        foreach (var value in values)
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("The values of a color matrix must be finite.", nameof(values));
        }

        Values = values.ToArray();
    }

    /// <summary>Gets the 20 values of the matrix, by row.</summary>
    public IReadOnlyList<double> Values { get; }

    /// <summary>Creates the filter of <c>grayscale()</c>: 1 turns the colors to gray, 0 keeps them.</summary>
    /// <param name="amount">The amount, from 0 to 1.</param>
    /// <returns>The filter.</returns>
    public static SceneColorMatrixFilter Grayscale(double amount = 1)
    {
        var s = 1 - Clamp(amount);
        return new(
            0.2126 + (0.7874 * s), 0.7152 - (0.7152 * s), 0.0722 - (0.0722 * s), 0, 0,
            0.2126 - (0.2126 * s), 0.7152 + (0.2848 * s), 0.0722 - (0.0722 * s), 0, 0,
            0.2126 - (0.2126 * s), 0.7152 - (0.7152 * s), 0.0722 + (0.9278 * s), 0, 0,
            0, 0, 0, 1, 0);
    }

    /// <summary>Creates the filter of <c>sepia()</c>: 1 turns the colors to sepia, 0 keeps them.</summary>
    /// <param name="amount">The amount, from 0 to 1.</param>
    /// <returns>The filter.</returns>
    public static SceneColorMatrixFilter Sepia(double amount = 1)
    {
        var s = 1 - Clamp(amount);
        return new(
            0.393 + (0.607 * s), 0.769 - (0.769 * s), 0.189 - (0.189 * s), 0, 0,
            0.349 - (0.349 * s), 0.686 + (0.314 * s), 0.168 - (0.168 * s), 0, 0,
            0.272 - (0.272 * s), 0.534 - (0.534 * s), 0.131 + (0.869 * s), 0, 0,
            0, 0, 0, 1, 0);
    }

    /// <summary>Creates the filter of <c>saturate()</c>: 0 is gray, 1 keeps the colors, more saturates them.</summary>
    /// <param name="amount">The amount, 0 or more.</param>
    /// <returns>The filter.</returns>
    public static SceneColorMatrixFilter Saturate(double amount)
    {
        var s = double.IsFinite(amount) ? Math.Max(0, amount) : 1;
        return new(
            0.213 + (0.787 * s), 0.715 - (0.715 * s), 0.072 - (0.072 * s), 0, 0,
            0.213 - (0.213 * s), 0.715 + (0.285 * s), 0.072 - (0.072 * s), 0, 0,
            0.213 - (0.213 * s), 0.715 - (0.715 * s), 0.072 + (0.928 * s), 0, 0,
            0, 0, 0, 1, 0);
    }

    /// <summary>Creates the filter of <c>hue-rotate()</c>: rotates the hues of the colors.</summary>
    /// <param name="degrees">The angle, in degrees.</param>
    /// <returns>The filter.</returns>
    public static SceneColorMatrixFilter HueRotate(double degrees)
    {
        var (sin, cos) = Math.SinCos((double.IsFinite(degrees) ? degrees : 0) * Math.PI / 180);
        return new(
            0.213 + (cos * 0.787) - (sin * 0.213), 0.715 - (cos * 0.715) - (sin * 0.715), 0.072 - (cos * 0.072) + (sin * 0.928), 0, 0,
            0.213 - (cos * 0.213) + (sin * 0.143), 0.715 + (cos * 0.285) + (sin * 0.140), 0.072 - (cos * 0.072) - (sin * 0.283), 0, 0,
            0.213 - (cos * 0.213) - (sin * 0.787), 0.715 - (cos * 0.715) + (sin * 0.715), 0.072 + (cos * 0.928) + (sin * 0.072), 0, 0,
            0, 0, 0, 1, 0);
    }

    /// <summary>Creates the filter of <c>invert()</c>: 1 inverts the colors, 0 keeps them.</summary>
    /// <param name="amount">The amount, from 0 to 1.</param>
    /// <returns>The filter.</returns>
    public static SceneColorMatrixFilter Invert(double amount = 1)
    {
        var a = Clamp(amount);
        return new(
            1 - (2 * a), 0, 0, 0, a,
            0, 1 - (2 * a), 0, 0, a,
            0, 0, 1 - (2 * a), 0, a,
            0, 0, 0, 1, 0);
    }

    /// <summary>Creates the filter of <c>brightness()</c>: 0 is black, 1 keeps the colors, more brightens them.</summary>
    /// <param name="amount">The amount, 0 or more.</param>
    /// <returns>The filter.</returns>
    public static SceneColorMatrixFilter Brightness(double amount)
    {
        var a = double.IsFinite(amount) ? Math.Max(0, amount) : 1;
        return new(
            a, 0, 0, 0, 0,
            0, a, 0, 0, 0,
            0, 0, a, 0, 0,
            0, 0, 0, 1, 0);
    }

    /// <summary>Creates the filter of <c>contrast()</c>: 0 is gray, 1 keeps the colors, more increases the contrast.</summary>
    /// <param name="amount">The amount, 0 or more.</param>
    /// <returns>The filter.</returns>
    public static SceneColorMatrixFilter Contrast(double amount)
    {
        var a = double.IsFinite(amount) ? Math.Max(0, amount) : 1;
        var offset = (1 - a) / 2;
        return new(
            a, 0, 0, 0, offset,
            0, a, 0, 0, offset,
            0, 0, a, 0, offset,
            0, 0, 0, 1, 0);
    }

    private static double Clamp(double amount) => double.IsFinite(amount) ? Math.Clamp(amount, 0, 1) : 1;
}
