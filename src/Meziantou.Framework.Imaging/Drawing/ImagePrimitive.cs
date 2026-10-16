using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive whose result is what a callback draws: <c>feImage</c> of SVG, of an image or of an element.</summary>
/// <remarks>
/// The callback draws in the coordinates of the drawing that is filtered, through the same transformation of the
/// canvas, on a transparent image of the part of the subregion of the primitive that is computed (its canvas is that
/// image): an image is drawn with <see cref="DrawingCanvas.DrawImage(ImageFrame, RectangleD, ImageSampling, double)"/>,
/// an element with its own draw calls. The result is in sRGB, whatever <see cref="FilterPrimitive.ColorSpace"/> says,
/// and converted when a primitive in linear light reads it. It has no input: by default, its subregion is the region of
/// the filter.
/// </remarks>
public sealed class ImagePrimitive : FilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="ImagePrimitive"/> class.</summary>
    /// <param name="drawing">The callback that draws the result. It runs each time the filter is applied, and must not retain the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="drawing"/> is <see langword="null"/>.</exception>
    public ImagePrimitive(DrawingAction drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        Drawing = drawing;
    }

    /// <summary>Gets the callback that draws the result.</summary>
    public DrawingAction Drawing { get; }

    internal override int InputCount => 0;

    internal override bool IsColorSpaceNeutral => true;

    internal override FilterInput GetInput(int index) => throw new ArgumentOutOfRangeException(nameof(index));

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion) => context.Processor.Draw(Drawing, result, subregion);
}
