using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>
/// An effect applied to a drawing as a whole, or to what is behind it: a blur, a color matrix, a drop shadow, or a
/// chain of them, as the <c>filter</c> and <c>backdrop-filter</c> properties of CSS.
/// </summary>
/// <remarks>
/// <para>
/// A filter is applied by <see cref="DrawingCanvas.Filter(Filter, DrawingAction)"/>, to what a callback draws, and by
/// <see cref="DrawingCanvas.BackdropLayer(Filter, RectangleD, double, DrawingAction, CompositeMode, BlendMode)"/>, to
/// what is already drawn behind a rectangle. Its lengths (the deviation of a blur, the offset of a shadow) are lengths
/// of the drawing: a transformation of the canvas that enlarges the drawing enlarges them.
/// </para>
/// <para>
/// <b>Arithmetic.</b> The pixels are read as 16-bit colors and filtered as colors multiplied by their alpha, as
/// fractions of one, kept in single precision between two filters of a chain and summed in double precision. The
/// result is rounded to the samples of the layer it is written to: the alpha as <c>floor(M a + 1/2 + 2^-20)</c> and each
/// color as <c>floor(M p / a + 1/2 + 2^-20)</c>, with <c>M</c> the largest sample value. Filters are immutable and
/// thread-safe.
/// </para>
/// </remarks>
public abstract class Filter
{
    private protected Filter()
    {
    }

    /// <summary>Applies the filter to a surface, in place.</summary>
    internal abstract void Apply(FilterProcessor processor, FilterSurface surface);

    /// <summary>Applies the filter to the pixels of a layer, in place.</summary>
    /// <param name="processor">The processor of the filters.</param>
    /// <param name="frame">The frame of the layer.</param>
    /// <param name="storage">The storage of the frame.</param>
    /// <param name="bounds">The pixels to filter.</param>
    /// <param name="row">A row of at least <paramref name="bounds"/>.Width pixels the pixels are read through.</param>
    internal virtual void ApplyToLayer(FilterProcessor processor, ImageFrame frame, PixelStorage storage, Rectangle bounds, Span<Rgba64> row)
    {
        using var surface = new FilterSurface(processor.Scope, bounds);
        FilterProcessor.Read(frame, storage, surface, row);
        Apply(processor, surface);
        FilterProcessor.Write(surface, storage, bounds);
    }

    /// <summary>Gets how far, in the drawing, the filter moves what it filters: along x and along y.</summary>
    internal abstract (double X, double Y) GetReach();
}
