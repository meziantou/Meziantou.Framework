namespace Meziantou.Framework.Pdf;

/// <summary>What <see cref="PdfPrintProductionReport"/> reports for a page: its boxes, its colorants and its issues.</summary>
public sealed class PdfPageProductionInfo
{
    internal PdfPageProductionInfo(int pageIndex, PdfRectangle mediaBox, PdfRectangle cropBox, PdfRectangle? bleedBox, PdfRectangle? trimBox, PdfRectangle? artBox, PdfColorantUsage colorants, IReadOnlyList<string> issues)
    {
        PageIndex = pageIndex;
        MediaBox = mediaBox;
        CropBox = cropBox;
        BleedBox = bleedBox;
        TrimBox = trimBox;
        ArtBox = artBox;
        Colorants = colorants;
        Issues = issues;
    }

    /// <summary>Gets the index of the page.</summary>
    public int PageIndex { get; }

    /// <summary>Gets the media box: the extent of the medium (14.11.2).</summary>
    public PdfRectangle MediaBox { get; }

    /// <summary>Gets the crop box: the region shown or printed, the media box when the page has none.</summary>
    public PdfRectangle CropBox { get; }

    /// <summary>Gets the bleed box the page declares: the region to which the content is clipped in production; <see langword="null"/> when the page has none (it is then the crop box).</summary>
    public PdfRectangle? BleedBox { get; }

    /// <summary>Gets the trim box the page declares: the intended dimensions of the finished page; <see langword="null"/> when the page has none (it is then the crop box).</summary>
    public PdfRectangle? TrimBox { get; }

    /// <summary>Gets the art box the page declares: the extent of its meaningful content; <see langword="null"/> when the page has none (it is then the crop box).</summary>
    public PdfRectangle? ArtBox { get; }

    /// <summary>Gets the colorants and the color spaces of the page.</summary>
    public PdfColorantUsage Colorants { get; }

    /// <summary>Gets the issues found on the page, such as a missing trim box or a trim box outside the bleed box.</summary>
    public IReadOnlyList<string> Issues { get; }
}
