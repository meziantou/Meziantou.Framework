namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive that moves the pixels of its input by the colors of a map: <c>feDisplacementMap</c> of SVG.</summary>
/// <remarks>
/// A pixel <c>(x, y)</c> of the result is the pixel of the input at <c>(x + scale (X - 1/2), y + scale (Y - 1/2))</c>,
/// with <c>X</c> and <c>Y</c> the components of the pixel of the map that <see cref="XChannel"/> and
/// <see cref="YChannel"/> select.
/// </remarks>
public sealed class SceneDisplacementMapPrimitive : SceneFilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="SceneDisplacementMapPrimitive"/> class.</summary>
    /// <param name="scale">How far a component of 0 or 1 moves a pixel, twice: <c>scale</c>.</param>
    /// <param name="map">The map: <c>in2</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="scale"/> is not a finite number.</exception>
    public SceneDisplacementMapPrimitive(double scale, SceneFilterInput map)
    {
        Scale = FilterValidation.Finite(scale, nameof(scale));
        Map = map;
    }

    /// <summary>Gets how far a component of 0 or 1 moves a pixel, twice.</summary>
    public double Scale { get; }

    /// <summary>Gets the map: <c>in2</c>.</summary>
    public SceneFilterInput Map { get; }

    /// <summary>Gets the input: <c>in</c>.</summary>
    public SceneFilterInput Input { get; init; }

    /// <summary>Gets the component of the map that moves the pixels along x: <c>xChannelSelector</c>. The default is <see cref="SceneColorChannel.Alpha"/>, as in SVG.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="SceneColorChannel"/>.</exception>
    public SceneColorChannel XChannel
    {
        get;
        init => field = FilterValidation.Defined(value, SceneColorChannel.Alpha, nameof(value));
    } = SceneColorChannel.Alpha;

    /// <summary>Gets the component of the map that moves the pixels along y: <c>yChannelSelector</c>. The default is <see cref="SceneColorChannel.Alpha"/>, as in SVG.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="SceneColorChannel"/>.</exception>
    public SceneColorChannel YChannel
    {
        get;
        init => field = FilterValidation.Defined(value, SceneColorChannel.Alpha, nameof(value));
    } = SceneColorChannel.Alpha;

    internal override IEnumerable<SceneFilterInput> GetInputs() => [Input, Map];

    internal override double GetReach() => Math.Abs(Scale) / 2;
}
