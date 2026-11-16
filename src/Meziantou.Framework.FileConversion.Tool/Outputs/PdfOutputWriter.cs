using Meziantou.Framework.FileConversionTool.Inputs;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Pdf;

namespace Meziantou.Framework.FileConversionTool.Outputs;

/// <summary>Writes the pages of the inputs to a PDF document: the PDF pages are imported as they are, and the images are placed one per page.</summary>
internal static class PdfOutputWriter
{
    // The size of a pixel when an image has no resolution: 96 DPI, the CSS pixel
    private const double DefaultDpi = 96;

    public static void Write(FullPath outputPath, IEnumerable<ConversionPage> pages, string? title, string? author, ConversionSettings settings, CancellationToken cancellationToken)
    {
        var document = new PdfDocument();
        foreach (var page in pages)
        {
            if (page.GetPdfPage() is { } pdfPage)
            {
                document.Pages.Add(pdfPage);
                continue;
            }

            AddImagePage(document, page, settings, cancellationToken);
        }

        if (document.Pages.Count is 0)
            throw new InvalidOperationException("The inputs have no page to write");

        document.Information.Title = title;
        document.Information.Author = author;
        ApplyOutputSettings(document, settings);
        document.Save(outputPath, new PdfSaveOptions { Linearize = settings.PdfLinearize });
    }

    /// <summary>Optimizes the document, converts it to PDF/A, and sets its encryption, as the command line asks.</summary>
    private static void ApplyOutputSettings(PdfDocument document, ConversionSettings settings)
    {
        if (settings.PdfOptimize || settings.PdfMaxImageDpi is not null)
        {
            var plan = PdfOptimizer.CreatePlan(document, new PdfOptimizationOptions
            {
                PruneUnusedResources = settings.PdfOptimize,
                RemovePageThumbnails = settings.PdfOptimize,
                MaxImageResolution = settings.PdfMaxImageDpi,
            });
            PdfOptimizer.Apply(plan);
        }

        // The conversion comes after the optimization, so that it checks the content that is saved
        if (settings.PdfConformance is not PdfConformance.None)
        {
            var conformance = settings.PdfConformance switch
            {
                PdfConformance.PdfA2B => PdfAConformance.PdfA2B,
                PdfConformance.PdfA2U => PdfAConformance.PdfA2U,
                PdfConformance.PdfA3B => PdfAConformance.PdfA3B,
                PdfConformance.PdfA3U => PdfAConformance.PdfA3U,
                _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.PdfConformance, "The PDF/A level is not supported"),
            };

            var options = settings.PdfOutputIntent is { } outputIntent ? new PdfAConversionOptions { OutputIntentProfile = File.ReadAllBytes(outputIntent) } : null;
            var result = document.ConvertToPdfA(conformance, options);
            if (!result.Succeeded)
                throw new ConversionException($"The output cannot be saved as {GetName(settings.PdfConformance)}:{Environment.NewLine}{string.Join(Environment.NewLine, result.Violations.Select(violation => "- " + violation))}");
        }

        if (settings.PdfOutputPassword is not null || settings.PdfOwnerPassword is not null)
        {
            document.Encryption = new PdfEncryption(settings.PdfOutputPassword ?? "", settings.PdfOwnerPassword ?? "")
            {
                Permissions = settings.PdfPermissions ?? PdfPermissions.All,
            };
        }
    }

    private static string GetName(PdfConformance conformance) => conformance switch
    {
        PdfConformance.PdfA2B => "PDF/A-2b",
        PdfConformance.PdfA2U => "PDF/A-2u",
        PdfConformance.PdfA3B => "PDF/A-3b",
        PdfConformance.PdfA3U => "PDF/A-3u",
        _ => conformance.ToString(),
    };

    private static void AddImagePage(PdfDocument document, ConversionPage page, ConversionSettings settings, CancellationToken cancellationToken)
    {
        PdfImage? pdfImage = null;
        var dpi = (Horizontal: DefaultDpi, Vertical: DefaultDpi);

        // A JPEG file is embedded as it is: no loss, no decoding
        if (settings.PdfImageEncoding is PdfImageEncoding.Auto && !settings.ResizesImages && page.GetEncodedImage() is { Format: ImageFormat.Jpeg } encoded)
        {
            try
            {
                pdfImage = PdfImage.FromJpeg(encoded.Data.Span);
                dpi = GetResolution(Image.Identify(encoded.Data.Span).Metadata.Resolution);
            }
            catch (ArgumentException)
            {
                // A JPEG file the DCTDecode filter does not support (12-bit samples, lossless or arithmetic coding): decode it
                pdfImage = null;
            }
        }

        if (pdfImage is null)
        {
            using var image = page.RenderImage(cancellationToken);
            dpi = GetResolution(image.Metadata.Resolution);
            settings.ResizeImage(image, cancellationToken);
            pdfImage = settings.PdfImageEncoding is PdfImageEncoding.Jpeg
                ? PdfImage.FromImageAsJpeg(image, (JpegEncoder)settings.CreateEncoder(ImageFormat.Jpeg, extension: null))
                : PdfImage.FromImage(image);
        }

        // The size of the image in points: the resolution of a resized image is the one of the original image over its new size
        var imageWidth = pdfImage.Width * 72 / dpi.Horizontal;
        var imageHeight = pdfImage.Height * 72 / dpi.Vertical;
        if (settings.ResizesImages && page.GetEncodedImage() is { } original)
        {
            var originalSize = Image.Identify(original.Data.Span).Size;
            imageWidth = originalSize.Width * 72 / dpi.Horizontal;
            imageHeight = originalSize.Height * 72 / dpi.Vertical;
        }

        var margin = PageSize.MillimetersToPoints(settings.PageMargin);
        PdfSize pageSize;
        if (settings.PageSize.Size is { } size)
        {
            var landscape = settings.PageOrientation switch
            {
                PageOrientation.Landscape => true,
                PageOrientation.Portrait => false,
                _ => imageWidth > imageHeight,
            };

            pageSize = landscape ? size.ToLandscape() : ToPortrait(size);
        }
        else
        {
            pageSize = new PdfSize(imageWidth + (2 * margin), imageHeight + (2 * margin));
        }

        // The image is scaled to fit the area inside the margins, and centered
        var areaWidth = Math.Max(1, pageSize.Width - (2 * margin));
        var areaHeight = Math.Max(1, pageSize.Height - (2 * margin));
        var scale = Math.Min(areaWidth / imageWidth, areaHeight / imageHeight);
        var width = imageWidth * scale;
        var height = imageHeight * scale;
        var x = (pageSize.Width - width) / 2;
        var y = (pageSize.Height - height) / 2;

        var pdfPage = document.Pages.Add(pageSize);
        pdfPage.CreateContentBuilder().DrawImage(pdfImage, x, y, width, height);
    }

    private static PdfSize ToPortrait(PdfSize size) => size.Width > size.Height ? new PdfSize(size.Height, size.Width) : size;

    private static (double Horizontal, double Vertical) GetResolution(ImageResolution? resolution)
    {
        if (resolution is null || !double.IsFinite(resolution.HorizontalDpi) || !double.IsFinite(resolution.VerticalDpi) || resolution.HorizontalDpi < 1 || resolution.VerticalDpi < 1)
            return (DefaultDpi, DefaultDpi);

        return (resolution.HorizontalDpi, resolution.VerticalDpi);
    }
}
