using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf;
using Meziantou.Framework.Pdf.Rendering;

namespace Meziantou.Framework.FileConversionTool.Inputs;

/// <summary>A PDF document: its pages are imported as they are into a PDF output, and rendered for the other outputs.</summary>
internal sealed class PdfInputDocument : InputDocument
{
    private readonly PdfDocument _document;
    private readonly ConversionSettings _settings;
    private PdfRenderSession? _renderSession;

    private PdfInputDocument(FullPath path, PdfDocument document, ConversionSettings settings)
        : base(path, FileFormat.Pdf)
    {
        _document = document;
        _settings = settings;
    }

    public override int? PageCount => _document.Pages.Count;

    public override string? Title => _document.Information.Title;

    public override string? Author => _document.Information.Author;

    public static PdfInputDocument Open(FullPath path, ConversionSettings settings, FileFormat outputFormat)
    {
        var document = PdfDocument.Open(File.ReadAllBytes(path), new PdfReadOptions { Password = settings.PdfPassword });

        // A PDF output imports the pages without their form fields: flattening draws the fields in the content of the pages first
        if (settings.PdfFlattenForms && outputFormat.Kind is FileFormatKind.Pdf)
        {
            document.Form.Flatten();
        }

        return new PdfInputDocument(path, document, settings);
    }

    public override IEnumerable<ConversionPage> GetPages(CancellationToken cancellationToken)
    {
        for (var i = 0; i < _document.Pages.Count; i++)
        {
            yield return new PdfConversionPage(this, i + 1, _document.Pages[i]);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderSession?.Dispose();
        }

        base.Dispose(disposing);
    }

    private Image<Rgba32> Render(PdfPage page, CancellationToken cancellationToken)
    {
        _renderSession ??= new PdfRenderSession(_document, new PdfRenderOptions { Dpi = _settings.Dpi, RenderAnnotations = _settings.PdfRenderAnnotations });
        return _renderSession.RenderPage(page, cancellationToken);
    }

    private sealed class PdfConversionPage(PdfInputDocument document, int pageNumber, PdfPage page) : ConversionPage(pageNumber)
    {
        public override PdfPage? GetPdfPage() => page;

        public override Image RenderImage(CancellationToken cancellationToken) => document.Render(page, cancellationToken);
    }
}
