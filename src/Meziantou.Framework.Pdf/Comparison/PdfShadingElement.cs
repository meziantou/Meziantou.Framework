namespace Meziantou.Framework.Pdf;

/// <summary>A shading a page paints (ISO 32000-2, 8.7.4.5), with the <c>sh</c> operator or as a shading pattern.</summary>
/// <param name="ShadingType">The type of the shading: 1 to 7.</param>
/// <param name="Matrix">The matrix from the space of the shading to default user space.</param>
/// <param name="Digest">A SHA-256 digest of the dictionary of the shading, in hexadecimal.</param>
public sealed record PdfShadingElement(int ShadingType, PdfMatrix Matrix, string Digest)
{
    /// <inheritdoc/>
    public override string ToString() => $"shading type {ShadingType} {Digest[..Math.Min(8, Digest.Length)]} {Matrix}";
}
