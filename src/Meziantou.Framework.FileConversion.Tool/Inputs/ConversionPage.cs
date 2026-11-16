using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf;

namespace Meziantou.Framework.FileConversionTool.Inputs;

/// <summary>A page of an input: an encoded image, a page of a PDF document, or something to render as an image.</summary>
internal abstract class ConversionPage
{
    protected ConversionPage(int pageNumber)
    {
        PageNumber = pageNumber;
    }

    /// <summary>Gets the 1-based number of the page in its input.</summary>
    public int PageNumber { get; }

    /// <summary>Gets the encoded image of the page, when the page is an image file, so that it can be copied without decoding it.</summary>
    public virtual EncodedImage? GetEncodedImage() => null;

    /// <summary>Gets the page as a PDF page, when the page is vector content that a PDF output imports as it is.</summary>
    public virtual PdfPage? GetPdfPage() => null;

    /// <summary>Gets the page as an image. The caller owns the image.</summary>
    public abstract Image RenderImage(CancellationToken cancellationToken);
}
