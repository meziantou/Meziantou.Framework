namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive that moves its input: <c>feOffset</c> of SVG.</summary>
public sealed class SceneOffsetPrimitive : SceneFilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="SceneOffsetPrimitive"/> class.</summary>
    /// <param name="offsetX">How far the input moves along x: <c>dx</c>.</param>
    /// <param name="offsetY">How far the input moves along y: <c>dy</c>.</param>
    /// <exception cref="ArgumentException">An offset is not a finite number.</exception>
    public SceneOffsetPrimitive(double offsetX, double offsetY)
    {
        OffsetX = FilterValidation.Finite(offsetX, nameof(offsetX));
        OffsetY = FilterValidation.Finite(offsetY, nameof(offsetY));
    }

    /// <summary>Gets how far the input moves along x.</summary>
    public double OffsetX { get; }

    /// <summary>Gets how far the input moves along y.</summary>
    public double OffsetY { get; }

    /// <summary>Gets the input: <c>in</c>.</summary>
    public SceneFilterInput Input { get; init; }

    internal override IEnumerable<SceneFilterInput> GetInputs() => [Input];

    internal override double GetReach() => Math.Max(Math.Abs(OffsetX), Math.Abs(OffsetY));
}
