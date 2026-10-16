using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that applies a color matrix to its input: <c>feColorMatrix</c> of SVG.</summary>
/// <remarks>
/// The matrix is applied as by <see cref="ColorMatrixFilter"/>, to the straight colors of each pixel. The types of
/// <c>feColorMatrix</c> are ready-made matrices: <c>saturate</c> is <see cref="ColorMatrix.CreateSaturation"/>,
/// <c>hueRotate</c> is <see cref="ColorMatrix.CreateHueRotation"/>, and <c>luminanceToAlpha</c> is
/// <see cref="ColorMatrix.LuminanceToAlpha"/>.
/// </remarks>
public sealed class ColorMatrixPrimitive : FilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="ColorMatrixPrimitive"/> class.</summary>
    /// <param name="matrix">The matrix.</param>
    /// <exception cref="ArgumentNullException"><paramref name="matrix"/> is <see langword="null"/>.</exception>
    public ColorMatrixPrimitive(ColorMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        Matrix = matrix;
    }

    /// <summary>Gets the matrix.</summary>
    public ColorMatrix Matrix { get; }

    /// <summary>Gets the input: <c>in</c>.</summary>
    public FilterInput Input { get; init; }

    internal override int InputCount => 1;

    internal override FilterInput GetInput(int index) => index == 0 ? Input : throw new ArgumentOutOfRangeException(nameof(index));

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
    {
        FilterGraphContext.Copy(context.GetInput(0), result, subregion);
        context.Processor.ApplyColorMatrix(result, Matrix, subregion);
    }
}
