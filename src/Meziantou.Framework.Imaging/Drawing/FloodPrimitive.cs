using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that fills its subregion with a color: <c>feFlood</c> of SVG.</summary>
/// <remarks>It has no input: by default, its subregion is the region of the filter.</remarks>
public sealed class FloodPrimitive : FilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="FloodPrimitive"/> class.</summary>
    /// <param name="color">The color, with straight alpha: <c>flood-color</c> and <c>flood-opacity</c>. An <see cref="Rgba32"/> converts implicitly.</param>
    public FloodPrimitive(Rgba64 color)
    {
        Color = color;
    }

    /// <summary>Gets the color.</summary>
    public Rgba64 Color { get; }

    internal override int InputCount => 0;

    internal override bool IsColorSpaceNeutral => true;

    internal override FilterInput GetInput(int index) => throw new ArgumentOutOfRangeException(nameof(index));

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
    {
        var (red, green, blue, alpha) = context.GetColor(Color);
        var r = (float)(red * alpha);
        var g = (float)(green * alpha);
        var b = (float)(blue * alpha);
        var a = (float)alpha;
        for (var y = subregion.Top; y < subregion.Bottom; y++)
        {
            var values = result.GetRow(y).Slice(4 * (subregion.Left - result.Bounds.Left), 4 * subregion.Width);
            for (var i = 0; i < values.Length; i += 4)
            {
                values[i] = r;
                values[i + 1] = g;
                values[i + 2] = b;
                values[i + 3] = a;
            }
        }
    }
}
