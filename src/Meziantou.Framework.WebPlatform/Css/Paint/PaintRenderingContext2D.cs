using Meziantou.Framework.WebPlatform.Css.Cascade;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The rendering context of a paint callback (https://drafts.css-houdini.org/css-paint-api-1/#paintrenderingcontext2d): the
/// subset of the 2D context of the canvases without the text, the pixel manipulation and the user interface. Its drawing
/// operations are recorded, and replayed where the <c>paint()</c> image is drawn, at the resolution of the device.
/// </summary>
/// <seealso href="https://drafts.css-houdini.org/css-paint-api-1/#paintrenderingcontext2d"/>
[IdlInterface("PaintRenderingContext2D", "https://drafts.css-houdini.org/css-paint-api-1/#paintrenderingcontext2d")]
public sealed class PaintRenderingContext2D : CanvasRenderingContext2DBase
{
    private readonly PaintWorkletGlobalScope _global;

    internal PaintRenderingContext2D(PaintWorkletGlobalScope global, CanvasBitmap bitmap, bool alpha)
        : base(bitmap, new CanvasRenderingContext2DSettings { Alpha = alpha })
    {
        _global = global;
    }

    /// <inheritdoc/>
    internal override HtmlCanvasElement? CanvasElement => null;

    /// <inheritdoc/>
    internal override GlobalScope? Global => _global;

    /// <inheritdoc/>
    private protected override void OnBitmapChanged()
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The context has no font and the worklet has no viewport: the <c>filter</c> attribute (an extension of Blink, the
    /// specification does not include <c>CanvasFilters</c>) resolves the font-relative lengths against <c>medium</c> and the
    /// viewport-percentage lengths against a viewport of size zero.
    /// </remarks>
    private protected override ComputedStyle GetFilterStyle() => GetFilterStyleWithoutViewport(_global);
}

/// <summary>The size of the image a paint callback draws (https://drafts.css-houdini.org/css-paint-api-1/#paintsize): the concrete object size of the <c>paint()</c> image, in CSS pixels.</summary>
/// <seealso href="https://drafts.css-houdini.org/css-paint-api-1/#paintsize"/>
[IdlInterface("PaintSize", "https://drafts.css-houdini.org/css-paint-api-1/#paintsize")]
public sealed class PaintSize
{
    internal PaintSize(double width, double height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Gets the width of the image.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-paint-api-1/#dom-paintsize-width"/>
    [IdlMember("width", "https://drafts.css-houdini.org/css-paint-api-1/#dom-paintsize-width")]
    public double Width { get; }

    /// <summary>Gets the height of the image.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-paint-api-1/#dom-paintsize-height"/>
    [IdlMember("height", "https://drafts.css-houdini.org/css-paint-api-1/#dom-paintsize-height")]
    public double Height { get; }
}
