namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive that draws its input over its shadow: <c>feDropShadow</c> of SVG.</summary>
public sealed class SceneDropShadowPrimitive : SceneFilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="SceneDropShadowPrimitive"/> class.</summary>
    /// <param name="offsetX">The offset of the shadow along x: <c>dx</c>.</param>
    /// <param name="offsetY">The offset of the shadow along y: <c>dy</c>.</param>
    /// <param name="standardDeviationX">The standard deviation of the blur of the shadow along x: <c>stdDeviation</c>.</param>
    /// <param name="standardDeviationY">The standard deviation of the blur of the shadow along y.</param>
    /// <param name="color">The color of the shadow: <c>flood-color</c> and <c>flood-opacity</c>.</param>
    /// <exception cref="ArgumentException">An offset or a standard deviation is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A standard deviation is negative.</exception>
    public SceneDropShadowPrimitive(double offsetX, double offsetY, double standardDeviationX, double standardDeviationY, SceneColor color)
    {
        OffsetX = FilterValidation.Finite(offsetX, nameof(offsetX));
        OffsetY = FilterValidation.Finite(offsetY, nameof(offsetY));
        StandardDeviationX = FilterValidation.NonNegative(standardDeviationX, nameof(standardDeviationX));
        StandardDeviationY = FilterValidation.NonNegative(standardDeviationY, nameof(standardDeviationY));
        Color = color;
    }

    /// <summary>Gets the offset of the shadow along x.</summary>
    public double OffsetX { get; }

    /// <summary>Gets the offset of the shadow along y.</summary>
    public double OffsetY { get; }

    /// <summary>Gets the standard deviation of the blur of the shadow along x.</summary>
    public double StandardDeviationX { get; }

    /// <summary>Gets the standard deviation of the blur of the shadow along y.</summary>
    public double StandardDeviationY { get; }

    /// <summary>Gets the color of the shadow.</summary>
    public SceneColor Color { get; }

    /// <summary>Gets the input: <c>in</c>.</summary>
    public SceneFilterInput Input { get; init; }

    internal override IEnumerable<SceneFilterInput> GetInputs() => [Input];

    internal override double GetReach() => (3 * Math.Max(StandardDeviationX, StandardDeviationY)) + Math.Max(Math.Abs(OffsetX), Math.Abs(OffsetY));
}
