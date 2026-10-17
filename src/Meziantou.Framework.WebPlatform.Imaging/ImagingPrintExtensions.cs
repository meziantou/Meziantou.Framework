using Meziantou.Framework.Imaging;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>
/// Prints the documents of a browsing context into pages (https://drafts.csswg.org/css-page-3/): the document is laid out with
/// the <c>print</c> media type and paginated (the page size, margins and page names of the <c>@page</c> rules, the page
/// breaks), and each page is rendered into its own image.
/// </summary>
/// <remarks>
/// <para>
/// The printing does not change the document: its styles and its layout are computed by other engines while it prints, so
/// the screen is neither styled nor laid out again afterwards (no transition starts, no event is fired). As for the
/// screenshots, call it from the thread of the <see cref="DomEventLoop"/> of the browsing context, after the resources are
/// loaded. The images and the fonts that only the print styles use (e.g. in <c>@media print</c>) are fetched by the printing,
/// but they are not drawn by it when they were not loaded before: print the document again once they are.
/// </para>
/// </remarks>
public static class ImagingPrintExtensions
{
    /// <summary>Prints the active document of a browsing context and renders each page.</summary>
    /// <param name="browsingContext">The browsing context.</param>
    /// <param name="printOptions">The page size and the default margins, or null for <see cref="PrintOptions.Default"/>.</param>
    /// <param name="options">The options of the images (the device pixel ratio), or null for the default options.</param>
    /// <returns>One image per page, of the size of its page box in device pixels. The caller owns them.</returns>
    /// <exception cref="InvalidOperationException">The document is not rendered.</exception>
    public static IReadOnlyList<Image<Rgba32>> RenderPages(this BrowsingContext browsingContext, PrintOptions? printOptions = null, ScreenshotOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(browsingContext);
        return RenderPages(browsingContext.Document, printOptions, options);
    }

    /// <summary>Prints a document and renders each page.</summary>
    /// <param name="document">The document, which must be the active document of a browsing context.</param>
    /// <param name="printOptions">The page size and the default margins, or null for <see cref="PrintOptions.Default"/>.</param>
    /// <param name="options">The options of the images (the device pixel ratio), or null for the default options.</param>
    /// <returns>One image per page, of the size of its page box in device pixels. The caller owns them.</returns>
    /// <exception cref="InvalidOperationException">The document is not rendered (it has no browsing context), or it is already being printed.</exception>
    public static IReadOnlyList<Image<Rgba32>> RenderPages(this Document document, PrintOptions? printOptions = null, ScreenshotOptions? options = null)
    {
        var pages = RenderPagePixels(document, printOptions, options);
        var result = new Image<Rgba32>[pages.Count];
        for (var i = 0; i < pages.Count; i++)
        {
            var (pixels, width, height) = pages[i];
            result[i] = ImagingScreenshotExtensions.ToImage(pixels, width, height);
        }

        return result;
    }

    /// <summary>Prints the active document of a browsing context and renders each page, encoded as PNG.</summary>
    /// <inheritdoc cref="RenderPages(BrowsingContext, PrintOptions?, ScreenshotOptions?)" path="/param"/>
    /// <inheritdoc cref="RenderPages(BrowsingContext, PrintOptions?, ScreenshotOptions?)" path="/exception"/>
    /// <returns>The bytes of the PNG image of each page.</returns>
    public static IReadOnlyList<byte[]> RenderPagesAsPng(this BrowsingContext browsingContext, PrintOptions? printOptions = null, ScreenshotOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(browsingContext);
        return RenderPagesAsPng(browsingContext.Document, printOptions, options);
    }

    /// <summary>Prints a document and renders each page, encoded as PNG.</summary>
    /// <inheritdoc cref="RenderPages(Document, PrintOptions?, ScreenshotOptions?)" path="/param"/>
    /// <inheritdoc cref="RenderPages(Document, PrintOptions?, ScreenshotOptions?)" path="/exception"/>
    /// <returns>The bytes of the PNG image of each page.</returns>
    public static IReadOnlyList<byte[]> RenderPagesAsPng(this Document document, PrintOptions? printOptions = null, ScreenshotOptions? options = null)
    {
        var pages = RenderPagePixels(document, printOptions, options);
        var result = new byte[pages.Count][];
        for (var i = 0; i < pages.Count; i++)
        {
            var (pixels, width, height) = pages[i];
            result[i] = PngEncoder.Encode(width, height, pixels);
        }

        return result;
    }

    private static List<(byte[] Pixels, int Width, int Height)> RenderPagePixels(Document document, PrintOptions? printOptions, ScreenshotOptions? options)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.BrowsingContext is null)
            throw new InvalidOperationException("The document is not rendered: it is not the active document of a browsing context.");

        using var session = document.BeginPrint(printOptions ?? PrintOptions.Default);
        session.FetchResources();
        return RenderPages(document, session, options);
    }

    /// <summary>Renders the pages of a document being printed (the pages are laid out when the layout is out of date).</summary>
    /// <remarks>With a <paramref name="pool"/>, the arrays are rented from it (they can be longer than the pixels): the caller returns them.</remarks>
    internal static List<(byte[] Pixels, int Width, int Height)> RenderPages(Document document, PrintSession session, ScreenshotOptions? options, System.Buffers.ArrayPool<byte>? pool = null)
    {
        var pages = session.Paint();
        var result = new List<(byte[] Pixels, int Width, int Height)>(pages.Count);
        foreach (var page in pages)
        {
            result.Add(ImagingScreenshotExtensions.Render(document, page.DisplayList, new PhysicalRect(0, 0, page.Size.Width, page.Size.Height), options, pool));
        }

        return result;
    }
}
