using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    /// <summary>Flattens the annotations of every page: see <see cref="PdfPage.FlattenAnnotations(PdfAnnotationFlattenOptions?)"/>.</summary>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfAnnotationFlattenOptions.Default"/>.</param>
    /// <returns>The number of annotations flattened, the pop-ups left out.</returns>
    public int FlattenAnnotations(PdfAnnotationFlattenOptions? options = null)
    {
        var count = 0;
        foreach (var page in Pages)
        {
            count += page.FlattenAnnotations(options);
        }

        return count;
    }

    /// <summary>
    /// Applies the redaction annotations of the document (ISO 32000-2, 12.5.6.23): the content of their regions is removed
    /// by <see cref="PdfPage.Redact(IEnumerable{PdfRectangle}, PdfRedactionOptions?)"/>, the regions are painted with the
    /// interior color of the annotation (<see cref="PdfRedactAnnotation.InteriorColor"/>, or
    /// <see cref="PdfRedactionOptions.FillColor"/> when it has none) and its overlay text, and the redaction annotations
    /// are removed.
    /// </summary>
    /// <param name="options">The options of the redaction, or <see langword="null"/> for <see cref="PdfRedactionOptions.Default"/>.</param>
    /// <returns>The number of redaction annotations applied.</returns>
    /// <remarks>The overlay text is drawn in Helvetica, sized to the height of each region; the characters Helvetica cannot show are left out. The remarks of <see cref="PdfPage.Redact(IEnumerable{PdfRectangle}, PdfRedactionOptions?)"/> apply.</remarks>
    /// <exception cref="InvalidPdfContentException">A content stream, or an image a region touches, cannot be decoded.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">An image a region touches uses a feature the library does not support.</exception>
    /// <exception cref="PdfResourceLimitException">The content runs more operators and glyphs than the limit, or its forms are nested too deep.</exception>
    public int ApplyRedactions(PdfRedactionOptions? options = null)
    {
        options ??= PdfRedactionOptions.Default;
        var count = 0;
        foreach (var page in Pages)
        {
            var annotations = page.Annotations.OfType<PdfRedactAnnotation>().ToList();
            if (annotations.Count == 0)
                continue;

            var overlays = annotations.Select(annotation => (Regions: annotation.Regions, Fill: annotation.InteriorColor ?? options.FillColor, Text: annotation.OverlayText, Repeat: annotation.RepeatOverlayText)).ToList();

            // The redaction annotations are removed first, so that the redaction does not count them
            AnnotationRemover.Remove(page, annotations.Select(annotation => ((PdfObject?)annotation.Reference ?? annotation.Dictionary, annotation.Dictionary)));
            page.Redact(overlays.SelectMany(overlay => overlay.Regions), new PdfRedactionOptions { FillColor = null, RemoveAnnotations = options.RemoveAnnotations });

            var builder = page.CreateContentBuilder();
            foreach (var (regions, fill, text, repeat) in overlays)
            {
                foreach (var region in regions.Where(region => region.Width > 0 && region.Height > 0))
                {
                    if (fill is { } color)
                    {
                        builder.SetFillColor(color).Rectangle(region).Fill();
                    }

                    if (!string.IsNullOrEmpty(text))
                    {
                        DrawOverlayText(builder, region, text, repeat, fill);
                    }
                }
            }

            builder.Complete();
            count += annotations.Count;
        }

        return count;
    }

    /// <summary>Draws the overlay text of a redaction in a region: centered, or repeated to fill the width; black, or white on a dark fill.</summary>
    private static void DrawOverlayText(PdfContentBuilder builder, PdfRectangle region, string text, bool repeat, PdfColor? fill)
    {
        var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        var printable = new string([.. text.Where(c => c is >= ' ' and <= 'ÿ')]);
        if (printable.Length == 0)
            return;

        var fontSize = Math.Max(region.Height * 0.7, 1);
        double width;
        try
        {
            width = font.GetTextWidth(printable, fontSize);
            if (width > region.Width && width > 0)
            {
                fontSize = Math.Max(fontSize * region.Width / width, 1);
                width = font.GetTextWidth(printable, fontSize);
            }

            if (repeat && width > 0)
            {
                var copies = Math.Clamp((int)(region.Width / (width + font.GetTextWidth(" ", fontSize))), 1, 1000);
                printable = string.Join(' ', Enumerable.Repeat(printable, copies));
                width = font.GetTextWidth(printable, fontSize);
            }
        }
        catch (ArgumentException)
        {
            return;
        }

        var components = fill?.GetComponents() ?? [1];
        var dark = fill?.ColorSpace switch
        {
            PdfDeviceColorSpace.Gray => components[0] < 0.5,
            PdfDeviceColorSpace.Rgb => ((0.3 * components[0]) + (0.59 * components[1]) + (0.11 * components[2])) < 0.5,
            PdfDeviceColorSpace.Cmyk => components[3] > 0.5 || components.Take(3).Average() > 0.6,
            _ => false,
        };
        builder.SetFillColor(dark ? PdfColor.White : PdfColor.Black)
            .DrawText(printable, font, fontSize, region.Left + ((region.Width - width) / 2), region.Bottom + ((region.Height - (font.CapHeight * fontSize / 1000)) / 2));
    }
}
