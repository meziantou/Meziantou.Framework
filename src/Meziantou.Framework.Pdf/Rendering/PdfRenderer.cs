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
/// masks and blend modes, the optional content of the default configuration, and the appearances of the annotations.
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
    /// <returns>The image: the crop box of the page at the resolution of the options, rotated by the <c>/Rotate</c> of the page.</returns>
    /// <exception cref="PdfResourceLimitException">The page is too large at the resolution, or it does more work than <see cref="PdfRenderOptions.MaxOperations"/>.</exception>
    public static Image<Rgba32> RenderPage(PdfPage page, PdfRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= PdfRenderOptions.Default;
        var (width, height, matrix) = PageGeometry.Compute(page, options.Dpi);
        var image = new Image<Rgba32>(width, height, options.Configuration);
        try
        {
            using var cache = new RenderCache(options);
            image.Draw(canvas =>
            {
                var optionalContent = new OptionalContent(page.Document);

                // A knockout page group (11.4.7): the objects of the page are elements composited with the paper
                var pageGroup = page.Dictionary.Get<PdfDictionary>(PdfNames.Group);
                var isKnockout = pageGroup?.GetName(PdfNames.S)?.Value == "Transparency" && pageGroup.Get<PdfBoolean>(PdfNames.K)?.Value is true;
                void Draw(DrawingCanvas target)
                {
                    var renderer = new ContentRenderer(cache, page.Document, new RenderGraphicsState { Ctm = matrix }, depth: 0, optionalContent)
                    {
                        BaseMatrix = matrix,
                        DeviceSize = (width, height),
                        IsKnockoutContext = isKnockout,
                    };
                    renderer.Run(target, GetContent(page), page.Resources);
                }

                if (isKnockout)
                {
                    canvas.Group(new GroupOptions { IsKnockout = true }, content => Draw(content));
                }
                else
                {
                    Draw(canvas);
                }

                if (options.RenderAnnotations)
                {
                    AnnotationRenderer.Render(page, canvas, cache, matrix, (width, height), optionalContent);
                }

                // The page is a group composited on the paper (11.4.7): the content is drawn on a transparent backdrop,
                // which the blend modes see, then the background is put behind it
                if (options.Background is { } background)
                {
                    canvas.Fill(new SolidBrush(background.ToRgba64()) { CompositeMode = CompositeMode.DestinationOver });
                }
            });
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return image;
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
