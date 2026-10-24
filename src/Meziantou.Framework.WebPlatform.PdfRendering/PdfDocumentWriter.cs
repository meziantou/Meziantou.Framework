using Meziantou.Framework.Pdf;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;
using Meziantou.Framework.WebPlatform.Rendering;
using ImagingImage = Meziantou.Framework.Imaging.Image;
using Rgba32 = Meziantou.Framework.Imaging.Rgba32;

namespace Meziantou.Framework.WebPlatform.PdfRendering;

/// <summary>Writes the printed pages of a document into a PDF document: the pages, the metadata, the structure, the links and the outline.</summary>
internal sealed class PdfDocumentWriter
{
    private readonly Dictionary<(FontFile File, int FaceIndex), PdfFont?> _fonts = [];
    private readonly Dictionary<(string Url, ExifOrientation Orientation, int Frame), PdfImage?> _images = [];
    private readonly Dictionary<(double Alpha, PdfBlendMode BlendMode), PdfExtGState> _graphicsStates = [];
    private RenderingBackend? _rasterizationBackend;

    public PdfDocumentWriter(Document document, PdfPrintOptions options)
    {
        Document = document;
        Options = options;
        var tagged = options.Tagged || options.PdfUAConformance != PdfUAConformance.None || options.PdfAConformance is PdfAConformance.PdfA1A or PdfAConformance.PdfA2A or PdfAConformance.PdfA3A;
        Structure = new PdfDocumentStructure(document, tagged);
    }

    public Document Document { get; }

    public PdfPrintOptions Options { get; }

    public PdfDocument Pdf { get; } = new();

    public PdfDocumentStructure Structure { get; }

    /// <summary>The backend whose surfaces rasterize what PDF cannot draw: the one of the browsing context, else a backend of Meziantou.Framework.WebPlatform.Imaging.</summary>
    public RenderingBackend RasterizationBackend => Document.BrowsingContext?.Options.RenderingBackend ?? (_rasterizationBackend ??= new ImagingRenderingBackend());

    public PdfDocument Write(IReadOnlyList<PrintedPage> pages)
    {
        WriteMetadata();
        for (var i = 0; i < pages.Count; i++)
        {
            Measure(i, pages[i].DisplayList);
        }

        Structure.BuildTree(Pdf);

        var pdfPages = new List<(PdfPage Page, PhysicalSize Size)>(pages.Count);
        foreach (var page in pages)
        {
            var size = page.Size;
            var pdfPage = Pdf.Pages.Add(new PdfSize(size.Width * PdfGeometry.PointsPerPixel, size.Height * PdfGeometry.PointsPerPixel));
            pdfPages.Add((pdfPage, size));

            var pageMatrix = PdfGeometry.GetPageMatrix(size.Height);
            var builder = pdfPage.CreateContentBuilder();
            builder.Transform(PdfGeometry.ToPdf(pageMatrix));
            var state = new WriterState(pageMatrix, DisplayMatrix.Identity, new PhysicalRect(0, 0, size.Width, size.Height));
            new DisplayListPdfWriter(this, builder, state, Structure.IsTagged).Write(page.DisplayList);
        }

        if (Options.IncludeLinks)
        {
            Structure.AddLinks(pdfPages);
        }

        if (Options.IncludeOutlines)
        {
            Structure.AddOutlines(Pdf, pdfPages);
        }

        return Pdf;
    }

    private void WriteMetadata()
    {
        Pdf.PdfUAConformance = Options.PdfUAConformance;
        Pdf.PdfAConformance = Options.PdfAConformance;

        var title = Options.Title ?? Document.Title;
        if (!string.IsNullOrWhiteSpace(title))
        {
            Pdf.Information.Title = title.Trim();
        }

        if (Document.DocumentElement?.GetAttribute("lang") is { Length: > 0 } language)
        {
            Pdf.Language = language;
        }

        Pdf.Information.Author = GetMetadata("author");
        Pdf.Information.Subject = GetMetadata("description");
        Pdf.Information.Keywords = GetMetadata("keywords");

        string? GetMetadata(string name)
        {
            foreach (var meta in Document.GetElementsByTagName("meta"))
            {
                if (string.Equals(meta.GetAttribute("name"), name, StringComparison.OrdinalIgnoreCase) && meta.GetAttribute("content") is { Length: > 0 } content && !string.IsNullOrWhiteSpace(content))
                    return content.Trim();
            }

            return null;
        }
    }

    /// <summary>Records where the items of a page paint the elements of the document (the first pass, before the structure tree is built).</summary>
    private void Measure(int pageIndex, DisplayList displayList)
    {
        var matrix = DisplayMatrix.Identity;
        var saved = new Stack<DisplayMatrix>();
        foreach (var item in displayList.Items)
        {
            switch (item)
            {
                case SaveItem or SaveLayerItem:
                    saved.Push(matrix);
                    break;

                case RestoreItem:
                    if (saved.Count > 0)
                    {
                        matrix = saved.Pop();
                    }

                    break;

                case TransformItem transform:
                    matrix = matrix.PreConcat(transform.Matrix with { Matrix3d = null });
                    break;

                case ClipRectItem or ClipRoundedRectItem or ClipPathItem:
                    break;

                default:
                    Structure.Record(pageIndex, item, PdfGeometry.GetBounds(item) is { } bounds ? PdfGeometry.Transform(matrix, bounds) : null);
                    break;
            }
        }
    }

    /// <summary>Returns the font of a face of a font file, or null when it cannot be embedded.</summary>
    public PdfFont? GetFont(FontFile file, int faceIndex)
    {
        if (!_fonts.TryGetValue((file, faceIndex), out var font))
        {
            try
            {
                font = PdfFont.FromFace(file.Faces[faceIndex].Face);
            }
            catch (UnsupportedPdfFeatureException)
            {
                font = null;
            }

            _fonts.Add((file, faceIndex), font);
        }

        return font;
    }

    /// <summary>
    /// Returns the PDF image of an image, or null when it cannot be decoded: a JPEG image is embedded as it is, the other
    /// images are decoded by the rendering backend of the browsing context.
    /// </summary>
    public PdfImage? GetImage(PaintImage image)
    {
        // The elements that show an image have their own instances of it
        var key = (image.Url, image.Orientation, image.Frame);
        if (_images.TryGetValue(key, out var result))
            return result;

        if (image.MimeType is "image/jpeg" && image.Orientation == ExifOrientation.TopLeft && image.Frame == 0 && !image.Data.IsEmpty)
        {
            try
            {
                result = PdfImage.FromJpeg(image.Data.Span);
            }
            catch (Exception ex) when (ex is PdfException or ArgumentException)
            {
                result = null;
            }
        }

        if (result is null && Document.BrowsingContext?.Options.RenderingBackend is { } backend && image.GetDecodedImage(backend) is { Width: > 0, Height: > 0 } decoded)
        {
            var pixels = new byte[checked(decoded.Width * decoded.Height * 4)];
            decoded.ReadPixels(pixels);
            using var bitmap = ImagingImage.ImportPixelBytes<Rgba32>(pixels, decoded.Width, decoded.Height, decoded.Width * 4);
            result = PdfImage.FromImage(bitmap);
        }

        _images.Add(key, result);
        return result;
    }

    /// <summary>Returns the graphics state of an opacity and a blend mode (shared by the content of the document).</summary>
    public PdfExtGState GetGraphicsState(double alpha, PdfBlendMode blendMode)
    {
        if (!_graphicsStates.TryGetValue((alpha, blendMode), out var state))
        {
            state = new PdfExtGState
            {
                FillAlpha = alpha < 1 ? alpha : null,
                StrokeAlpha = alpha < 1 ? alpha : null,
                BlendMode = blendMode != PdfBlendMode.Normal ? blendMode : null,
            };
            _graphicsStates.Add((alpha, blendMode), state);
        }

        return state;
    }
}
