using Meziantou.Framework.WebPlatform.Css.Layout;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.WebPlatform.Dom;

// HTML-in-canvas (https://github.com/WICG/html-in-canvas): the element geometries of an OffscreenCanvas, applied to the elements of
// its placeholder canvas
public sealed partial class OffscreenCanvas
{
    /// <summary>
    /// Sets the canvas transform of an element of the placeholder canvas (see <see cref="HtmlCanvasElement.UpdateElementGeometry"/>):
    /// the updates are batched, and an <c>elementgeometryupdate</c> event is fired at the placeholder canvas in a microtask.
    /// </summary>
    /// <param name="element">An element of the placeholder canvas, or an <see cref="ElementImage"/> of the placeholder canvas.</param>
    /// <param name="options">The transform and the clip of the element.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the canvas has no placeholder, or the element is not a drawable element of its placeholder.</exception>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("updateElementGeometry", HtmlCanvasElement.HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public void UpdateElementGeometry(object? element, UpdateElementGeometryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        var placeholder = GetPlaceholderForElementImages();
        if (element is ElementImage image)
        {
            ValidateElementImage(placeholder, image);
            var matrix = options?.CanvasTransform is { } init ? DomMatrix.FromMatrixInit(init) : null;
            var clip = options?.CanvasClip is { } clipInit
                ? new PhysicalRect(Math.Min(clipInit.X, clipInit.X + clipInit.Width), Math.Min(clipInit.Y, clipInit.Y + clipInit.Height), Math.Abs(clipInit.Width), Math.Abs(clipInit.Height))
                : (PhysicalRect?)null;
            var preserveHitTestOrder = options?.PreserveHitTestOrder ?? false;
            placeholder.UpdateGeometryFromOffscreenCanvas(image.Element, RelevantGlobal?.EventLoop, () => placeholder.UpdateGeometry(image.Element, matrix is null ? null : [.. matrix.Elements], matrix?.Is2DMatrix ?? true, clip, preserveHitTestOrder));
            return;
        }

        // (an element is only given on the thread of its document)
        placeholder.UpdateElementGeometry(element, options);
        placeholder.UpdateGeometryFromOffscreenCanvas((Element)element, RelevantGlobal?.EventLoop, () => { });
    }

    /// <summary>Clears the canvas transform of an element of the placeholder canvas (see <see cref="UpdateElementGeometry"/>).</summary>
    /// <param name="element">An element of the placeholder canvas, or an <see cref="ElementImage"/> of the placeholder canvas.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the canvas has no placeholder, or the element is not a drawable element of its placeholder.</exception>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("clearElementGeometry", HtmlCanvasElement.HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public void ClearElementGeometry(object? element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var placeholder = GetPlaceholderForElementImages();
        if (element is ElementImage image)
        {
            ValidateElementImage(placeholder, image);
            placeholder.UpdateGeometryFromOffscreenCanvas(image.Element, RelevantGlobal?.EventLoop, () => placeholder.ClearGeometry(image.Element));
            return;
        }

        placeholder.ClearElementGeometry(element);
        placeholder.UpdateGeometryFromOffscreenCanvas((Element)element, RelevantGlobal?.EventLoop, () => { });
    }

    /// <summary>Returns the size of an element image in the pixels of the bitmap of the canvas (see <see cref="HtmlCanvasElement.GetElementImageDefaultSize"/>).</summary>
    /// <param name="element">An element of the placeholder canvas, or an <see cref="ElementImage"/> of the placeholder canvas.</param>
    /// <returns>The size.</returns>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the canvas has no placeholder, or the element is not a drawable element of its placeholder or has no snapshot.</exception>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("getElementImageDefaultSize", HtmlCanvasElement.HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public ElementImageDefaultSize GetElementImageDefaultSize(object? element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var placeholder = GetPlaceholderForElementImages();
        var (snapshot, _, (scaleX, scaleY)) = HtmlCanvasElement.GetDrawableSnapshot(element, placeholder, ((int)Math.Min(_width, int.MaxValue), (int)Math.Min(_height, int.MaxValue)));
        return new ElementImageDefaultSize { Width = snapshot.Size.Width * scaleX, Height = snapshot.Size.Height * scaleY };
    }

    private static void ValidateElementImage(HtmlCanvasElement placeholder, ElementImage image)
    {
        if (!ReferenceEquals(image.Canvas, placeholder))
            throw new DomException("The element image was captured from another canvas", "InvalidStateError");

        if (image.Snapshot is null)
            throw new DomException("The element image is closed", "InvalidStateError");
    }

    private HtmlCanvasElement GetPlaceholderForElementImages()
        => Placeholder ?? throw new DomException("The OffscreenCanvas has no placeholder canvas", "InvalidStateError");
}
