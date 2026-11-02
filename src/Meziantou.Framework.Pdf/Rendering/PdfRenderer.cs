using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.Pdf.Rendering;

/// <summary>Renders the pages of a document to images (ISO 32000-2, 8 to 12).</summary>
/// <remarks>
/// <para>
/// The page is drawn with the canvas of Meziantou.Framework.Imaging, in sRGB: the paths with antialiasing, the text with
/// the outlines of its embedded fonts (TrueType, CFF, Type 1) or of the fonts that replace the fonts it does not embed,
/// the images of every filter and color space with their masks, the shadings and patterns, the transparency groups, soft
/// masks and blend modes, the optional content of the default configuration, and the appearances of the annotations.
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
    /// <returns>The image: the crop box of the page at the resolution of the options, rotated by the <c>/Rotate</c> of the page.</returns>
    /// <exception cref="PdfResourceLimitException">The page is too large at the resolution, or it does more work than <see cref="PdfRenderOptions.MaxOperations"/>.</exception>
    public static Image<Rgba32> RenderPage(PdfPage page, PdfRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        using var session = new PdfRenderSession(page.Document, options);
        return session.RenderPage(page);
    }

    /// <summary>Renders a page to an image.</summary>
    /// <param name="page">The page.</param>
    /// <param name="options">The options; <see langword="null"/> for <see cref="PdfRenderOptions.Default"/>.</param>
    /// <returns>The image.</returns>
    /// <exception cref="PdfResourceLimitException">The page is too large at the resolution, or it does more work than <see cref="PdfRenderOptions.MaxOperations"/>.</exception>
    public static Image<Rgba32> Render(this PdfPage page, PdfRenderOptions? options = null) => RenderPage(page, options);
}
