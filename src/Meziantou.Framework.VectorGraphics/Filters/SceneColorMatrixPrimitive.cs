namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A primitive that gives each pixel of its input a new color from its own one: <c>feColorMatrix</c> of SVG, with the
/// matrix of <see cref="SceneColorMatrixFilter"/>.
/// </summary>
public sealed class SceneColorMatrixPrimitive : SceneFilterPrimitive
{
    private readonly double[] _values;

    /// <summary>Initializes a new instance of the <see cref="SceneColorMatrixPrimitive"/> class.</summary>
    /// <param name="values">The 20 values of the matrix, by row: <c>values</c> of <c>type="matrix"</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="values"/> does not have 20 values, or one is not finite.</exception>
    public SceneColorMatrixPrimitive(params ReadOnlySpan<double> values)
    {
        if (values.Length != 20)
            throw new ArgumentException($"A color matrix has 20 values, but {values.Length} were provided.", nameof(values));

        foreach (var value in values)
        {
            FilterValidation.Finite(value, nameof(values));
        }

        _values = values.ToArray();
    }

    /// <summary>Initializes a new instance of the <see cref="SceneColorMatrixPrimitive"/> class with the matrix of a filter, such as <see cref="SceneColorMatrixFilter.Saturate(double)"/>.</summary>
    /// <param name="matrix">The filter whose matrix is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="matrix"/> is <see langword="null"/>.</exception>
    public SceneColorMatrixPrimitive(SceneColorMatrixFilter matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        _values = [.. matrix.Values];
    }

    /// <summary>Gets the matrix of <c>luminanceToAlpha</c>: the alpha becomes the luminance of the color, and the color black.</summary>
    public static SceneColorMatrixPrimitive LuminanceToAlpha => new(
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        0.2125, 0.7154, 0.0721, 0, 0);

    /// <summary>Gets the 20 values of the matrix, by row.</summary>
    public IReadOnlyList<double> Values => _values;

    /// <summary>Gets the input: <c>in</c>.</summary>
    public SceneFilterInput Input { get; init; }

    internal override IEnumerable<SceneFilterInput> GetInputs() => [Input];
}
