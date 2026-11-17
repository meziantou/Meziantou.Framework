using Meziantou.Framework.FileConversionTool.Layout;
using Meziantou.Framework.Pdf;
using Meziantou.Framework.WebPlatform;

namespace Meziantou.Framework.FileConversionTool.Inputs;

/// <summary>
/// A Markdown or HTML document, laid out on pages with its style sheets: as PDF pages for a PDF output (vector text), as
/// images otherwise. It loads the local files of its folder (images, style sheets, fonts), and nothing from the network.
/// </summary>
internal sealed class HtmlInputDocument : InputDocument
{
    private readonly LocalFileDocumentLoader _loader;
    private readonly string _url;
    private readonly ConversionSettings _settings;
    private readonly bool _renderToPdf;

    // The documents printed to PDF stay alive until their pages are imported into the output
    private readonly List<PdfDocument> _printedDocuments = [];

    private HtmlInputDocument(FullPath path, FileFormat format, LocalFileDocumentLoader loader, string url, string? title, ConversionSettings settings, bool renderToPdf)
        : base(path, format)
    {
        _loader = loader;
        _url = url;
        Title = title;
        _settings = settings;
        _renderToPdf = renderToPdf;
    }

    public override int? PageCount => null;

    public override string? Title { get; }

    public static HtmlInputDocument Open(FullPath path, FileFormat format, ConversionSettings settings, FileFormat outputFormat)
    {
        var root = settings.HtmlRoot ?? path.Parent;
        if (!path.IsChildOf(root))
            throw new ConversionException($"The input '{path}' is not in the folder of --html-root '{root}'");

        byte[] content;
        string mediaType;
        string? title;
        if (format.Kind is FileFormatKind.Markdown)
        {
            var (html, markdownTitle) = TextDocumentConverter.MarkdownToHtml(File.ReadAllText(path), FileFormat.GetNameWithoutExtension(path.Name), settings, fragment: false);
            content = Encoding.UTF8.GetBytes(html);
            mediaType = "text/html; charset=utf-8";
            title = markdownTitle;
        }
        else
        {
            // The parser detects the encoding of the file, from its byte order mark or its <meta charset>
            content = File.ReadAllBytes(path);
            mediaType = "text/html";
            title = settings.HtmlTitle ?? GetTitle(content);
        }

        var loader = new LocalFileDocumentLoader(root, path, content, mediaType);
        return new HtmlInputDocument(path, format, loader, loader.GetUrl(path), title, settings, renderToPdf: outputFormat.Kind is FileFormatKind.Pdf);
    }

    public override IEnumerable<ConversionPage> GetPages(CancellationToken cancellationToken)
    {
        var pageSize = _settings.LayoutPageSize ?? PdfSize.A4;
        var pages = LayoutRenderer.Render(_loader, _url, pageSize, _settings, _renderToPdf, firstPageNumber: 0, _printedDocuments, cancellationToken);

        // The images of the pages that are not written are released
        var index = 0;
        try
        {
            for (; index < pages.Count; index++)
            {
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

    private static string? GetTitle(byte[] content)
    {
        // The bytes are decoded as the renderer decodes them: from the byte order mark, the <meta charset>, or as UTF-8
        var title = new HtmlDocumentParser().Parse(content.AsSpan()).Title.Trim();
        return title.Length > 0 ? title : null;
    }
}
