using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Pdf.Objects;
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
        options ??= PdfRenderOptions.Default;
        var (width, height, _) = PageGeometry.Compute(page, options);
        var image = new Image<Rgba32>(width, height, options.Configuration);
        try
        {
            Render(page, image, options, backgroundBehind: true, cancellationToken);
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return image;
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
        Render(page, target, options ?? PdfRenderOptions.Default, backgroundBehind: false, cancellationToken);
    }

    /// <summary>Renders a page on an image: the background is painted behind the page on a new image, which is transparent, and before it on an image of the application, which may have no alpha.</summary>
    private static void Render(PdfPage page, Image target, PdfRenderOptions options, bool backgroundBehind, CancellationToken cancellationToken)
    {
        var (pageWidth, pageHeight, matrix) = PageGeometry.Compute(page, options);
        var width = Math.Min(pageWidth, target.Width);
        var height = Math.Min(pageHeight, target.Height);
        using var cache = new RenderCache(options, cancellationToken);
        target.Draw(canvas => canvas.Clip(new Rectangle(0, 0, width, height), clipped => clipped.Antialiasing(options.PathAntialiasing, content =>
        {
            if (!backgroundBehind && options.Background is { } paper)
            {
                content.Fill(new SolidBrush(paper.ToRgba64()));
            }

            // The magnification: 100% is one pixel per point
            var userUnit = page.UserUnit is > 0 and var unit && double.IsFinite(unit) ? unit : 1;
            var zoom = Math.Sqrt(Math.Abs((matrix.A * matrix.D) - (matrix.B * matrix.C))) / userUnit * 100;
            var optionalContent = new OptionalContent(page.Document, options, zoom);

            // A knockout page group (11.4.7): the objects of the page are elements composited with the paper
            var pageGroup = page.Dictionary.Get<PdfDictionary>(PdfNames.Group);
            var isKnockout = pageGroup?.GetName(PdfNames.S)?.Value == "Transparency" && pageGroup.Get<PdfBoolean>(PdfNames.K)?.Value is true;
            void Draw(DrawingCanvas surface)
            {
                var renderer = new ContentRenderer(cache, page.Document, new RenderGraphicsState { Ctm = matrix }, depth: 0, optionalContent)
                {
                    BaseMatrix = matrix,
                    DeviceSize = (width, height),
                    IsKnockoutContext = isKnockout,
                };
                renderer.Run(surface, GetContent(page), page.Resources);
            }

            if (isKnockout)
            {
                content.Group(new GroupOptions { IsKnockout = true }, group => Draw(group));
            }
            else
            {
                Draw(content);
            }

            if (options.RenderAnnotations)
            {
                AnnotationRenderer.Render(page, content, cache, matrix, (width, height), optionalContent);
            }

            // The page is a group composited on the paper (11.4.7): the content is drawn on a transparent backdrop,
            // which the blend modes see, then the background is put behind it
            if (backgroundBehind && options.Background is { } background)
            {
                content.Fill(new SolidBrush(background.ToRgba64()) { CompositeMode = CompositeMode.DestinationOver });
            }
        })), cancellationToken);
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

    /// <summary>Gets the content of a page: its streams joined, each that cannot be decoded left out (7.8.2).</summary>
    private static byte[] GetContent(PdfPage page)
    {
        var output = new MemoryStream();
        foreach (var stream in page.GetContentStreams())
        {
            try
            {
                var data = stream.GetDecodedData();
                output.Write(data);
                output.WriteByte((byte)'\n');
            }
            catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException)
            {
            }
        }

        return output.ToArray();
    }
}
