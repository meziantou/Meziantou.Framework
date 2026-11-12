namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive that blurs its input: <c>feGaussianBlur</c> of SVG.</summary>
public sealed class SceneGaussianBlurPrimitive : SceneFilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="SceneGaussianBlurPrimitive"/> class with the same standard deviation along both axes.</summary>
    /// <param name="standardDeviation">The standard deviation of the blur: <c>stdDeviation</c>. 0 does not blur.</param>
    /// <exception cref="ArgumentException"><paramref name="standardDeviation"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="standardDeviation"/> is negative.</exception>
    public SceneGaussianBlurPrimitive(double standardDeviation)
        : this(standardDeviation, standardDeviation)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SceneGaussianBlurPrimitive"/> class.</summary>
    /// <param name="standardDeviationX">The standard deviation of the blur along x. 0 does not blur along x.</param>
    /// <param name="standardDeviationY">The standard deviation of the blur along y. 0 does not blur along y.</param>
    /// <exception cref="ArgumentException">A standard deviation is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A standard deviation is negative.</exception>
    public SceneGaussianBlurPrimitive(double standardDeviationX, double standardDeviationY)
    {
        StandardDeviationX = FilterValidation.NonNegative(standardDeviationX, nameof(standardDeviationX));
        StandardDeviationY = FilterValidation.NonNegative(standardDeviationY, nameof(standardDeviationY));
    }

    /// <summary>Gets the standard deviation of the blur along x.</summary>
    public double StandardDeviationX { get; }

    /// <summary>Gets the standard deviation of the blur along y.</summary>
    public double StandardDeviationY { get; }

    /// <summary>Gets what the blur reads beyond the edges of its input: <c>edgeMode</c>. The default is <see cref="SceneFilterEdgeMode.None"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="SceneFilterEdgeMode"/>.</exception>
    public SceneFilterEdgeMode EdgeMode
    {
        get;
        init => field = FilterValidation.Defined(value, SceneFilterEdgeMode.Mirror, nameof(value));
    }

    /// <summary>Gets the input: <c>in</c>.</summary>
    public SceneFilterInput Input { get; init; }

    internal override IEnumerable<SceneFilterInput> GetInputs() => [Input];

    internal override double GetReach() => 3 * Math.Max(StandardDeviationX, StandardDeviationY);
}
