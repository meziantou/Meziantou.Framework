using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf;

namespace Meziantou.Framework.FileConversionTool.Inputs;

/// <summary>An input file opened for the conversion: a sequence of pages. It stays open until its pages are written.</summary>
internal abstract class InputDocument : IDisposable
{
    protected InputDocument(FullPath path, FileFormat format)
    {
        Path = path;
        Format = format;
    }

    public FullPath Path { get; }

    public FileFormat Format { get; }

    /// <summary>Gets the number of pages of the input, or null when it is known only once the pages are produced (rendered EPUB).</summary>
    public abstract int? PageCount { get; }

    /// <summary>Gets the title of the document, copied to the metadata of a PDF output.</summary>
    public virtual string? Title => null;

    /// <summary>Gets the author of the document, copied to the metadata of a PDF output.</summary>
    public virtual string? Author => null;

    /// <summary>Opens an input file.</summary>
    public static InputDocument Open(FullPath path, FileFormat format, ConversionSettings settings, FileFormat outputFormat, CancellationToken cancellationToken) => format.Kind switch
    {
        FileFormatKind.Image => ImageInputDocument.Open(path, format),
        FileFormatKind.Pdf => PdfInputDocument.Open(path, settings, outputFormat),
        FileFormatKind.Comic or FileFormatKind.Archive => ArchiveInputDocument.Open(path, format, settings),
        FileFormatKind.Epub => EpubInputDocument.Open(path, settings, outputFormat, cancellationToken),
        FileFormatKind.Mobi => MobiInputDocument.Open(path, settings, outputFormat, cancellationToken),
        FileFormatKind.Markdown or FileFormatKind.Html => HtmlInputDocument.Open(path, format, settings, outputFormat),
        FileFormatKind.Svg => SvgInputDocument.Open(path, settings, outputFormat),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The format cannot be read"),
    };

    /// <summary>Gets the pages, in order. The page numbers start at 1.</summary>
    public abstract IEnumerable<ConversionPage> GetPages(CancellationToken cancellationToken);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
