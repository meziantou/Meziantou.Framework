using System.IO.Compression;
using Meziantou.Framework.FileConversionTool.Epub;
using Meziantou.Framework.FileConversionTool.Layout;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf;

namespace Meziantou.Framework.FileConversionTool.Inputs;

/// <summary>
/// An EPUB book. A book whose pages are images gives its images; the other books are laid out with their style sheets, each
/// document of the reading order starting a new page: as PDF pages for a PDF output (vector text), as images otherwise.
/// </summary>
internal sealed class EpubInputDocument : InputDocument
{
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

            // The pages of a document are rendered together: the images of the pages that are not written are released
            var pages = RenderDocument(item, pageNumber, cancellationToken);
            var index = 0;
            try
            {
                for (; index < pages.Count; index++)
                {
                    pageNumber++;
                    yield return pages[index];
                }
            }
            finally
            {
                for (var i = index; i < pages.Count; i++)
                {
                    pages[i].Discard();
                }
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
        => LayoutRenderer.Render(new EpubDocumentLoader(_archive, _package), EpubDocumentLoader.GetUrl(item.Path), _settings.LayoutPageSize ?? PdfSize.A5, _settings, _renderToPdf, firstPageNumber, _printedDocuments, cancellationToken);
}
