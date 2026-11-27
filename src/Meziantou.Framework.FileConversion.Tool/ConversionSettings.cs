using System.IO.Compression;
using Meziantou.Framework.Compression;
using Meziantou.Framework.Email.Storage;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Pdf;

namespace Meziantou.Framework.FileConversionTool;

/// <summary>The settings of a conversion, bound from the command line.</summary>
internal sealed class ConversionSettings
{
    public IReadOnlyList<string> Inputs { get; init; } = [];

    public string? Output { get; init; }

    public FileFormat? InputFormat { get; init; }

    public FileFormat? OutputFormat { get; init; }

    public bool Overwrite { get; init; }

    public bool PerInput { get; init; }

    public bool PerPage { get; init; }

    public PageRange Pages { get; init; } = PageRange.All;

    // Images
    public int? MaxWidth { get; init; }

    public int? MaxHeight { get; init; }

    public Rgba64 Background { get; init; } = new(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);

    public int? JpegQuality { get; init; }

    public int? WebPQuality { get; init; }

    public bool WebPLossless { get; init; }

    public int? AvifQuality { get; init; }

    public int? AvifEffort { get; init; }

    public bool AvifLossless { get; init; }

    public int? HeicQuality { get; init; }

    public int? JpegXlQuality { get; init; }

    public bool JpegXlLossless { get; init; }

    public CompressionLevel? PngCompressionLevel { get; init; }

    public TiffCompression? TiffCompression { get; init; }

    public int? GifMaxColors { get; init; }

    // SVG input
    /// <summary>Gets the width of the images of the SVG documents, in pixels, or null for the size of the document.</summary>
    public int? SvgWidth { get; init; }

    /// <summary>Gets the height of the images of the SVG documents, in pixels, or null for the size of the document.</summary>
    public int? SvgHeight { get; init; }

    // PDF input
    public double Dpi { get; init; } = 150;

    public string? PdfPassword { get; init; }

    public bool PdfRenderAnnotations { get; init; } = true;

    // PDF output
    public PageSize PageSize { get; init; } = PageSize.Fit;

    public double PageMargin { get; init; }

    public PageOrientation PageOrientation { get; init; }

    public PdfImageEncoding PdfImageEncoding { get; init; }

    public PdfConformance PdfConformance { get; init; }

    /// <summary>Gets the ICC profile of the output intent of a PDF/A output, or null for sRGB.</summary>
    public FullPath? PdfOutputIntent { get; init; }

    public double? PdfMaxImageDpi { get; init; }

    public bool PdfOptimize { get; init; }

    public bool PdfLinearize { get; init; }

    public string? PdfOutputPassword { get; init; }

    public string? PdfOwnerPassword { get; init; }

    public PdfPermissions? PdfPermissions { get; init; }

    public bool PdfFlattenForms { get; init; }

    // Layout of the text of EPUB, MOBI, Markdown and HTML documents
    /// <summary>Gets the size of the pages, or null for the default size of the format: A5 for EPUB and MOBI, A4 for Markdown and HTML.</summary>
    public PdfSize? LayoutPageSize { get; init; }

    public double LayoutMargin { get; init; } = 15;

    public double? LayoutFontSize { get; init; }

    /// <summary>Gets the folder whose files Markdown and HTML documents can load, or null for the folder of each document.</summary>
    public FullPath? HtmlRoot { get; init; }

    // EPUB input
    public EpubMode EpubMode { get; init; }

    // Markdown input
    public string MarkdownExtensions { get; init; } = "advanced";

    public bool MarkdownDisableHtml { get; init; }

    // HTML output
    public bool HtmlFragment { get; init; }

    public string? HtmlTitle { get; init; }

    public IReadOnlyList<string> HtmlStylesheets { get; init; } = [];

    // Markdown output
    public HtmlToMarkdownOptions MarkdownOutput { get; init; } = new();

    // Comic book archive output
    public ComicImageFormat ComicImageFormat { get; init; }

    // Archives
    public string? ArchivePassword { get; init; }

    public CompressionLevel? ArchiveCompressionLevel { get; init; }

    public ZipMethod? ZipMethod { get; init; }

    public string? ZipPassword { get; init; }

    public ZipEncryptionMethod ZipEncryption { get; init; }

    public SevenZipCompressionMethod? SevenZipMethod { get; init; }

    public bool SevenZipSolid { get; init; } = true;

    public string? SevenZipPassword { get; init; }

    public bool SevenZipEncryptHeaders { get; init; }

    // Mailboxes
    public MboxFormat MboxFormat { get; init; } = MboxFormat.Mboxrd;

    public bool EmailIncludeNonMailItems { get; init; }

    /// <summary>Gets the format of a PST output, or null for the default: Unicode for PST, Unicode4K for OST.</summary>
    public PstFormat? PstFileFormat { get; init; }

    public PstEncryption PstEncryption { get; init; } = PstEncryption.Permute;

    /// <summary>Gets the options of the conversion of a mailbox to <paramref name="outputFormat"/>.</summary>
    /// <exception cref="ConversionException">The <c>--pst-format</c> is not one of the output format.</exception>
    public MailboxConversionOptions CreateMailboxOptions(FileFormat outputFormat)
    {
        // The Unicode4K format is the format of the OST files: the writer writes the signature of an OST file
        var isOst = outputFormat == FileFormat.Ost;
        var pstFormat = PstFileFormat ?? (isOst ? PstFormat.Unicode4K : PstFormat.Unicode);
        if (isOst && pstFormat is not PstFormat.Unicode4K)
            throw new ConversionException("An OST file has the Unicode4K format: remove --pst-format");

        if (!isOst && pstFormat is PstFormat.Unicode4K && outputFormat.MailboxFormat is Email.Storage.MailboxFormat.Pst)
            throw new ConversionException("The Unicode4K format is the format of the OST files: use --output-format ost, or --pst-format Ansi or Unicode");

        return new MailboxConversionOptions
        {
            MboxFormat = MboxFormat,
            IncludeNonMailItems = EmailIncludeNonMailItems,
            PstWriterOptions = new PstWriterOptions { Format = pstFormat, Encryption = PstEncryption },
        };
    }

    /// <summary>Gets a value indicating whether the images are resized, so that the encoded images cannot be copied as they are.</summary>
    public bool ResizesImages => MaxWidth is not null || MaxHeight is not null;

    /// <summary>Creates the encoder of an image format with the settings of the command line.</summary>
    /// <param name="format">The image format.</param>
    /// <param name="extension">The extension of the output file, which selects a variant of some formats (<c>.apng</c>), or null.</param>
    public ImageEncoder CreateEncoder(ImageFormat format, string? extension)
    {
        ImageEncoder encoder;
        try
        {
            encoder = extension is not null && FileFormat.FromExtension(extension)?.ImageFormat == format ? ImageEncoder.FromFileExtension(extension) : ImageEncoder.Create(format);
        }
        catch (ArgumentException)
        {
            encoder = ImageEncoder.Create(format);
        }

        return encoder switch
        {
            JpegEncoder jpeg => new JpegEncoder
            {
                Quality = JpegQuality ?? jpeg.Quality,
                BackgroundColor = Background,
            },
            WebPEncoder webp => new WebPEncoder
            {
                Compression = WebPLossless ? WebPCompression.Lossless : webp.Compression,
                Quality = WebPQuality ?? webp.Quality,
            },
            AvifEncoder avif => new AvifEncoder
            {
                Compression = AvifLossless ? AvifCompression.Lossless : avif.Compression,
                Quality = AvifQuality ?? avif.Quality,
                Effort = AvifEffort ?? avif.Effort,
            },
            HeicEncoder heic => new HeicEncoder
            {
                Quality = HeicQuality ?? heic.Quality,
            },
            JpegXlEncoder jxl => new JpegXlEncoder
            {
                Compression = JpegXlLossless ? JpegXlCompression.Lossless : jxl.Compression,
                Quality = JpegXlQuality ?? jxl.Quality,
            },
            PngEncoder png => new PngEncoder
            {
                AnimationMode = png.AnimationMode,
                CompressionLevel = PngCompressionLevel ?? png.CompressionLevel,
            },
            TiffEncoder tiff => new TiffEncoder
            {
                Compression = TiffCompression ?? tiff.Compression,
            },
            GifEncoder gif => GifMaxColors is { } maxColors ? new GifEncoder { Quantization = new QuantizeOptions { MaxColors = maxColors } } : gif,
            _ => encoder,
        };
    }

    /// <summary>Gets a value indicating whether the command line sets an option of the encoder of an image format, so that an image of this format must be encoded again.</summary>
    public bool HasEncoderOptions(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => JpegQuality is not null,
        ImageFormat.WebP => WebPQuality is not null || WebPLossless,
        ImageFormat.Png => PngCompressionLevel is not null,
        ImageFormat.Avif => AvifQuality is not null || AvifEffort is not null || AvifLossless,
        ImageFormat.Heic => HeicQuality is not null,
        ImageFormat.JpegXl => JpegXlQuality is not null || JpegXlLossless,
        ImageFormat.Tiff => TiffCompression is not null,
        ImageFormat.Gif => GifMaxColors is not null,
        _ => false,
    };

    /// <summary>Resizes an image to fit <see cref="MaxWidth"/> x <see cref="MaxHeight"/>, keeping its aspect ratio. The image is never enlarged.</summary>
    public void ResizeImage(Image image, CancellationToken cancellationToken)
    {
        if (!ResizesImages)
            return;

        var width = MaxWidth ?? int.MaxValue;
        var height = MaxHeight ?? int.MaxValue;
        if (image.Size.Width <= width && image.Size.Height <= height)
            return;

        var scale = Math.Min((double)width / image.Size.Width, (double)height / image.Size.Height);
        var newWidth = Math.Max(1, (int)Math.Round(image.Size.Width * scale, MidpointRounding.AwayFromZero));
        var newHeight = Math.Max(1, (int)Math.Round(image.Size.Height * scale, MidpointRounding.AwayFromZero));
        image.Resize(new ResizeOptions(newWidth, newHeight) { Mode = ResizeMode.Stretch }, cancellationToken);
    }
}
