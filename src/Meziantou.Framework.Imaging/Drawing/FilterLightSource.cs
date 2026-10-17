using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The light of a <see cref="DiffuseLightingPrimitive"/> or of a <see cref="SpecularLightingPrimitive"/>: <see cref="DistantLight"/>, <see cref="PointLight"/> or <see cref="SpotLight"/>.</summary>
/// <remarks>
/// The positions of a light are in the coordinates of the drawing, and its <c>z</c> is a length of the drawing, above
/// the plane: on the canvas, it is multiplied by the square root of the product of the scales of the axes. Lights are
/// immutable and thread-safe.
/// </remarks>
public abstract class FilterLightSource
{
    private protected FilterLightSource()
    {
    }

    /// <summary>Places the light on the canvas of a filter.</summary>
    internal abstract FilterLight Place(FilterProcessor processor);
}
