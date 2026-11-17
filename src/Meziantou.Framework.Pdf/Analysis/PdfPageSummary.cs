using System.Security.Cryptography;

namespace Meziantou.Framework.Pdf;

/// <summary>A summary of a page: its boxes, its rotation, its annotations and a digest of its content.</summary>
public sealed record PdfPageSummary
{
    private PdfPageSummary(PdfRectangle mediaBox, PdfRectangle cropBox, int rotation, int annotationCount, string? contentDigest)
    {
        MediaBox = mediaBox;
        CropBox = cropBox;
        Rotation = rotation;
        AnnotationCount = annotationCount;
        ContentDigest = contentDigest;
    }

    /// <summary>Gets the media box of the page.</summary>
    public PdfRectangle MediaBox { get; }

    /// <summary>Gets the crop box of the page.</summary>
    public PdfRectangle CropBox { get; }

    /// <summary>Gets the rotation of the page, in degrees.</summary>
    public int Rotation { get; }

    /// <summary>Gets the number of annotations of the page.</summary>
    public int AnnotationCount { get; }

    /// <summary>Gets the SHA-256 digest of the decoded content of the page, in hexadecimal, or <see langword="null"/> when the content cannot be decoded.</summary>
    public string? ContentDigest { get; }

    internal static PdfPageSummary Create(PdfPage page)
    {
        string? digest;
        try
        {
            digest = Convert.ToHexStringLower(SHA256.HashData(page.GetContentData()));
        }
        catch (PdfException)
        {
            digest = null;
        }

        int annotationCount;
        try
        {
            annotationCount = page.Annotations.Count;
        }
        catch (PdfException)
        {
            annotationCount = 0;
        }

        return new PdfPageSummary(page.MediaBox, page.CropBox, page.Rotation, annotationCount, digest);
    }
}
