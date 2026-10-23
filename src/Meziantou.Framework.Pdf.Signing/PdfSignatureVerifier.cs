using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// Finds and checks the signatures of a document (ISO 32000-2, 12.8): for each signature field, the bytes the signature
/// covers are hashed and compared with the digest that was signed, the CMS signature is checked with the certificate of
/// the signer, and the RFC 3161 time stamp is checked.
/// </summary>
public static class PdfSignatureVerifier
{
    private const string SignatureTimeStampTokenOid = "1.2.840.113549.1.9.16.2.14";
    private const int MaxFieldDepth = 32;

    private static readonly PdfName FieldsName = PdfName.Create("Fields");
    private static readonly PdfName FieldTypeName = PdfName.Create("FT");
    private static readonly PdfName FieldNameName = PdfName.Create("T");
    private static readonly PdfName ValueName = PdfName.Create("V");
    private static readonly PdfName ByteRangeName = PdfName.Create("ByteRange");
    private static readonly PdfName SubFilterName = PdfName.Create("SubFilter");

    /// <summary>Checks the signatures of a file.</summary>
    /// <param name="file">The file.</param>
    /// <param name="options">The options to read the file with, such as the password of an encrypted file.</param>
    /// <returns>The signatures, in the order of the fields of the interactive form.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not a PDF document.</exception>
    /// <exception cref="PdfPasswordException">The document is encrypted, and the password of the options does not open it.</exception>
    public static IReadOnlyList<PdfSignatureInfo> Verify(byte[] file, PdfReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        var document = PdfDocument.Open(file, options);
        var result = new List<PdfSignatureInfo>();
        if (document.Catalog.GetResolved(PdfNames.AcroForm) is PdfDictionary form && form.GetResolved(FieldsName) is PdfArray fields)
        {
            var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            foreach (var field in fields)
            {
                VisitField(file, PdfReference.ResolveIfReference(field) as PdfDictionary, parentName: null, inheritedType: null, visited, result, depth: 0);
            }
        }

        return result;
    }

    private static void VisitField(byte[] file, PdfDictionary? field, string? parentName, string? inheritedType, HashSet<PdfDictionary> visited, List<PdfSignatureInfo> result, int depth)
    {
        if (field is null || depth > MaxFieldDepth || !visited.Add(field))
            return;

        // The name and the type of a field are inherited from its parents (12.7.4.2)
        var partialName = field.GetText(FieldNameName);
        var name = partialName is null ? parentName : parentName is null ? partialName : parentName + "." + partialName;
        var type = field.GetName(FieldTypeName)?.Value ?? inheritedType;
        if (type == "Sig" && field.GetResolved(ValueName) is PdfDictionary signature)
        {
            result.Add(Check(file, name ?? "", signature));
        }

        if (field.GetResolved(PdfNames.Kids) is PdfArray kids)
        {
            foreach (var kid in kids)
            {
                VisitField(file, PdfReference.ResolveIfReference(kid) as PdfDictionary, name, type, visited, result, depth + 1);
            }
        }
    }

    private static PdfSignatureInfo Check(byte[] file, string fieldName, PdfDictionary signature)
    {
        var info = new PdfSignatureInfo
        {
            FieldName = fieldName,
            SubFilter = signature.GetName(SubFilterName)?.Value,
            SignerName = signature.GetText(PdfName.Create("Name")),
            Reason = signature.GetText(PdfName.Create("Reason")),
            Location = signature.GetText(PdfName.Create("Location")),
            ContactInfo = signature.GetText(PdfName.Create("ContactInfo")),
            SigningTime = PdfDate.TryParse(signature.GetText(PdfName.Create("M")), out var time) ? time : null,
        };

        try
        {
            var ranges = ReadByteRanges(signature, file.Length);
            info.ByteRanges = ranges;
            info.CoversWholeDocument = ranges.Count > 0 && ranges[0].Offset == 0 && ranges[^1].Offset + ranges[^1].Length == file.Length;
            var data = new byte[ranges.Sum(range => range.Length)];
            var position = 0;
            foreach (var (offset, length) in ranges)
            {
                file.AsSpan((int)offset, (int)length).CopyTo(data.AsSpan(position));
                position += (int)length;
            }

            if (!signature.TryGetResolved<PdfString>(PdfNames.Contents, out var contents))
                throw new InvalidPdfContentException("The signature has no /Contents");

            var encoded = TrimPadding(contents.Span);
            switch (info.SubFilter)
            {
                case null or "adbe.pkcs7.detached" or "ETSI.CAdES.detached":
                    CheckCms(info, encoded, data, detached: true);
                    break;
                case "adbe.pkcs7.sha1":
                    CheckCms(info, encoded, data, detached: false);
                    break;
                case "ETSI.RFC3161":
                    CheckDocumentTimestamp(info, encoded, data);
                    break;
                default:
                    info.Error = $"The signature format '{info.SubFilter}' is not supported";
                    break;
            }
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidPdfContentException or AsnContentException or ArgumentException)
        {
            info.Error = ex.Message;
        }

        return info;
    }

    private static List<(long Offset, long Length)> ReadByteRanges(PdfDictionary signature, long fileLength)
    {
        if (signature.GetResolved(ByteRangeName) is not PdfArray array || array.Count == 0 || array.Count % 2 != 0)
            throw new InvalidPdfContentException("The signature has no valid /ByteRange");

        var result = new List<(long, long)>();
        long end = 0;
        for (var i = 0; i < array.Count; i += 2)
        {
            if (!array.TryGetResolved<PdfInteger>(i, out var offset) || !array.TryGetResolved<PdfInteger>(i + 1, out var length))
                throw new InvalidPdfContentException("The /ByteRange of the signature has a value that is not an integer");

            if (offset.Value < end || length.Value < 0 || offset.Value + length.Value > fileLength || offset.Value + length.Value > int.MaxValue)
                throw new InvalidPdfContentException("The /ByteRange of the signature is outside the file or not in order");

            end = offset.Value + length.Value;
            result.Add((offset.Value, length.Value));
        }

        return result;
    }

    /// <summary>The signature of <c>/Contents</c> without the zeros of the space reserved after it.</summary>
    private static byte[] TrimPadding(ReadOnlySpan<byte> contents)
    {
        AsnDecoder.ReadEncodedValue(contents, AsnEncodingRules.BER, out _, out _, out var length);
        return contents[..length].ToArray();
    }

    private static void CheckCms(PdfSignatureInfo info, byte[] encoded, byte[] data, bool detached)
    {
        var cms = detached ? new SignedCms(new ContentInfo(data), detached: true) : new SignedCms();
        cms.Decode(encoded);
        if (cms.SignerInfos.Count == 0)
            throw new InvalidPdfContentException("The signature has no signer");

        var signer = cms.SignerInfos[0];
        info.Certificate = signer.Certificate;
        info.Certificates.AddRange(cms.Certificates);

        // The integrity of the document: the digest of its bytes (adbe.pkcs7.sha1: the SHA-1 digest is the signed content)
        if (detached)
        {
            var messageDigest = signer.SignedAttributes.OfType<CryptographicAttributeObject>()
                .Where(attribute => attribute.Oid.Value == "1.2.840.113549.1.9.4")
                .SelectMany(attribute => attribute.Values.Cast<AsnEncodedData>())
                .Select(value => AsnDecoder.ReadOctetString(value.RawData, AsnEncodingRules.BER, out _))
                .FirstOrDefault();
            info.IsDocumentIntact = messageDigest is not null && Hash(signer.DigestAlgorithm, data).AsSpan().SequenceEqual(messageDigest);
        }
        else
        {
            var digest = Sha1(data);
            info.IsDocumentIntact = cms.ContentInfo.Content.AsSpan().SequenceEqual(digest) || EncapsulatedOctets(cms.ContentInfo.Content).SequenceEqual(digest);
        }

        try
        {
            signer.CheckSignature(verifySignatureOnly: true);
            info.IsSignatureValid = true;
        }
        catch (CryptographicException)
        {
            info.IsSignatureValid = false;
        }

        // The time stamp of the signature value (PAdES B-T)
        foreach (var attribute in signer.UnsignedAttributes)
        {
            if (attribute.Oid.Value != SignatureTimeStampTokenOid)
                continue;

            foreach (var value in attribute.Values)
            {
                if (Rfc3161TimestampToken.TryDecode(value.RawData, out var token, out _))
                {
                    info.TimestampTime = token.TokenInfo.Timestamp;
                    info.IsTimestampValid = token.VerifySignatureForSignerInfo(signer, out _);
                }
                else
                {
                    info.IsTimestampValid = false;
                }
            }
        }
    }

    private static void CheckDocumentTimestamp(PdfSignatureInfo info, byte[] encoded, byte[] data)
    {
        if (!Rfc3161TimestampToken.TryDecode(encoded, out var token, out _))
            throw new InvalidPdfContentException("The document time stamp is not an RFC 3161 time stamp token");

        var cms = token.AsSignedCms();
        info.Certificates.AddRange(cms.Certificates);
        info.TimestampTime = token.TokenInfo.Timestamp;
        var algorithm = token.TokenInfo.HashAlgorithmId;
        info.IsDocumentIntact = Hash(algorithm, data).AsSpan().SequenceEqual(token.TokenInfo.GetMessageHash().Span);
        info.IsSignatureValid = token.VerifySignatureForData(data, out var signer);
        info.Certificate = signer ?? (cms.SignerInfos.Count > 0 ? cms.SignerInfos[0].Certificate : null);
        info.IsTimestampValid = info.IsSignatureValid;
    }

    /// <summary>The content of an OCTET STRING, for the encapsulated content of <c>adbe.pkcs7.sha1</c> that some writers wrap again.</summary>
    private static ReadOnlySpan<byte> EncapsulatedOctets(byte[] content)
    {
        try
        {
            return AsnDecoder.ReadOctetString(content, AsnEncodingRules.BER, out _);
        }
        catch (AsnContentException)
        {
            return [];
        }
    }

    // The signatures of older documents use SHA-1: they are checked, not created
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
    private static byte[] Sha1(byte[] data) => SHA1.HashData(data);
#pragma warning restore CA5350

    private static byte[] Hash(Oid algorithm, byte[] data) => algorithm.Value switch
    {
        "1.3.14.3.2.26" => Sha1(data),
        "2.16.840.1.101.3.4.2.1" => SHA256.HashData(data),
        "2.16.840.1.101.3.4.2.2" => SHA384.HashData(data),
        "2.16.840.1.101.3.4.2.3" => SHA512.HashData(data),
        _ => throw new CryptographicException($"The digest algorithm '{algorithm.Value}' is not supported"),
    };
}
