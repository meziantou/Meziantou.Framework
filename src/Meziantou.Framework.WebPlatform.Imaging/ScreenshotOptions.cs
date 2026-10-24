using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>Options of a screenshot (see <see cref="ImagingScreenshotExtensions"/>).</summary>
public sealed class ScreenshotOptions
{
    /// <summary>
    /// Gets the number of pixels of the screenshot per CSS pixel. The default is null: the
    /// <see cref="ViewportOptions.DevicePixelRatio"/> of the browsing context (a screenshot of a 800x600 viewport at a device pixel
    /// ratio of 2 is 1600x1200 pixels). Changing it does not change the layout (the media queries still see the device pixel
    /// ratio of the viewport): the painting is scaled.
    /// </summary>
    public double? DevicePixelRatio { get; init; }

    /// <summary>
    /// Gets whether the background of the canvas of a top-level document is omitted: the document is painted over a transparent
    /// canvas instead of the color of the canvas (white, or a dark color for a document with a dark color scheme), so the
    /// parts of the page that the document does not paint are transparent. The backgrounds of the document (of its root
    /// element and of its body) are still painted. For the printed pages, the pages are painted over a transparent sheet
    /// instead of white paper. The default is false. It is ignored by the JPEG screenshots, which have no alpha channel.
    /// </summary>
    /// <remarks>This is the <c>omitBackground</c> option of the screenshots of Playwright.</remarks>
    public bool OmitBackground { get; init; }

    /// <summary>
    /// Gets whether the screenshot of a viewport shows the whole document instead of the viewport: the scrolling area of the
    /// viewport from the origin of the document (its <c>scrollWidth</c> and <c>scrollHeight</c>, at least the size of the
    /// viewport). The layout does not change: the viewport units and the media queries still see the size of the viewport, the
    /// fixed positioned elements are painted once, where they are in the document at the current scroll position (at the top of
    /// the document when it is not scrolled), and the scrollbars of the viewport are not painted. The default is false. It is
    /// ignored by the screenshots of the elements.
    /// </summary>
    /// <remarks>This is the <c>fullPage</c> option of the screenshots of Playwright.</remarks>
    public bool FullPage { get; init; }

    /// <summary>
    /// Gets the area of the screenshot of a viewport, in CSS pixels: relative to the viewport, or to the origin of the document
    /// with <see cref="FullPage"/>. The area is clipped to the viewport (or to the document); it must not be empty once clipped.
    /// The default is null: the whole viewport (or document). It is ignored by the screenshots of the elements.
    /// </summary>
    /// <remarks>This is the <c>clip</c> option of the screenshots of Playwright.</remarks>
    public ScreenshotClip? Clip { get; init; }

    /// <summary>
    /// Gets the quality of the JPEG screenshots, from 0 to 100. The default is null: 80, the quality of the JPEG screenshots of
    /// Chromium. It is ignored by the PNG screenshots.
    /// </summary>
    /// <remarks>This is the <c>quality</c> option of the screenshots of Playwright.</remarks>
    public int? Quality { get; init; }
}

/// <summary>An area of a screenshot (see <see cref="ScreenshotOptions.Clip"/>), in CSS pixels.</summary>
/// <param name="X">The left edge of the area.</param>
/// <param name="Y">The top edge of the area.</param>
/// <param name="Width">The width of the area.</param>
/// <param name="Height">The height of the area.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct ScreenshotClip(double X, double Y, double Width, double Height);
