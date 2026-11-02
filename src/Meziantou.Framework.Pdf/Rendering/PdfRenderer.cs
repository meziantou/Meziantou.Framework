using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Rendering.Graphics;

namespace Meziantou.Framework.Pdf.Rendering;

/// <summary>Renders the pages of a document to images (ISO 32000-2, 8 to 12).</summary>
/// <remarks>
/// <para>
/// The page is drawn with the canvas of Meziantou.Framework.Imaging, in sRGB: the paths with antialiasing, the text with
/// the outlines of its embedded fonts (TrueType, CFF, Type 1) or of the fonts that replace the fonts it does not embed,
/// the images of every filter and color space with their masks, the shadings and patterns, the transparency groups, soft
/// masks and blend modes, the optional content of a configuration, and the appearances of the annotations (generated
/// for the annotations that have none).
/// </para>
/// <para>
/// A damaged content is drawn up to the operator that is damaged, as viewers do: an operator whose operands or
/// resources are invalid is skipped, and a font, image or shading that cannot be read is not drawn.
/// </para>
/// <para>
/// Each call reads the resources of the page again; <see cref="PdfRenderSession"/> renders the pages of a document with
/// caches they share.
/// </para>
/// </remarks>
public static class PdfRenderer
{
    /// <summary>Renders a page to an image.</summary>
    /// <param name="page">The page.</param>
    /// <param name="options">The options; <see langword="null"/> for <see cref="PdfRenderOptions.Default"/>.</param>
    /// <returns>The image: the crop box of the page (or the region of the options) at the resolution of the options, rotated by the <c>/Rotate</c> of the page.</returns>
    /// <exception cref="PdfResourceLimitException">The page is too large at the resolution, or it does more work than <see cref="PdfRenderOptions.MaxOperations"/>.</exception>
    public static Image<Rgba32> RenderPage(PdfPage page, PdfRenderOptions? options = null) => RenderPage(page, options, CancellationToken.None);

    /// <summary>Renders a page to an image.</summary>
    /// <param name="page">The page.</param>
    /// <param name="options">The options; <see langword="null"/> for <see cref="PdfRenderOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests: it is checked as the operators, glyphs, images and shadings of the page are drawn.</param>
    /// <returns>The image: the crop box of the page (or the region of the options) at the resolution of the options, rotated by the <c>/Rotate</c> of the page.</returns>
    /// <exception cref="PdfResourceLimitException">The page is too large at the resolution, or it does more work than <see cref="PdfRenderOptions.MaxOperations"/>.</exception>
    /// <exception cref="OperationCanceledException">The rendering was canceled.</exception>
    public static Image<Rgba32> RenderPage(PdfPage page, PdfRenderOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        using var session = new PdfRenderSession(page.Document, options);
        return session.RenderPage(page, cancellationToken);
    }

    /// <summary>
    /// Renders a page to an image of the application, whatever its pixel format (a <see cref="Gray8"/> image gives a
    /// grayscale rendering, for instance): the page is drawn from the top left corner of the image, at the size
    /// <see cref="GetImageSize"/> gives. The background of the options is painted first, over the pixels of the image:
    /// set it to <see langword="null"/> to draw the page over what the image shows.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="target">The image the page is drawn on: every frame of it. What exceeds it is not drawn.</param>
    /// <param name="options">The options; <see langword="null"/> for <see cref="PdfRenderOptions.Default"/>. Their <see cref="PdfRenderOptions.Configuration"/> is not used: the image has its own.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. The shapes already drawn stay on the image.</param>
    /// <exception cref="PdfResourceLimitException">The page is too large at the resolution, or it does more work than <see cref="PdfRenderOptions.MaxOperations"/>.</exception>
    /// <exception cref="OperationCanceledException">The rendering was canceled.</exception>
    public static void RenderPage(PdfPage page, Image target, PdfRenderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(target);
        using var session = new PdfRenderSession(page.Document, options);
        session.RenderPage(page, target, cancellationToken);
    }

    /// <summary>Gets the size of the image <see cref="RenderPage(PdfPage, PdfRenderOptions?)"/> renders a page to: the crop box (or the region of the options) at their resolution or size, rotated by the <c>/Rotate</c> of the page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="options">The options; <see langword="null"/> for <see cref="PdfRenderOptions.Default"/>.</param>
    /// <returns>The width and height of the image, in pixels.</returns>
    /// <exception cref="PdfResourceLimitException">The page is too large at the resolution.</exception>
    public static Size GetImageSize(PdfPage page, PdfRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        var (width, height, _) = PageGeometry.Compute(page, options ?? PdfRenderOptions.Default);
        return new Size(width, height);
    }

    /// <summary>Renders a page to an image.</summary>
    /// <param name="page">The page.</param>
    /// <param name="options">The options; <see langword="null"/> for <see cref="PdfRenderOptions.Default"/>.</param>
    /// <returns>The image.</returns>
    /// <exception cref="PdfResourceLimitException">The page is too large at the resolution, or it does more work than <see cref="PdfRenderOptions.MaxOperations"/>.</exception>
    public static Image<Rgba32> Render(this PdfPage page, PdfRenderOptions? options = null) => RenderPage(page, options);
}
