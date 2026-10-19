using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A color matrix, as a filter: the filter functions of CSS that change colors, <c>feColorMatrix</c> of SVG.</summary>
/// <remarks>
/// The matrix is applied to the colors of each pixel without their alpha (straight), as fractions of one, and the
/// result is clipped to the range from 0 to 1: <c>R' = m00 R + m01 G + m02 B + m03 A + m04</c>, and so on. A
/// transparent pixel has the color black: a matrix with a constant in its row of alpha makes it visible.
/// </remarks>
public sealed class ColorMatrixFilter : Filter
{
    /// <summary>Initializes a new instance of the <see cref="ColorMatrixFilter"/> class.</summary>
    /// <param name="matrix">The matrix.</param>
    /// <exception cref="ArgumentNullException"><paramref name="matrix"/> is <see langword="null"/>.</exception>
    public ColorMatrixFilter(ColorMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        Matrix = matrix;
    }

    /// <summary>Gets the matrix.</summary>
    public ColorMatrix Matrix { get; }

    internal override void Apply(FilterProcessor processor, FilterSurface surface) => processor.ApplyColorMatrix(surface, Matrix);

    internal override (double X, double Y) GetReach() => (0, 0);

    // A transparent pixel stays transparent when the matrix adds no alpha. The straight color of a value left by the
    // running sums of a blur is anything: the matrix could make it visible
    internal override int GetLocalSteps(bool isAfterReach) => !isAfterReach && !(Matrix.Values[19] > 0) ? 1 : -1;
}
