using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive of a <see cref="FilterGraph"/>: an element <c>fe*</c> of an SVG filter.</summary>
/// <remarks>
/// A primitive computes an image from its inputs (the drawing, its alpha, or the results of earlier primitives) in
/// its <see cref="Subregion"/>, and nothing outside it. Its lengths are lengths of the drawing.
/// </remarks>
public abstract class FilterPrimitive
{
    private protected FilterPrimitive()
    {
    }

    /// <summary>
    /// Gets the rectangle of the drawing the primitive computes its result in, or <see langword="null"/> (the default)
    /// for the union of the subregions of its inputs, or the region of the filter when one of its inputs is the drawing
    /// or its alpha, or when it has no input (<c>x</c>, <c>y</c>, <c>width</c> and <c>height</c> of SVG).
    /// </summary>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public RectangleD? Subregion
    {
        get;
        init
        {
            if (value is { } rectangle)
            {
                DrawingGuard.Rectangle(rectangle, nameof(value));
            }

            field = value;
        }
    }

    /// <summary>Gets the colors the primitive computes with. Defaults to <see cref="FilterColorSpace.LinearRgb"/>, as in SVG.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="FilterColorSpace"/>.</exception>
    public FilterColorSpace ColorSpace
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The color space is not valid.");

            field = value;
        }
    }

    /// <summary>Gets the number of inputs of the primitive.</summary>
    internal abstract int InputCount { get; }

    /// <summary>Gets an input of the primitive.</summary>
    internal abstract FilterInput GetInput(int index);

    /// <summary>Gets a value indicating whether the color space of the primitive makes no difference to its result: it copies or moves pixels, or makes a color of its own.</summary>
    /// <remarks>The result of such a primitive is in the colors of its first input (sRGB when it has none), which are not converted.</remarks>
    internal virtual bool IsColorSpaceNeutral => false;

    /// <summary>Computes the result of the primitive in the part of its subregion on the surface (which is transparent when it is called), from its inputs (<see cref="FilterGraphContext.GetInput"/>).</summary>
    internal abstract void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion);

    /// <summary>Gets how far, in the drawing, the primitive moves what it reads.</summary>
    internal virtual (double X, double Y) GetReach() => (0, 0);
}
