namespace Meziantou.Framework.Pdf.Signing;

/// <summary>A signer info of CMS signed data, as <see cref="CmsSignature.ReadSigners"/> read it.</summary>
internal sealed record CmsSignerData(string DigestAlgorithm, byte[]? SignedAttributes, string SignatureAlgorithm, ReadOnlyMemory<byte>? SignatureParameters, byte[] Signature);
