using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;

#pragma warning disable WEBPLATEXP0017 // The members of the proposal use each other

namespace Meziantou.Framework.WebPlatform.Dom;

// The canvas layers (https://github.com/whatwg/html/pull/9537, https://github.com/fserb/canvas2D/blob/master/spec/layers.md):
// the drawings between beginLayer() and endLayer() are recorded in a display list of their own, which endLayer() draws as
// one image through the drawing model, with the global alpha, the compositing, the shadow, the filter and the clip region
// of the state saved by beginLayer().
public abstract partial class CanvasRenderingContext2DBase
{
    // The explainer of the proposal (the IDL members need a URL with a fragment)
    private const string LayersHref = "https://github.com/fserb/canvas2D/blob/master/spec/layers.md#proposal";

    // The display list of the innermost open layer, or null when no layer is open
    private DisplayListBuilder? _layerBuilder;

    /// <summary>The display list that the drawing operations are added to: the one of the innermost open layer, or the one of the bitmap.</summary>
    private DisplayListBuilder Builder => _layerBuilder ?? Bitmap.Builder;

    /// <summary>Whether a layer opened by <see cref="BeginLayer"/> is not closed yet.</summary>
    internal bool HasOpenLayers => _layerBuilder is not null;

    /// <summary>
    /// Saves the drawing state on the stack and starts a layer (https://github.com/whatwg/html/pull/9537): the following drawing
    /// operations are drawn into a transparent layer, which <see cref="EndLayer"/> draws on the canvas as one image. In the
    /// layer, the global alpha, the compositing operator, the shadows, the filter and the clip region are reset; they apply to
    /// the layer when it is drawn. The transformation matrix is not reset.
    /// </summary>
    /// <param name="options">
    /// The options of the layer: its <c>filter</c>, a CSS filter applied to the layer before the filter of the state, in the
    /// coordinates of the current transformation matrix (the <c>CanvasFilter</c> objects are ignored).
    /// </param>
    /// <remarks>
    /// While a layer is open, the pixels of the canvas cannot be read (<c>getImageData()</c>, <c>toDataURL()</c>,
    /// <c>drawImage()</c> of the canvas...): these methods throw an <c>InvalidStateError</c>. The canvas displays the pixels
    /// drawn before the layer was opened.
    /// </remarks>
    /// <seealso href="https://github.com/fserb/canvas2D/blob/master/spec/layers.md#proposal"/>
    [IdlMember("beginLayer", LayersHref)]
    [Experimental(ExperimentalApis.CanvasLayers, UrlFormat = ExperimentalApis.UrlFormat)]
    public void BeginLayer(BeginLayerOptions? options = null)
    {
        // The filter is resolved before the state changes (with the font of the current state)
        var filter = options?.Filter is string value && ParseFilter(value) is { } parsed ? GetFilters(parsed) : null;

        // A layer opened with a non-invertible transformation matrix is not drawn
        var layer = new CanvasLayer(_layerBuilder, filter, IsDrawn: Invert(_state.Transform) is not null);
        _stack.Add(new StackEntry(_state.Clone(), layer));
        _state.ResetLayerRenderingState();
        _layerBuilder = new DisplayListBuilder();
    }

    /// <summary>
    /// Restores the drawing state saved by <see cref="BeginLayer"/> and draws the layer on the canvas (or on the parent layer)
    /// with the global alpha, the compositing operator, the shadow, the filter and the clip region of this state.
    /// </summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the last state of the stack was not pushed by <see cref="BeginLayer"/> (no layer is open, or a <see cref="Save"/> is not restored).</exception>
    /// <seealso href="https://github.com/fserb/canvas2D/blob/master/spec/layers.md#proposal"/>
    [IdlMember("endLayer", LayersHref)]
    [Experimental(ExperimentalApis.CanvasLayers, UrlFormat = ExperimentalApis.UrlFormat)]
    public void EndLayer()
    {
        if (_stack.Count == 0 || _stack[^1].Layer is not { } layer)
            throw new DomException("There is no layer to end: the last state of the stack was not saved by beginLayer()", "InvalidStateError");

        var content = Builder.ToDisplayList(new PhysicalSize(Bitmap.Width, Bitmap.Height));
        _layerBuilder = layer.ParentBuilder;
        _state = _stack[^1].State;
        _stack.RemoveAt(_stack.Count - 1);
        if (!layer.IsDrawn)
            return;

        // The layer is drawn as an image of the size of the canvas, in the coordinates of the canvas: its drawings are
        // composited with each other in an isolated group (a clearRect() clears the layer, not the canvas). Its own filter
        // applies in the coordinates of the transformation matrix of the layer (unlike the filter of the state).
        var alpha = _state.GlobalAlpha;
        var transform = _state.Transform;
        // With a shadow, the drawings of the layer must not be added twice to the parent (once for the shadow, once for the
        // layer): a shadow at each nested layer would double the size of the display list at each level. The shadow is drawn
        // from an image of the layer instead, as the browsers draw the texture of the layer.
        var shadowContent = ShadowsAreDrawn ? RasterizeForShadow(content) : null;
        DrawShape(builder => AddLayer(builder, content), shadowContent is null ? null : builder => AddLayer(builder, shadowContent));

        void AddLayer(DisplayListBuilder builder, DisplayList list)
        {
            builder.SaveLayer(new SaveLayerItem(alpha, BlendMode.Normal, Filters: null));
            if (layer.Filters is { } filters && Invert(transform) is { } inverse)
            {
                builder.Save();
                builder.Transform(transform);
                builder.SaveLayer(new SaveLayerItem(1, BlendMode.Normal, filters));
                builder.Transform(inverse);
                builder.AddList(list);
                builder.Restore();
                builder.Restore();
            }
            else
            {
                builder.AddList(list);
            }

            builder.Restore();
        }
    }

    /// <summary>
    /// Returns an image of the drawings of a layer for the pass of its shadow, as the browsers draw the texture of the layer: the area that the shadow moves onto the canvas (the canvas moved by the
    /// opposite of the offset), with a margin for the blur and the filters, so its size does not depend on the offset. Returns
    /// null when the image cannot be created: the drawings are then added again.
    /// </summary>
    private DisplayList? RasterizeForShadow(DisplayList content)
    {
        double width = Bitmap.Width;
        double height = Bitmap.Height;
        var margin = Math.Ceiling((_state.ShadowBlur * 1.5) + Math.Max(width, height));
        var x = Math.Round(-_state.ShadowOffsetX, MidpointRounding.AwayFromZero) - margin;
        var y = Math.Round(-_state.ShadowOffsetY, MidpointRounding.AwayFromZero) - margin;
        var imageWidth = width + (2 * margin);
        var imageHeight = height + (2 * margin);
        if (imageWidth * imageHeight > BitmapPixels.MaxPixelCount)
            return null;

        using var surface = Bitmap.Backend.CreateSurface((int)imageWidth, (int)imageHeight, new RenderingSurfaceOptions(HasAlpha: true, Bitmap.Options.ColorSpace));
        if (surface is null)
            return null;

        surface.Draw(content.WithTransform(DisplayMatrix.Translation(-x, -y)));
        var image = new PaintImage("", Bitmap.Backend, surface.CreateSnapshot());
        var builder = new DisplayListBuilder();
        builder.Add(new DrawImageItem(image, new PhysicalRect(0, 0, imageWidth, imageHeight), new PhysicalRect(x, y, imageWidth, imageHeight), ImageSampling.Pixelated));
        return builder.ToDisplayList(content.Size);
    }

    /// <summary>Closes the open layers (and the states saved in them), as <see cref="EndLayer"/> does, e.g. at the end of a paint callback.</summary>
    internal void EndLayers()
    {
        while (HasOpenLayers)
        {
            while (_stack[^1].Layer is null)
            {
                Restore();
            }

            EndLayer();
        }
    }

    /// <summary>Throws an <c>InvalidStateError</c> when a layer is open: the pixels of the canvas cannot be read or replaced.</summary>
    internal void ThrowIfLayersAreOpen()
    {
        if (HasOpenLayers)
            throw new DomException("The pixels of the canvas cannot be accessed while a layer is open", "InvalidStateError");
    }

    /// <summary>An entry of the stack of drawing states: a state saved by <see cref="Save"/>, or by <see cref="BeginLayer"/> with its layer.</summary>
    private readonly record struct StackEntry(DrawingState State, CanvasLayer? Layer);

    /// <summary>A layer opened by <see cref="BeginLayer"/>.</summary>
    /// <param name="ParentBuilder">The display list of the parent layer, or null for the bitmap.</param>
    /// <param name="Filters">The filters of the layer, applied in the coordinates of its transformation matrix before the filters of the state, or null.</param>
    /// <param name="IsDrawn">Whether the layer is drawn: false when it was opened with a non-invertible transformation matrix.</param>
    private sealed record CanvasLayer(DisplayListBuilder? ParentBuilder, DisplayFilter[]? Filters, bool IsDrawn);
}

/// <summary>The options of <see cref="CanvasRenderingContext2DBase.BeginLayer"/> (https://github.com/whatwg/html/pull/9537).</summary>
/// <seealso href="https://github.com/fserb/canvas2D/blob/master/spec/layers.md#proposal"/>
[IdlInterface("BeginLayerOptions", "https://github.com/fserb/canvas2D/blob/master/spec/layers.md#proposal")]
public sealed class BeginLayerOptions
{
    /// <summary>
    /// Gets the filter of the layer: a string is a CSS filter (as <see cref="CanvasRenderingContext2DBase.Filter"/>; an
    /// invalid value is no filter). The other values (the <c>CanvasFilter</c> objects and their descriptions) are ignored.
    /// </summary>
    /// <seealso href="https://github.com/fserb/canvas2D/blob/master/spec/layers.md#proposal"/>
    [IdlMember("filter", "https://github.com/fserb/canvas2D/blob/master/spec/layers.md#proposal")]
    public object? Filter { get; init; }
}
