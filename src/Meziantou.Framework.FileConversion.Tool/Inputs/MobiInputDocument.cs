using Meziantou.Framework.FileConversionTool.Layout;
using Meziantou.Framework.FileConversionTool.Mobi;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf;

namespace Meziantou.Framework.FileConversionTool.Inputs;

/// <summary>
/// A MOBI book (Mobipocket, Kindle), from its KF8 version when it has one, or its MOBI 6 version. A KF8 book whose
/// documents each show one image (comics) gives its images. The other books are laid out with their images and style
/// sheets: each document of a KF8 book, and each page break of a MOBI 6 book, starts a new page, as PDF pages for a PDF
/// output (vector text), as images otherwise. The cover, when the text does not show it, is the first page.
/// </summary>
internal sealed class MobiInputDocument : InputDocument
{
    private readonly MobiBook _book;
    private readonly MobiDocumentLoader _loader;
    private readonly IReadOnlyList<string> _documentUrls;
    private readonly IReadOnlyList<EncodedImage>? _imagePages;
    private readonly EncodedImage? _cover;
    private readonly ConversionSettings _settings;
    private readonly bool _renderToPdf;

    // The documents printed to PDF stay alive until their pages are imported into the output
    private readonly List<PdfDocument> _printedDocuments = [];

    private MobiInputDocument(FullPath path, MobiBook book, MobiDocumentLoader loader, IReadOnlyList<string> documentUrls, IReadOnlyList<EncodedImage>? imagePages, EncodedImage? cover, ConversionSettings settings, bool renderToPdf)
        : base(path, FileFormat.Mobi)
    {
        _book = book;
        _loader = loader;
        _documentUrls = documentUrls;
        _imagePages = imagePages;
        _cover = cover;
        _settings = settings;
        _renderToPdf = renderToPdf;
    }

    public override int? PageCount => _imagePages is null ? null : _imagePages.Count + (_cover is null ? 0 : 1);

    public override string? Title => _book.Title;

    public override string? Author => _book.Author;

    public static MobiInputDocument Open(FullPath path, ConversionSettings settings, FileFormat outputFormat, CancellationToken cancellationToken)
    {
        var book = MobiBook.Read(File.ReadAllBytes(path));
        var renderToPdf = outputFormat.Kind is FileFormatKind.Pdf;
        if (book.Kf8 is { } kf8Header)
        {
            Kf8Content? content = null;
            try
            {
                content = Kf8Content.Read(book, kf8Header);
            }
            catch (InvalidDataException) when (book.Mobi6 is not null)
            {
                // The MOBI 6 version of a joint file is read when its KF8 version cannot be
            }

            if (content is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var loader = new MobiDocumentLoader(book, html: null, kf8Header.EncodingName, content);
                var urls = Enumerable.Range(0, content.Parts.Count).Select(Kf8Content.GetPartUrl).ToArray();
                var imagePages = content.ImagePages?.Select(index => GetImage(book, index)!).ToArray();
                return new MobiInputDocument(path, book, loader, urls, imagePages, GetCover(book, content.ResourceIndexes), settings, renderToPdf);
            }
        }

        var mobi6 = book.Mobi6 ?? throw new InvalidDataException("The book has no MOBI 6 version");
        var html = MobiHtml.Convert(book.ReadText(mobi6), mobi6.IsUtf8, out var imageIndexes);
        cancellationToken.ThrowIfCancellationRequested();
        var mobi6Loader = new MobiDocumentLoader(book, html, mobi6.EncodingName, kf8: null);
        return new MobiInputDocument(path, book, mobi6Loader, [MobiHtml.DocumentUrl], imagePages: null, GetCover(book, imageIndexes), settings, renderToPdf);
    }

    public override IEnumerable<ConversionPage> GetPages(CancellationToken cancellationToken)
    {
        var pageNumber = 0;
        if (_cover is not null)
        {
            pageNumber++;
            yield return new EncodedImagePage(pageNumber, _cover);
        }

        if (_imagePages is not null)
        {
            foreach (var image in _imagePages)
            {
                pageNumber++;
                yield return new EncodedImagePage(pageNumber, image);
            }

            yield break;
        }

        foreach (var url in _documentUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The pages of a document are rendered together: the images of the pages that are not written are released
            var pages = LayoutRenderer.Render(_loader, url, _settings.LayoutPageSize ?? PdfSize.A5, _settings, _renderToPdf, pageNumber, _printedDocuments, cancellationToken);
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

    /// <summary>Gets the cover of a book when its text does not show it, and it is an image of a supported format.</summary>
    internal static EncodedImage? GetCover(MobiBook book, IReadOnlySet<int> imageIndexes)
    {
        if (book.CoverImageIndex is not { } index || imageIndexes.Contains(index))
            return null;

        return GetImage(book, index);
    }

    /// <summary>Gets an image of a book, when it is an image of a supported format.</summary>
    private static EncodedImage? GetImage(MobiBook book, int index)
    {
        if (book.GetImage(index) is not { } data)
            return null;

        var format = Image.DetectFormat(data.Span);
        return format is ImageFormat.Unknown or ImageFormat.Custom ? null : new EncodedImage(data, format);
    }
}
