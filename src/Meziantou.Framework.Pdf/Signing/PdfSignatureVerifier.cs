using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// Finds and checks the signatures of a document (ISO 32000-2, 12.8): for each signature field, the bytes the signature
/// covers are hashed and compared with the digest that was signed, the CMS signature is checked with the certificate of
/// the signer, the RFC 3161 time stamps are checked, and the changes made after the signature are compared with what its
/// certification and its field lock allow.
/// </summary>
/// <remarks>
/// The information of a signature is read from the revision it signs: a change of the signature dictionary appended
/// after it does not change what is reported, and is reported in <see cref="PdfSignatureInfo.Modifications"/>.
/// </remarks>
public static class PdfSignatureVerifier
{
    /// <summary>Checks the signatures of a file.</summary>
    /// <param name="file">The file.</param>
    /// <param name="options">The options to read the file with, such as the password of an encrypted file.</param>
    /// <returns>The signatures, in the order of the fields of the interactive form.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not a PDF document.</exception>
    /// <exception cref="PdfPasswordException">The document is encrypted, and the password of the options does not open it.</exception>
    public static IReadOnlyList<PdfSignatureInfo> Verify(byte[] file, PdfReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        var context = new VerificationContext(file, options, PdfDocument.Open(file, options));
        var result = new List<PdfSignatureInfo>();
        foreach (var field in PdfSignatureFields.GetFields(context.Document))
        {
            if (field.Type == "Sig" && field.Field.GetResolved(PdfSignatureFields.ValueName) is PdfDictionary signature)
            {
                result.Add(Check(context, field, signature));
            }
        }

        return result;
    }

    /// <summary>Reads the validation data of the document security store of a file (ISO 32000-2, 12.8.4.3).</summary>
    /// <param name="file">The file.</param>
    /// <param name="options">The options to read the file with, such as the password of an encrypted file.</param>
    /// <returns>The certificates, the revocation lists and the OCSP responses of the store; empty when the document has none.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not a PDF document.</exception>
    /// <exception cref="PdfPasswordException">The document is encrypted, and the password of the options does not open it.</exception>
    public static PdfValidationData GetValidationData(byte[] file, PdfReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        var document = PdfDocument.Open(file, options);
        var result = new PdfValidationData();
        if (document.Catalog.GetResolved(PdfSignatureFields.DssName) is not PdfDictionary store)
            return result;

        foreach (var data in ReadStreams(store, "Certs"))
        {
            try
            {
                result.Certificates.Add(X509CertificateLoader.LoadCertificate(data));
            }
            catch (CryptographicException)
            {
                // A certificate that cannot be read is not returned
            }
        }

        foreach (var data in ReadStreams(store, "CRLs"))
        {
            result.CertificateRevocationLists.Add(data);
        }

        foreach (var data in ReadStreams(store, "OCSPs"))
        {
            result.OcspResponses.Add(data);
        }

        return result;
    }

    private static IEnumerable<byte[]> ReadStreams(PdfDictionary store, string key)
    {
        if (store.GetResolved(PdfName.Create(key)) is not PdfArray array)
            yield break;

        foreach (var item in array)
        {
            if (PdfReference.ResolveIfReference(item) is not PdfStream stream)
                continue;

            byte[] data;
            try
            {
                data = stream.GetDecodedData();
            }
            catch (PdfException)
            {
                continue;
            }

            yield return data;
        }
    }

    private static PdfSignatureInfo Check(VerificationContext context, PdfFieldEntry field, PdfDictionary signature)
    {
        var file = context.File;
        var info = new PdfSignatureInfo
        {
            FieldName = field.Name,
            SubFilter = signature.GetName(PdfSignatureFields.SubFilterName)?.Value,
        };

        try
        {
            var ranges = ReadByteRanges(signature, file);
            info.ByteRanges = ranges;

            // The ranges cover the revision from its first byte; the whole file when the last one ends at its end
            var revisionLength = ranges[1].Offset + ranges[1].Length;
            info.CoversWholeDocument = revisionLength == file.Length;

            // The signature dictionary as it was signed: a later revision can change the one of the current document
            var signed = signature;
            PdfDocument? revision = null;
            if (!info.CoversWholeDocument)
            {
                revision = context.GetRevision(revisionLength);
                signed = FindSignedDictionary(revision, field) ?? throw new InvalidPdfContentException("The signature dictionary is not in the revision the signature covers");
                if (!ReadByteRanges(signed, file).SequenceEqual(ranges))
                    throw new InvalidPdfContentException("The /ByteRange of the signature dictionary was changed after the document was signed");
            }

            info.SubFilter = signed.GetName(PdfSignatureFields.SubFilterName)?.Value;
            info.SignerName = signed.GetText(PdfName.Create("Name"));
            info.Reason = signed.GetText(PdfName.Create("Reason"));
            info.Location = signed.GetText(PdfName.Create("Location"));
            info.ContactInfo = signed.GetText(PdfName.Create("ContactInfo"));
            info.SigningTime = PdfDate.TryParse(signed.GetText(PdfName.Create("M")), out var time) ? time : null;
            info.CertificationLevel = PdfSignatureFields.GetCertificationLevel(signed);
            info.FieldLock = PdfSignatureFields.GetFieldLock(signed);

            if (revision is not null)
            {
                info.Modifications = context.GetModifications(revisionLength, revision);
                info.AreModificationsPermitted = info.Modifications.All(modification => PdfModificationAnalyzer.IsPermitted(modification, info.CertificationLevel, info.FieldLock));
            }

            if (!signed.TryGetResolved<PdfString>(PdfNames.Contents, out var contents))
                throw new InvalidPdfContentException("The signature has no /Contents");

            if (!IsContentsGap(file, ranges, contents.Span))
                throw new InvalidPdfContentException("The bytes the /ByteRange leaves out are not the /Contents of the signature");

            var encoded = TrimPadding(contents.Span);
            var signedBytes = new SignedBytes(file, (int)ranges[0].Length, (int)ranges[1].Offset, (int)ranges[1].Length);
            switch (info.SubFilter)
            {
                case null or "adbe.pkcs7.detached" or "ETSI.CAdES.detached":
                    CheckCms(info, encoded, signedBytes, detached: true);
                    break;
                case "adbe.pkcs7.sha1":
                    CheckCms(info, encoded, signedBytes, detached: false);
                    break;
                case "ETSI.RFC3161":
                    CheckDocumentTimestamp(info, encoded, signedBytes);
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

    /// <summary>The signature dictionary of the field in the revision the signature covers.</summary>
    private static PdfDictionary? FindSignedDictionary(PdfDocument? revision, PdfFieldEntry field)
    {
        if (revision is null)
            return null;

        if (field.Field[PdfSignatureFields.ValueName] is PdfReference value)
            return Resolve(revision, value) as PdfDictionary;

        // A direct signature dictionary is part of its field
        if (field.Reference is { } reference && Resolve(revision, reference) is PdfDictionary signedField)
            return signedField.GetResolved(PdfSignatureFields.ValueName) as PdfDictionary;

        return null;
    }

    private static PdfObject? Resolve(PdfDocument document, PdfReference reference)
    {
        if (document.Objects.GetEntry(reference.ObjectNumber) is not { } entry || entry.Reference.Generation != reference.Generation)
            return null;

        return document.Objects.Resolve(entry.Reference);
    }

    /// <summary>
    /// Reads the byte ranges (12.8.1): two ranges, from the start of the file to the <c>/Contents</c> of the signature
    /// and from its end, so that the signature covers every byte of its revision but itself.
    /// </summary>
    private static List<(long Offset, long Length)> ReadByteRanges(PdfDictionary signature, byte[] file)
    {
        if (signature.GetResolved(PdfSignatureFields.ByteRangeName) is not PdfArray { Count: 4 } array)
            throw new InvalidPdfContentException("The /ByteRange of the signature does not have two ranges");

        var values = new long[4];
        for (var i = 0; i < 4; i++)
        {
            if (!array.TryGetResolved<PdfInteger>(i, out var value))
                throw new InvalidPdfContentException("The /ByteRange of the signature has a value that is not an integer");

            values[i] = value.Value;
        }

        // Each value is checked alone, so that no sum overflows
        var (offset1, length1, offset2, length2) = (values[0], values[1], values[2], values[3]);
        if (offset1 != 0 || length1 <= 0 || length1 > file.Length || offset2 < length1 || offset2 > file.Length || length2 <= 0 || length2 > file.Length - offset2)
            throw new InvalidPdfContentException("The /ByteRange of the signature is outside the file, not in order, or does not start at the start of the file");

        return [(offset1, length1), (offset2, length2)];
    }

    /// <summary>
    /// Gets a value indicating whether the bytes between the ranges are the hexadecimal string of the <c>/Contents</c> of
    /// this signature, and nothing else: a gap that left out other bytes would let them change without the signature.
    /// </summary>
    private static bool IsContentsGap(byte[] file, List<(long Offset, long Length)> ranges, ReadOnlySpan<byte> contents)
    {
        // The gap is the string with its delimiters, or only its digits between the delimiters some writers sign
        var start = (int)ranges[0].Length;
        var end = (int)ranges[1].Offset;
        ReadOnlySpan<byte> digits;
        if (end - start >= 2 && file[start] == '<' && file[end - 1] == '>')
        {
            digits = file.AsSpan(start + 1, end - start - 2);
        }
        else if (start > 0 && end < file.Length && file[start - 1] == '<' && file[end] == '>')
        {
            digits = file.AsSpan(start, end - start);
        }
        else
        {
            return false;
        }

        var decoded = new byte[(digits.Length + 1) / 2];
        var count = 0;
        var high = -1;
        foreach (var b in digits)
        {
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f' or 0)
                continue;

            var digit = b switch
            {
                >= (byte)'0' and <= (byte)'9' => b - '0',
                >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
                >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
                _ => -1,
            };
            if (digit < 0)
                return false;

            if (high < 0)
            {
                high = digit;
            }
            else
            {
                decoded[count++] = (byte)((high << 4) | digit);
                high = -1;
            }
        }

        if (high >= 0)
        {
            decoded[count++] = (byte)(high << 4);
        }

        return decoded.AsSpan(0, count).SequenceEqual(contents);
    }

    /// <summary>The signature of <c>/Contents</c> without the zeros of the space reserved after it.</summary>
    private static byte[] TrimPadding(ReadOnlySpan<byte> contents)
    {
        AsnDecoder.ReadEncodedValue(contents, AsnEncodingRules.BER, out _, out _, out var length);
        return contents[..length].ToArray();
    }

    private static void CheckCms(PdfSignatureInfo info, byte[] encoded, SignedBytes data, bool detached)
    {
        // The content is given to SignedCms only when it must check it itself: decoding does not need it
        var cms = detached ? new SignedCms(new ContentInfo([]), detached: true) : new SignedCms();
        cms.Decode(encoded);

        // A PDF signature has one signer (ISO 32000-2, 12.8.3.3.1; ETSI EN 319 142-1, 6.3): another signer would not be checked
        if (cms.SignerInfos.Count != 1)
            throw new InvalidPdfContentException($"The signature has {cms.SignerInfos.Count} signers: a PDF signature has one");

        var signer = cms.SignerInfos[0];
        info.Certificate = signer.Certificate;
        info.Certificates.AddRange(cms.Certificates);

        if (!detached)
        {
            // adbe.pkcs7.sha1: the SHA-1 digest of the signed bytes is the encapsulated content, which the signature covers
            var digest = CmsSignature.Hash(HashAlgorithmName.SHA1, data.First, data.Second);
            info.IsDocumentIntact = cms.ContentInfo.Content.AsSpan().SequenceEqual(digest) || EncapsulatedOctets(cms.ContentInfo.Content).SequenceEqual(digest);
            info.IsSignatureValid = CheckSignature(signer);
        }
        else if (signer.SignedAttributes.Count == 0)
        {
            // Without signed attributes, the signature is computed on the bytes themselves (RFC 5652, 5.4): SignedCms checks it
            var content = data.ToArray();
            var withContent = new SignedCms(new ContentInfo(content), detached: true);
            withContent.Decode(encoded);
            info.IsSignatureValid = CheckSignature(withContent.SignerInfos[0]);
            info.IsDocumentIntact = info.IsSignatureValid;
        }
        else
        {
            // The signed attributes have one message digest, of the signed bytes (RFC 5652, 11.2)
            var messageDigests = signer.SignedAttributes.OfType<CryptographicAttributeObject>()
                .Where(attribute => attribute.Oid.Value == CmsSignature.MessageDigestOid)
                .SelectMany(attribute => attribute.Values.Cast<AsnEncodedData>())
                .Select(value => AsnDecoder.ReadOctetString(value.RawData, AsnEncodingRules.BER, out _))
                .ToList();
            var algorithm = CmsSignature.GetHashAlgorithm(signer.DigestAlgorithm.Value) ?? throw new CryptographicException($"The digest algorithm '{signer.DigestAlgorithm.Value}' is not supported");
            info.IsDocumentIntact = messageDigests.Count == 1 && CmsSignature.Hash(algorithm, data.First, data.Second).AsSpan().SequenceEqual(messageDigests[0]);

            // The signature of the attributes is checked without the content when the algorithm is known, by SignedCms otherwise
            var signerData = CmsSignature.ReadSigners(encoded);
            bool? isValid = signerData.Count == 1 && signer.Certificate is not null ? CmsSignature.VerifySignedAttributes(signerData[0], signer.Certificate) : null;
            if (isValid is null)
            {
                var withContent = new SignedCms(new ContentInfo(data.ToArray()), detached: true);
                withContent.Decode(encoded);
                isValid = CheckSignature(withContent.SignerInfos[0]);
            }

            info.IsSignatureValid = isValid.Value;
        }

        // The ESS signing certificate binds the signature to the certificate of the signer; PAdES requires it
        info.IsSigningCertificateValid = CmsSignature.CheckSigningCertificate(signer, signer.Certificate) ?? (info.SubFilter == "ETSI.CAdES.detached" ? false : null);

        // The time stamps of the signature value (PAdES B-T): each of them is checked, an invalid one is not hidden by a
        // valid one, and the time is the earliest of the valid ones
        var timestamps = new List<(DateTimeOffset Time, bool IsValid)>();
        var allDecoded = true;
        foreach (var attribute in signer.UnsignedAttributes)
        {
            if (attribute.Oid.Value != CmsSignature.SignatureTimeStampTokenOid)
                continue;

            foreach (var value in attribute.Values)
            {
                if (Rfc3161TimestampToken.TryDecode(value.RawData, out var token, out _))
                {
                    timestamps.Add((token.TokenInfo.Timestamp, token.VerifySignatureForSignerInfo(signer, out _)));
                }
                else
                {
                    allDecoded = false;
                }
            }
        }

        if (timestamps.Count > 0 || !allDecoded)
        {
            info.IsTimestampValid = allDecoded && timestamps.TrueForAll(timestamp => timestamp.IsValid);
            var candidates = timestamps.Exists(timestamp => timestamp.IsValid) ? timestamps.FindAll(timestamp => timestamp.IsValid) : timestamps;
            info.TimestampTime = candidates.Count > 0 ? candidates.Min(timestamp => timestamp.Time) : null;
        }
    }

    private static bool CheckSignature(SignerInfo signer)
    {
        try
        {
            signer.CheckSignature(verifySignatureOnly: true);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static void CheckDocumentTimestamp(PdfSignatureInfo info, byte[] encoded, SignedBytes data)
    {
        if (!Rfc3161TimestampToken.TryDecode(encoded, out var token, out _))
            throw new InvalidPdfContentException("The document time stamp is not an RFC 3161 time stamp token");

        var cms = token.AsSignedCms();
        info.Certificates.AddRange(cms.Certificates);
        info.TimestampTime = token.TokenInfo.Timestamp;
        var algorithmId = token.TokenInfo.HashAlgorithmId;
        var algorithm = CmsSignature.GetHashAlgorithm(algorithmId.Value) ?? throw new CryptographicException($"The digest algorithm '{algorithmId.Value}' is not supported");
        var hash = CmsSignature.Hash(algorithm, data.First, data.Second);
        info.IsDocumentIntact = hash.AsSpan().SequenceEqual(token.TokenInfo.GetMessageHash().Span);
        info.IsSignatureValid = token.VerifySignatureForHash(hash, algorithmId, out var signer);
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

    /// <summary>The bytes a signature covers: the two ranges of the file, read in place.</summary>
    private readonly struct SignedBytes(byte[] file, int firstLength, int secondOffset, int secondLength)
    {
        public ReadOnlySpan<byte> First => file.AsSpan(0, firstLength);

        public ReadOnlySpan<byte> Second => file.AsSpan(secondOffset, secondLength);

        /// <summary>The bytes in one array, for the checks <see cref="SignedCms"/> makes on the content itself.</summary>
        public byte[] ToArray()
        {
            var result = new byte[firstLength + secondLength];
            First.CopyTo(result);
            Second.CopyTo(result.AsSpan(firstLength));
            return result;
        }
    }

    /// <summary>The file, and the revisions the signatures cover, read once for all the signatures of the file.</summary>
    private sealed class VerificationContext(byte[] file, PdfReadOptions? options, PdfDocument document)
    {
        private readonly Dictionary<long, PdfDocument?> _revisions = [];
        private readonly Dictionary<long, List<PdfModification>> _modifications = [];

        public byte[] File { get; } = file;

        public PdfDocument Document { get; } = document;

        public PdfDocument? GetRevision(long length)
        {
            if (!_revisions.TryGetValue(length, out var revision))
            {
                try
                {
                    revision = PdfDocument.Open(File.AsMemory(0, (int)length), options);
                }
                catch (PdfException)
                {
                    revision = null;
                }

                _revisions[length] = revision;
            }

            return revision;
        }

        public List<PdfModification> GetModifications(long length, PdfDocument revision)
        {
            if (!_modifications.TryGetValue(length, out var modifications))
            {
                modifications = PdfModificationAnalyzer.Compare(revision, Document);
                _modifications[length] = modifications;
            }

            return modifications;
        }
    }
}
