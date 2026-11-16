using System.IO.Compression;
using Meziantou.Framework.FileConversionTool.Epub;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf;
using Meziantou.Framework.Pdf.Rendering;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;
using Meziantou.Framework.WebPlatform.PdfRendering;

namespace Meziantou.Framework.FileConversionTool.Inputs;

/// <summary>
/// An EPUB book. A book whose pages are images gives its images; the other books are laid out with their style sheets, each
/// document of the reading order starting a new page: as PDF pages for a PDF output (vector text), as images otherwise.
/// </summary>
internal sealed class EpubInputDocument : InputDocument
{
    private const double CssPixelsPerPoint = 96.0 / 72.0;

    private readonly ZipArchive _archive;
    private readonly EpubPackage _package;
    private readonly IReadOnlyList<string>? _imagePages;
    private readonly ConversionSettings _settings;
    private readonly bool _renderToPdf;

    // The documents printed to PDF stay alive until their pages are imported into the output
    private readonly List<PdfDocument> _printedDocuments = [];

    private EpubInputDocument(FullPath path, ZipArchive archive, EpubPackage package, IReadOnlyList<string>? imagePages, ConversionSettings settings, bool renderToPdf)
        : base(path, FileFormat.Epub)
    {
        _archive = archive;
        _package = package;
        _imagePages = imagePages;
        _settings = settings;
        _renderToPdf = renderToPdf;
    }

    public override int? PageCount => _imagePages?.Count;

    public override string? Title => _package.Title;

    public override string? Author => _package.Author;

    /// <summary>Gets a value indicating whether the pages are the images of the book, rather than rendered pages.</summary>
    public bool UsesImagePages => _imagePages is not null;

    public static EpubInputDocument Open(FullPath path, ConversionSettings settings, FileFormat outputFormat, CancellationToken cancellationToken)
    {
        var archive = ZipFiles.OpenRead(path);
        try
        {
            var package = EpubPackage.Read(archive);
            IReadOnlyList<string>? imagePages = null;
            if (settings.EpubMode is not EpubMode.Render)
            {
                imagePages = EpubImagePages.Find(archive, package);
                if (imagePages is null && settings.EpubMode is EpubMode.Images)
                    throw new InvalidDataException("The pages of the book are not images: a document of its reading order has text, several images or no image. Use '--epub-mode render'");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new EpubInputDocument(path, archive, package, imagePages, settings, renderToPdf: outputFormat.Kind is FileFormatKind.Pdf);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    public override IEnumerable<ConversionPage> GetPages(CancellationToken cancellationToken)
    {
        if (_imagePages is not null)
        {
            for (var i = 0; i < _imagePages.Count; i++)
            {
                yield return CreateImagePage(i + 1, _imagePages[i]);
            }

            yield break;
        }

        var pageNumber = 0;
        foreach (var item in _package.Spine)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.IsImage)
            {
                pageNumber++;
                yield return CreateImagePage(pageNumber, item.Path);
                continue;
            }

            if (_archive.GetEntry(item.Path) is null)
                continue;

            foreach (var page in RenderDocument(item, pageNumber, cancellationToken))
            {
                pageNumber++;
                yield return page;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _archive.Dispose();
        }

        base.Dispose(disposing);
    }

    private EncodedImagePage CreateImagePage(int pageNumber, string path)
    {
        var entry = _archive.GetEntry(path) ?? throw new InvalidDataException($"The image '{path}' of the book does not exist");
        var data = ZipFiles.ReadEntry(entry);
        var format = Image.DetectFormat(data);
        if (format is ImageFormat.Unknown)
            throw new InvalidDataException($"The file '{path}' of the book is not an image of a supported format");

        return new EncodedImagePage(pageNumber, new EncodedImage(data, format));
    }

    private List<ConversionPage> RenderDocument(EpubManifestItem item, int firstPageNumber, CancellationToken cancellationToken)
    {
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new EpubDocumentLoader(_archive, _package),
            RenderingBackend = new ImagingRenderingBackend(),
        });

        context.Navigate(EpubDocumentLoader.GetUrl(item.Path));
        context.EventLoop.RunUntilIdle();
        cancellationToken.ThrowIfCancellationRequested();

        AddUserStyleSheet(context.Document);
        context.EventLoop.RunUntilIdle();

        var pageSize = _settings.EpubPageSize;
        var margin = PageSize.MillimetersToPoints(_settings.EpubMargin) * CssPixelsPerPoint;
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
        if (_renderToPdf)
        {
            var document = context.PrintToPdf(printOptions);
            _printedDocuments.Add(document);
            foreach (var page in document.Pages)
            {
                pages.Add(new PrintedPdfPage(firstPageNumber + pages.Count + 1, page, _settings));
            }
        }
        else
        {
            var images = context.RenderPages(printOptions, new ScreenshotOptions { DevicePixelRatio = _settings.Dpi / 96 });
            foreach (var image in images)
            {
                pages.Add(new RenderedImagePage(firstPageNumber + pages.Count + 1, image));
            }
        }

        return pages;
    }

    private void AddUserStyleSheet(Document document)
    {
        if (_settings.EpubFontSize is not { } fontSize)
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
        // The image is given once: the caller owns it
        public override Image RenderImage(CancellationToken cancellationToken) => image;
    }
}
