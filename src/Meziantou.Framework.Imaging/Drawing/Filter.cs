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

    /// <summary>
    /// Gets the number of steps of a filter that leaves transparent the pixels beyond its reach (<see cref="GetReach"/>)
    /// from the pixels that are not transparent, when it reads transparent pixels beyond the canvas, and that gives
    /// the pixels within it from those pixels alone: such a filter can be applied to those pixels only.
    /// </summary>
    /// <param name="isAfterReach">
    /// <see langword="true"/> when a filter with a reach comes before this one: its running sums leave values of a few
    /// units of the last place beyond its reach, which only a filter that does not divide by the alpha keeps that small.
    /// </param>
    /// <returns>The number of steps, each of which may read one pixel beyond the reach; -1 when the filter is not such a filter.</returns>
    internal virtual int GetLocalSteps(bool isAfterReach) => -1;

    /// <summary>
    /// Gets a rectangle that contains everything the filter paints when what it filters stays inside a rectangle: how
    /// far a blur or a shadow spreads, to size an image or a layer that keeps all of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rectangles are in the coordinates of the drawing the filter is applied to, as its lengths are: before the
    /// transformation of the canvas. Filtered pixels are whole pixels: the pixels the result changes are within the
    /// rectangle rounded out to whole pixels.
    /// </para>
    /// <para>
    /// A blur spreads by three times its standard deviation, rounded up; a drop shadow covers what it filters (unless
    /// <see cref="DropShadowFilter.ShadowOnly"/>) and the same rectangle moved by its offset and spread by its blur; a
    /// color matrix stays in the rectangle; a chain combines the rectangles of its filters; a filter graph stays in its
    /// <see cref="FilterGraph.Region"/>.
    /// </para>
    /// <para>
    /// Some filters paint where nothing is drawn, so nothing bounds them but the canvas: a color matrix that adds to the
    /// alpha, a blur whose <see cref="BlurFilter.EdgeMode"/> reads the edges of the canvas, and a filter graph without a
    /// region (a flood or a turbulence fills it). The method returns <see langword="false"/> for those.
    /// </para>
    /// </remarks>
    /// <param name="bounds">A rectangle that contains what is filtered.</param>
    /// <param name="result">When the method returns <see langword="true"/>, a rectangle that contains what the filter paints; otherwise, an empty rectangle.</param>
    /// <returns><see langword="true"/> when the filter paints within a rectangle; <see langword="false"/> when it can paint anywhere on the canvas.</returns>
    /// <exception cref="ArgumentException">An edge of <paramref name="bounds"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of an edge of <paramref name="bounds"/> exceeds 16777216.</exception>
    public bool TryGetBounds(RectangleD bounds, out RectangleD result)
    {
        DrawingGuard.Rectangle(bounds, nameof(bounds));
        if (TryGetBoundsCore(bounds, out result))
            return true;

        result = default;
        return false;
    }

    /// <summary>Gets a rectangle that contains what the filter paints from what is inside a rectangle; <see langword="false"/> when it can paint anywhere.</summary>
    internal abstract bool TryGetBoundsCore(RectangleD bounds, out RectangleD result);
}
