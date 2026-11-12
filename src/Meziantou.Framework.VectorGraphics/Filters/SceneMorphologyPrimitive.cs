namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive that makes the shapes of its input thinner or thicker: <c>feMorphology</c> of SVG.</summary>
/// <remarks>Each component of a pixel, the colors multiplied by their alpha, is the smallest or the largest one on the rectangle of the radii around it.</remarks>
public sealed class SceneMorphologyPrimitive : SceneFilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="SceneMorphologyPrimitive"/> class.</summary>
    /// <param name="operator">Whether the shapes get thinner or thicker.</param>
    /// <param name="radiusX">The radius along x: <c>radius</c>. 0 leaves that axis as it is.</param>
    /// <param name="radiusY">The radius along y. 0 leaves that axis as it is.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="operator"/> is not defined, or a radius is negative.</exception>
    /// <exception cref="ArgumentException">A radius is not a finite number.</exception>
    public SceneMorphologyPrimitive(SceneMorphologyOperator @operator, double radiusX, double radiusY)
    {
        Operator = FilterValidation.Defined(@operator, SceneMorphologyOperator.Dilate, nameof(@operator));
        RadiusX = FilterValidation.NonNegative(radiusX, nameof(radiusX));
        RadiusY = FilterValidation.NonNegative(radiusY, nameof(radiusY));
    }

    /// <summary>Gets whether the shapes get thinner or thicker.</summary>
    public SceneMorphologyOperator Operator { get; }

    /// <summary>Gets the radius along x.</summary>
    public double RadiusX { get; }

    /// <summary>Gets the radius along y.</summary>
    public double RadiusY { get; }

    /// <summary>Gets the input: <c>in</c>.</summary>
    public SceneFilterInput Input { get; init; }

    internal override IEnumerable<SceneFilterInput> GetInputs() => [Input];

    internal override double GetReach() => Math.Max(RadiusX, RadiusY);
}
