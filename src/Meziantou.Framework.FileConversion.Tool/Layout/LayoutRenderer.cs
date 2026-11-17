using Meziantou.Framework.FileConversionTool.Inputs;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf;
using Meziantou.Framework.Pdf.Rendering;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;
using Meziantou.Framework.WebPlatform.PdfRendering;

namespace Meziantou.Framework.FileConversionTool.Layout;

/// <summary>
/// Lays out an HTML document on pages, with its style sheets: as PDF pages for a PDF output (vector text), as images
/// otherwise. The documents never run scripts: the browsing context has no script engine.
/// </summary>
internal static class LayoutRenderer
{
    private const double CssPixelsPerPoint = 96.0 / 72.0;

    /// <summary>Lays out a document on pages.</summary>
    /// <param name="loader">The loader of the document and of its resources.</param>
    /// <param name="url">The URL of the document.</param>
    /// <param name="pageSize">The size of the pages.</param>
    /// <param name="settings">The settings: the margin, the font size and the resolution of the images.</param>
    /// <param name="renderToPdf">Whether the pages are PDF pages, rather than images.</param>
    /// <param name="firstPageNumber">The number of the page before the first page of the document.</param>
    /// <param name="printedDocuments">The PDF documents the pages belong to, which must stay alive until the pages are imported.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static List<ConversionPage> Render(DocumentLoader loader, string url, PdfSize pageSize, ConversionSettings settings, bool renderToPdf, int firstPageNumber, List<PdfDocument> printedDocuments, CancellationToken cancellationToken)
    {
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = loader,
            RenderingBackend = new ImagingRenderingBackend(),
        });

        context.Navigate(url);
        context.EventLoop.RunUntilIdle();
        cancellationToken.ThrowIfCancellationRequested();

        AddUserStyleSheet(context.Document, settings);
        context.EventLoop.RunUntilIdle();

        var margin = PageSize.MillimetersToPoints(settings.LayoutMargin) * CssPixelsPerPoint;
        var printOptions = new PrintOptions
        {
            PageWidth = pageSize.Width * CssPixelsPerPoint,
            PageHeight = pageSize.Height * CssPixelsPerPoint,
            MarginTop = margin,
            MarginRight = margin,
            MarginBottom = margin,
            MarginLeft = margin,
            PreferCssPageSize = false,
        };

        var pages = new List<ConversionPage>();
        if (renderToPdf)
        {
            var document = context.PrintToPdf(printOptions);
            printedDocuments.Add(document);
            foreach (var page in document.Pages)
            {
                pages.Add(new PrintedPdfPage(firstPageNumber + pages.Count + 1, page, settings));
            }
        }
        else
        {
            var images = context.RenderPages(printOptions, new ScreenshotOptions { DevicePixelRatio = settings.Dpi / 96 });
            foreach (var image in images)
            {
                pages.Add(new RenderedImagePage(firstPageNumber + pages.Count + 1, image));
            }
        }

        return pages;
    }

    private static void AddUserStyleSheet(Document document, ConversionSettings settings)
    {
        if (settings.LayoutFontSize is not { } fontSize)
            return;

        var parent = (Node?)document.Head ?? document.DocumentElement;
        if (parent is null)
            return;

        var style = document.CreateElement("style");
        style.TextContent = string.Create(CultureInfo.InvariantCulture, $"html {{ font-size: {fontSize}pt; }}");
        parent.AppendChild(style);
    }

    private sealed class PrintedPdfPage(int pageNumber, PdfPage page, ConversionSettings settings) : ConversionPage(pageNumber)
    {
        public override PdfPage? GetPdfPage() => page;

        public override Image RenderImage(CancellationToken cancellationToken) => PdfRenderer.RenderPage(page, new PdfRenderOptions { Dpi = settings.Dpi }, cancellationToken);
    }

    private sealed class RenderedImagePage(int pageNumber, Image image) : ConversionPage(pageNumber)
    {
        private Image? _image = image;

        // The image is given once: the caller owns it
        public override Image RenderImage(CancellationToken cancellationToken)
        {
            var result = _image ?? throw new InvalidOperationException("The image of the page is already used");
            _image = null;
            return result;
        }

        public override void Discard()
        {
            _image?.Dispose();
            _image = null;
        }
    }
}
