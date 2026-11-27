using System.IO.Compression;
using Meziantou.Framework.FileConversionTool.Layout;
using Meziantou.Framework.Pdf;

namespace Meziantou.Framework.FileConversionTool.Inputs;

/// <summary>
/// An SVG document (or a gzip-compressed SVG document), rendered by the web platform at its size: as one vector PDF page for a
/// PDF output, as one image otherwise. It loads the local files of its folder (images, style sheets, fonts), and nothing from
/// the network.
/// </summary>
internal sealed class SvgInputDocument : InputDocument
{
    // An SVG file larger than this is not decompressed: a small .svgz file can expand to gigabytes
    private const long MaxDocumentLength = 256L * 1024 * 1024;

    private readonly LocalFileDocumentLoader _loader;
    private readonly string _url;
    private readonly ConversionSettings _settings;
    private readonly bool _renderToPdf;

    // The document printed to PDF stays alive until its page is imported into the output
    private readonly List<PdfDocument> _printedDocuments = [];

    private SvgInputDocument(FullPath path, LocalFileDocumentLoader loader, string url, ConversionSettings settings, bool renderToPdf)
        : base(path, FileFormat.Svg)
    {
        _loader = loader;
        _url = url;
        _settings = settings;
        _renderToPdf = renderToPdf;
    }

    public override int? PageCount => 1;

    public static SvgInputDocument Open(FullPath path, ConversionSettings settings, FileFormat outputFormat)
    {
        var content = ReadContent(File.ReadAllBytes(path));
        var loader = new LocalFileDocumentLoader(path.Parent, path, content, "image/svg+xml");
        return new SvgInputDocument(path, loader, loader.GetUrl(path), settings, renderToPdf: outputFormat.Kind is FileFormatKind.Pdf);
    }

    /// <summary>Gets the content of an SVG file: its bytes, decompressed when they are compressed with gzip (<c>.svgz</c>).</summary>
    /// <exception cref="InvalidDataException">The compressed data is not valid, or it is too large once decompressed.</exception>
    internal static byte[] ReadContent(byte[] data)
    {
        if (data is not [0x1F, 0x8B, ..])
            return data;

        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            if (output.Length + read > MaxDocumentLength)
                throw new InvalidDataException("The decompressed SVG document is too large");

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    public override IEnumerable<ConversionPage> GetPages(CancellationToken cancellationToken)
    {
        yield return _renderToPdf
            ? SvgRenderer.RenderToPdf(_loader, _url, Path.Value, pageNumber: 1, _settings, _printedDocuments, cancellationToken)
            : SvgRenderer.RenderToImage(_loader, _url, Path.Value, pageNumber: 1, _settings.SvgWidth, _settings.SvgHeight, cancellationToken);
    }
}
