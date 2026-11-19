using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Dom;

// HTML-in-canvas (https://github.com/WICG/html-in-canvas): drawElementImage()
public abstract partial class CanvasRenderingContext2DBase
{
    /// <summary>
    /// Draws the last snapshot of a drawable element of the canvas at its default size (see
    /// <see cref="HtmlCanvasElement.GetElementImageDefaultSize"/>), and sets its canvas transform to where it is drawn (unless
    /// <see cref="DrawElementImageOptions.PreserveElementGeometry"/> is true).
    /// </summary>
    /// <param name="element">A drawable element of the canvas, or an <see cref="ElementImage"/> of the canvas.</param>
    /// <param name="dx">The x coordinate of the destination.</param>
    /// <param name="dy">The y coordinate of the destination.</param>
    /// <param name="options">The options.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the element is not a drawable element of the canvas, it has no snapshot, or the image is closed.</exception>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("drawElementImage", HtmlCanvasElement.HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public void DrawElementImage(object? element, double dx, double dy, DrawElementImageOptions? options = null)
        => DrawElementImageCore(element, source: null, dx, dy, destinationSize: null, options);

    /// <summary>Draws the last snapshot of a drawable element of the canvas scaled to a rectangle (see <see cref="DrawElementImage(object, double, double, DrawElementImageOptions?)"/>).</summary>
    /// <param name="element">A drawable element of the canvas, or an <see cref="ElementImage"/> of the canvas.</param>
    /// <param name="dx">The x coordinate of the destination.</param>
    /// <param name="dy">The y coordinate of the destination.</param>
    /// <param name="dwidth">The width of the destination.</param>
    /// <param name="dheight">The height of the destination.</param>
    /// <param name="options">The options.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the element is not a drawable element of the canvas, it has no snapshot, or the image is closed.</exception>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("drawElementImage", HtmlCanvasElement.HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public void DrawElementImage(object? element, double dx, double dy, double dwidth, double dheight, DrawElementImageOptions? options = null)
        => DrawElementImageCore(element, source: null, dx, dy, (dwidth, dheight), options);

    /// <summary>
    /// Draws a rectangle of the last snapshot of a drawable element of the canvas at its size (see
    /// <see cref="DrawElementImage(object, double, double, DrawElementImageOptions?)"/>). The source rectangle is in the coordinates of
    /// the image (the pixels of the bitmap of the canvas), and can extend beyond the border box of the element (its ink overflow).
    /// </summary>
    /// <param name="element">A drawable element of the canvas, or an <see cref="ElementImage"/> of the canvas.</param>
    /// <param name="sx">The x coordinate of the source rectangle.</param>
    /// <param name="sy">The y coordinate of the source rectangle.</param>
    /// <param name="swidth">The width of the source rectangle.</param>
    /// <param name="sheight">The height of the source rectangle.</param>
    /// <param name="dx">The x coordinate of the destination.</param>
    /// <param name="dy">The y coordinate of the destination.</param>
    /// <param name="options">The options.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the element is not a drawable element of the canvas, it has no snapshot, or the image is closed.</exception>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("drawElementImage", HtmlCanvasElement.HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public void DrawElementImage(object? element, double sx, double sy, double swidth, double sheight, double dx, double dy, DrawElementImageOptions? options = null)
        => DrawElementImageCore(element, (sx, sy, swidth, sheight), dx, dy, destinationSize: null, options);

    /// <summary>Draws a rectangle of the last snapshot of a drawable element of the canvas scaled to a rectangle (see <see cref="DrawElementImage(object, double, double, double, double, double, double, DrawElementImageOptions?)"/>).</summary>
    /// <param name="element">A drawable element of the canvas, or an <see cref="ElementImage"/> of the canvas.</param>
    /// <param name="sx">The x coordinate of the source rectangle.</param>
    /// <param name="sy">The y coordinate of the source rectangle.</param>
    /// <param name="swidth">The width of the source rectangle.</param>
    /// <param name="sheight">The height of the source rectangle.</param>
    /// <param name="dx">The x coordinate of the destination.</param>
    /// <param name="dy">The y coordinate of the destination.</param>
    /// <param name="dwidth">The width of the destination.</param>
    /// <param name="dheight">The height of the destination.</param>
    /// <param name="options">The options.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the element is not a drawable element of the canvas, it has no snapshot, or the image is closed.</exception>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("drawElementImage", HtmlCanvasElement.HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public void DrawElementImage(object? element, double sx, double sy, double swidth, double sheight, double dx, double dy, double dwidth, double dheight, DrawElementImageOptions? options = null)
        => DrawElementImageCore(element, (sx, sy, swidth, sheight), dx, dy, (dwidth, dheight), options);

    /// <summary>
    /// Draws a snapshot (https://github.com/WICG/html-in-canvas): its display list (in the CSS pixels of the element) is scaled to
    /// the pixels of the bitmap (the ratio of the size of the bitmap to the size of the content box of the canvas: the image has
    /// its default size), mapped from the source rectangle to the destination rectangle, clipped to the destination rectangle,
    /// and drawn with the current transform, the global alpha, the compositing and the shadows. The canvas transform of the
    /// element is the transform from its border box to the content box of the canvas, in CSS pixels.
    /// </summary>
    private void DrawElementImageCore(object? element, (double X, double Y, double Width, double Height)? source, double dx, double dy, (double Width, double Height)? destinationSize, DrawElementImageOptions? options)
    {
        ArgumentNullException.ThrowIfNull(element);
        var offscreen = (this as OffscreenCanvasRenderingContext2D)?.Canvas;
        var canvas = CanvasElement ?? offscreen?.Placeholder;
        var (snapshot, target, (scaleX, scaleY)) = HtmlCanvasElement.GetDrawableSnapshot(element, canvas, (Bitmap.Width, Bitmap.Height));
        var imageWidth = snapshot.Size.Width * scaleX;
        var imageHeight = snapshot.Size.Height * scaleY;
        var (sx, sy, sw, sh) = source ?? (0, 0, imageWidth, imageHeight);
        var (dw, dh) = destinationSize ?? (source is null ? (imageWidth, imageHeight) : (sw, sh));
        if (!double.IsFinite(sx) || !double.IsFinite(sy) || !double.IsFinite(sw) || !double.IsFinite(sh) || !double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(dw) || !double.IsFinite(dh))
            return;

        // As for drawImage(), a rectangle of zero width or height draws nothing, and the rectangles with a negative width or height
        // are normalized
        if (sw == 0 || sh == 0 || dw == 0 || dh == 0)
            return;

        var sourceRect = NormalizeRect(sx, sy, sw, sh);
        var destinationRect = NormalizeRect(dx, dy, dw, dh);
        var kx = destinationRect.Width / sourceRect.Width;
        var ky = destinationRect.Height / sourceRect.Height;

        // The CSS pixels of the element → the pixels of the image → the destination rectangle
        var imageMatrix = DisplayMatrix.Translation(destinationRect.X - (sourceRect.X * kx), destinationRect.Y - (sourceRect.Y * ky)).PreConcat(DisplayMatrix.Scale(kx * scaleX, ky * scaleY));
        if (Invert(_state.Transform) is not null)
        {
            var alpha = _state.GlobalAlpha;
            var content = snapshot.GetContent();
            DrawShape(builder =>
            {
                builder.Save();
                builder.Transform(_state.Transform);
                builder.Add(new ClipRectItem(destinationRect));
                builder.Transform(imageMatrix);
                if (alpha < 1)
                {
                    builder.SaveLayer(new SaveLayerItem(alpha, BlendMode.Normal, Filters: null));
                }

                builder.AddList(content);
                if (alpha < 1)
                {
                    builder.Restore();
                }

                builder.Restore();
            });
        }

        if (options?.PreserveElementGeometry == true)
            return;

        // The canvas transform maps the element to the content box of the canvas: the pixels of the bitmap are scaled back to CSS
        // pixels of the canvas (it is converted to the CSS pixels of the element when the geometry is updated, see
        // UpdateGeometryFromDrawing). The hit testing is clipped to the source rectangle
        var transform = DisplayMatrix.Scale(1 / scaleX, 1 / scaleY).PreConcat(_state.Transform).PreConcat(imageMatrix);
        var clip = new PhysicalRect(sourceRect.X / scaleX, sourceRect.Y / scaleY, sourceRect.Width / scaleX, sourceRect.Height / scaleY);
        var fallbackRatio = snapshot.ZoomRatio;
        if (offscreen is null)
        {
            canvas!.UpdateGeometryFromDrawing(target, transform, clip, fallbackRatio);
        }
        else
        {
            // The geometry of the elements of the placeholder canvas is updated in its event loop (the OffscreenCanvas may be in a worker)
            canvas!.UpdateGeometryFromOffscreenCanvas(target, Global?.EventLoop, () => canvas.UpdateGeometryFromDrawing(target, transform, clip, fallbackRatio));
        }
    }
}
