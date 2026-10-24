using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// The CMS signed data of a PAdES signature (ETSI EN 319 142-1, CAdES detached): the signed attributes content-type,
/// message-digest and ESS signing-certificate-v2, without signing-time (the time is the <c>/M</c> of the signature
/// dictionary), and the RFC 3161 time stamp of the signature value as an unsigned attribute for B-T.
/// </summary>
internal static class CmsSignature
{
    private const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";
    private const string SignatureTimeStampTokenOid = "1.2.840.113549.1.9.16.2.14";

    public static async Task<byte[]> CreateAsync(ReadOnlyMemory<byte> content, PdfSignatureOptions options, CancellationToken cancellationToken)
    {
        var cms = new SignedCms(new ContentInfo(content.ToArray()), detached: true);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, options.Certificate)
        {
            DigestAlgorithm = GetOid(options.HashAlgorithm),
            IncludeOption = X509IncludeOption.EndCertOnly,
        };
        signer.Certificates.AddRange(options.Certificates);
        signer.SignedAttributes.Add(new AsnEncodedData(SigningCertificateV2Oid, EncodeSigningCertificateV2(options.Certificate)));
        cms.ComputeSignature(signer, silent: true);

        if (options.TimestampAuthority is { } authority)
        {
            var signerInfo = cms.SignerInfos[0];
            var nonce = RandomNumberGenerator.GetBytes(8);
            nonce[0] &= 0x7F;
            var request = Rfc3161TimestampRequest.CreateFromSignerInfo(signerInfo, HashAlgorithmName.SHA256, requestSignerCertificates: true, nonce: nonce);
            var response = await SendAsync(options.HttpClient, authority, request.Encode(), cancellationToken).ConfigureAwait(false);
            var token = request.ProcessResponse(response, out _);
            signerInfo.AddUnsignedAttribute(new AsnEncodedData(SignatureTimeStampTokenOid, token.AsSignedCms().Encode()));
        }

        return cms.Encode();
    }

    internal static Oid GetOid(HashAlgorithmName algorithm) => algorithm.Name switch
    {
        "SHA256" => new Oid("2.16.840.1.101.3.4.2.1"),
        "SHA384" => new Oid("2.16.840.1.101.3.4.2.2"),
        "SHA512" => new Oid("2.16.840.1.101.3.4.2.3"),
        _ => throw new ArgumentException($"The digest algorithm '{algorithm.Name}' is not supported: use SHA-256, SHA-384 or SHA-512", nameof(algorithm)),
    };

    /// <summary>The ESS signing-certificate-v2 attribute (RFC 5035): the SHA-256 digest of the certificate of the signer, and its issuer and serial number.</summary>
    private static byte[] EncodeSigningCertificateV2(X509Certificate2 certificate)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    // The hash algorithm is SHA-256, the default value, which DER leaves out
                    writer.WriteOctetString(SHA256.HashData(certificate.RawData));
                    using (writer.PushSequence())
                    {
                        using (writer.PushSequence())
                        {
                            writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true));
                            writer.WriteEncodedValue(certificate.IssuerName.RawData);
                            writer.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true));
                        }

                        writer.WriteInteger(certificate.SerialNumberBytes.Span);
                    }
                }
            }
        }

        return writer.Encode();
    }

    private static async Task<byte[]> SendAsync(HttpClient? client, Uri authority, byte[] request, CancellationToken cancellationToken)
    {
        HttpClient? ownedClient = null;
        try
        {
            client ??= ownedClient = new HttpClient();
            using var content = new ByteArrayContent(request);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/timestamp-query");
            using var response = await client.PostAsync(authority, content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ownedClient?.Dispose();
        }
    }
}
