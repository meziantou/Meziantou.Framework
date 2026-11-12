namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive of a <see cref="SceneFilterGraph"/>: an element <c>fe*</c> of an SVG filter.</summary>
/// <remarks>
/// A primitive computes an image from its inputs (the drawing of the element, its alpha, or the results of earlier
/// primitives) in its <see cref="Subregion"/>, and nothing outside it. Its lengths and positions are in the coordinates
/// of the parent of the element, as the region of the graph.
/// </remarks>
public abstract class SceneFilterPrimitive
{
    private protected SceneFilterPrimitive()
    {
    }

    /// <summary>
    /// Gets the rectangle the primitive computes its result in (the <c>x</c>, <c>y</c>, <c>width</c> and <c>height</c> of
    /// SVG), or <see langword="null"/> (the default) for the union of the subregions of its inputs, or the region of the
    /// graph when one of its inputs is the drawing of the element or its alpha, or when it has no input.
    /// </summary>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The width or the height is negative.</exception>
    public SceneBounds? Subregion
    {
        get;
        init => field = FilterValidation.Bounds(value, nameof(value));
    }

    /// <summary>Gets the colors the primitive computes with. The default is <see cref="SceneFilterColorSpace.LinearRgb"/>, as in SVG.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="SceneFilterColorSpace"/>.</exception>
    public SceneFilterColorSpace ColorSpace
    {
        get;
        init => field = FilterValidation.Defined(value, SceneFilterColorSpace.Srgb, nameof(value));
    }

    /// <summary>Gets the inputs of the primitive.</summary>
    internal abstract IEnumerable<SceneFilterInput> GetInputs();

    /// <summary>Gets how far the primitive moves what it reads, in the coordinates of the parent of the element.</summary>
    internal virtual double GetReach() => 0;
}
