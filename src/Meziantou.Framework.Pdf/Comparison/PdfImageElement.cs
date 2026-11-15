namespace Meziantou.Framework.Pdf;

/// <summary>An image a page paints: where, its size in pixels, and a digest of its data.</summary>
/// <param name="Bounds">The bounding box of the image on the page, in default user space.</param>
/// <param name="Width">The width of the image, in pixels.</param>
/// <param name="Height">The height of the image, in pixels.</param>
/// <param name="Digest">The SHA-256 digest of the encoded data of the image, in hexadecimal.</param>
public sealed record PdfImageElement(PdfRectangle Bounds, int Width, int Height, string Digest)
{
    /// <inheritdoc/>
    public override string ToString() => $"image {Width}x{Height} {Digest[..Math.Min(8, Digest.Length)]} {Bounds}";
}
