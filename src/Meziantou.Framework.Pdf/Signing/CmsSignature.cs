using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// The CMS signed data of a PAdES signature (RFC 5652, ETSI EN 319 142-1, CAdES detached): the signed attributes
/// content-type, message-digest and ESS signing-certificate-v2, without signing-time (the time is the <c>/M</c> of the
/// signature dictionary), and the RFC 3161 time stamp of the signature value as an unsigned attribute for B-T.
/// </summary>
/// <remarks>
/// The signed data is encoded here rather than by <see cref="SignedCms"/>, which needs the signed content in one array:
/// the digest of the bytes of the file is computed range by range, so that no copy of the file is made.
/// </remarks>
internal static class CmsSignature
{
    public const string SignedDataOid = "1.2.840.113549.1.7.2";
    public const string DataOid = "1.2.840.113549.1.7.1";
    public const string ContentTypeOid = "1.2.840.113549.1.9.3";
    public const string MessageDigestOid = "1.2.840.113549.1.9.4";
    public const string SigningCertificateOid = "1.2.840.113549.1.9.16.2.12";
    public const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";
    public const string SignatureTimeStampTokenOid = "1.2.840.113549.1.9.16.2.14";

    private const string Sha1Oid = "1.3.14.3.2.26";
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
    private const string Sha384Oid = "2.16.840.1.101.3.4.2.2";
    private const string Sha512Oid = "2.16.840.1.101.3.4.2.3";
    private const string RsaEncryptionOid = "1.2.840.113549.1.1.1";
    private const string Mgf1Oid = "1.2.840.113549.1.1.8";
    private const string RsaPssOid = "1.2.840.113549.1.1.10";
    private const string EcPublicKeyOid = "1.2.840.10045.2.1";

    /// <summary>Creates the signed data of the digest of the signed bytes.</summary>
    public static async Task<byte[]> CreateAsync(byte[] digest, PdfSignatureOptions options, CancellationToken cancellationToken)
    {
        var certificate = options.Certificate;
        var digestAlgorithm = GetOidValue(options.HashAlgorithm);

        // The signed attributes, a DER SET OF sorted by the writer (RFC 5652, 5.3)
        var attributes = new AsnWriter(AsnEncodingRules.DER);
        using (attributes.PushSetOf())
        {
            WriteAttribute(attributes, ContentTypeOid, EncodeObjectIdentifier(DataOid));
            WriteAttribute(attributes, MessageDigestOid, EncodeOctetString(digest));
            WriteAttribute(attributes, SigningCertificateV2Oid, EncodeSigningCertificateV2(certificate));
        }

        var signedAttributes = attributes.Encode();
        var (signatureAlgorithm, signature) = Sign(certificate, options.HashAlgorithm, options.RsaSignaturePadding, signedAttributes, nameof(options));

        // PAdES B-T: the time stamp of the signature value (ETSI EN 319 122-1, 5.3)
        byte[]? timestamp = null;
        if (options.TimestampAuthority is { } authority)
        {
            timestamp = await RequestTimestampAsync(options.HttpClient, authority, SHA256.HashData(signature), HashAlgorithmName.SHA256, cancellationToken).ConfigureAwait(false);
        }

        var certificates = new List<byte[]> { certificate.RawData };
        foreach (var other in options.Certificates)
        {
            if (!certificates.Exists(item => item.AsSpan().SequenceEqual(other.RawData)))
            {
                certificates.Add(other.RawData);
            }
        }

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(SignedDataOid);
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
            using (writer.PushSequence())
            {
                writer.WriteInteger(1);
                using (writer.PushSetOf())
                {
                    WriteAlgorithm(writer, digestAlgorithm, parameters: null);
                }

                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(DataOid);
                }

                using (writer.PushSetOf(new Asn1Tag(TagClass.ContextSpecific, 0)))
                {
                    foreach (var item in certificates)
                    {
                        writer.WriteEncodedValue(item);
                    }
                }

                using (writer.PushSetOf())
                using (writer.PushSequence())
                {
                    // Version 1: the signer is identified by the issuer and the serial number of its certificate
                    writer.WriteInteger(1);
                    using (writer.PushSequence())
                    {
                        writer.WriteEncodedValue(certificate.IssuerName.RawData);
                        writer.WriteInteger(certificate.SerialNumberBytes.Span);
                    }

                    WriteAlgorithm(writer, digestAlgorithm, parameters: null);

                    // The signed attributes are [0] IMPLICIT: the same encoding with another tag
                    var implicitAttributes = (byte[])signedAttributes.Clone();
                    implicitAttributes[0] = 0xA0;
                    writer.WriteEncodedValue(implicitAttributes);
                    writer.WriteEncodedValue(signatureAlgorithm);
                    writer.WriteOctetString(signature);
                    if (timestamp is not null)
                    {
                        using (writer.PushSetOf(new Asn1Tag(TagClass.ContextSpecific, 1)))
                        {
                            WriteAttribute(writer, SignatureTimeStampTokenOid, timestamp);
                        }
                    }
                }
            }
        }

        return writer.Encode();
    }

    /// <summary>Gets a time stamp token (RFC 3161) of a digest from a time stamp authority, as the encoding of its signed data.</summary>
    public static async Task<byte[]> RequestTimestampAsync(HttpClient? client, Uri authority, byte[] hash, HashAlgorithmName hashAlgorithm, CancellationToken cancellationToken)
    {
        var nonce = RandomNumberGenerator.GetBytes(8);
        nonce[0] &= 0x7F;
        var request = Rfc3161TimestampRequest.CreateFromHash(hash, hashAlgorithm, nonce: nonce, requestSignerCertificates: true);
        var response = await SendAsync(client, authority, request.Encode(), cancellationToken).ConfigureAwait(false);
        var token = request.ProcessResponse(response, out _);
        return token.AsSignedCms().Encode();
    }

    internal static Oid GetOid(HashAlgorithmName algorithm) => new(GetOidValue(algorithm));

    private static string GetOidValue(HashAlgorithmName algorithm) => algorithm.Name switch
    {
        "SHA256" => Sha256Oid,
        "SHA384" => Sha384Oid,
        "SHA512" => Sha512Oid,
        _ => throw new ArgumentException($"The digest algorithm '{algorithm.Name}' is not supported: use SHA-256, SHA-384 or SHA-512", nameof(algorithm)),
    };

    /// <summary>Gets the hash algorithm of an object identifier, or <see langword="null"/> when it is not supported.</summary>
    public static HashAlgorithmName? GetHashAlgorithm(string? oid) => oid switch
    {
        Sha1Oid => HashAlgorithmName.SHA1,
        Sha256Oid => HashAlgorithmName.SHA256,
        Sha384Oid => HashAlgorithmName.SHA384,
        Sha512Oid => HashAlgorithmName.SHA512,
        _ => null,
    };

    /// <summary>The digest of the bytes of a file but those of a gap, computed without copying them.</summary>
    public static byte[] Hash(HashAlgorithmName algorithm, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        // The signatures of older documents use SHA-1: they are checked, not created
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
        using var hash = IncrementalHash.CreateHash(algorithm);
#pragma warning restore CA5350
        hash.AppendData(first);
        hash.AppendData(second);
        return hash.GetHashAndReset();
    }

    /// <summary>The signature of the signed attributes, and the algorithm identifier of the signature (RFC 5754, RFC 4055).</summary>
    private static (byte[] Algorithm, byte[] Signature) Sign(X509Certificate2 certificate, HashAlgorithmName hashAlgorithm, RSASignaturePadding? padding, byte[] data, string parameterName)
    {
        using (var rsa = certificate.GetRSAPrivateKey())
        {
            if (rsa is not null)
            {
                if (padding is null || padding == RSASignaturePadding.Pkcs1)
                {
                    // sha256WithRSAEncryption and the others, with NULL parameters (RFC 4055, 5)
                    var oid = hashAlgorithm.Name switch
                    {
                        "SHA384" => "1.2.840.113549.1.1.12",
                        "SHA512" => "1.2.840.113549.1.1.13",
                        _ => "1.2.840.113549.1.1.11",
                    };
                    return (EncodeAlgorithm(oid, [0x05, 0x00]), rsa.SignData(data, hashAlgorithm, RSASignaturePadding.Pkcs1));
                }

                // RSASSA-PSS: MGF1 with the same hash, and a salt of the length of the hash (RFC 4055, 3.1)
                return (EncodeAlgorithm(RsaPssOid, EncodePssParameters(hashAlgorithm)), rsa.SignData(data, hashAlgorithm, RSASignaturePadding.Pss));
            }
        }

        using (var ecdsa = certificate.GetECDsaPrivateKey())
        {
            if (ecdsa is not null)
            {
                if (padding is not null)
                    throw new ArgumentException("A padding of the RSA signatures is set, and the key of the certificate is not an RSA key", parameterName);

                var oid = hashAlgorithm.Name switch
                {
                    "SHA384" => "1.2.840.10045.4.3.3",
                    "SHA512" => "1.2.840.10045.4.3.4",
                    _ => "1.2.840.10045.4.3.2",
                };
                return (EncodeAlgorithm(oid, parameters: null), ecdsa.SignData(data, hashAlgorithm, DSASignatureFormat.Rfc3279DerSequence));
            }
        }

        throw new ArgumentException("The private key of the certificate is not an RSA or an ECDSA key", parameterName);
    }

    /// <summary>
    /// Checks the signature of the signed attributes with the public key of a certificate: <see langword="null"/> when the
    /// algorithm is not one of those checked here, so that the caller checks it with <see cref="SignedCms"/>.
    /// </summary>
    public static bool? VerifySignedAttributes(CmsSignerData signer, X509Certificate2 certificate)
    {
        if (signer.SignedAttributes is null || GetHashAlgorithm(signer.DigestAlgorithm) is not { } digest)
            return null;

        var hash = signer.SignatureAlgorithm switch
        {
            RsaEncryptionOid or EcPublicKeyOid => digest,
            "1.2.840.113549.1.1.5" or "1.2.840.10045.4.1" => HashAlgorithmName.SHA1,
            "1.2.840.113549.1.1.11" or "1.2.840.10045.4.3.2" => HashAlgorithmName.SHA256,
            "1.2.840.113549.1.1.12" or "1.2.840.10045.4.3.3" => HashAlgorithmName.SHA384,
            "1.2.840.113549.1.1.13" or "1.2.840.10045.4.3.4" => HashAlgorithmName.SHA512,
            RsaPssOid => ReadPssHashAlgorithm(signer.SignatureParameters),
            _ => (HashAlgorithmName?)null,
        };
        if (hash is not { } hashAlgorithm)
            return null;

        // The signature algorithm hashes the attributes with the digest algorithm of the signer (RFC 5652, 5.4)
        if (hashAlgorithm != digest)
            return false;

        try
        {
            switch (signer.SignatureAlgorithm)
            {
                case EcPublicKeyOid or "1.2.840.10045.4.1" or "1.2.840.10045.4.3.2" or "1.2.840.10045.4.3.3" or "1.2.840.10045.4.3.4":
                    using (var ecdsa = certificate.GetECDsaPublicKey())
                    {
                        return ecdsa?.VerifyData(signer.SignedAttributes, signer.Signature, hashAlgorithm, DSASignatureFormat.Rfc3279DerSequence) ?? false;
                    }

                default:
                    using (var rsa = certificate.GetRSAPublicKey())
                    {
                        var padding = signer.SignatureAlgorithm == RsaPssOid ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1;
                        return rsa?.VerifyData(signer.SignedAttributes, signer.Signature, hashAlgorithm, padding) ?? false;
                    }
            }
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the signer infos of signed data (RFC 5652, 5.3): the signed attributes as they were signed (a DER SET OF),
    /// the algorithms and the signature value. The encoding can be BER.
    /// </summary>
    public static List<CmsSignerData> ReadSigners(ReadOnlyMemory<byte> encoded)
    {
        var contentInfo = new AsnReader(encoded, AsnEncodingRules.BER).ReadSequence();
        if (contentInfo.ReadObjectIdentifier() != SignedDataOid)
            throw new CryptographicException("The signature is not CMS signed data");

        var signedData = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
        signedData.ReadInteger();
        signedData.ReadSetOf();
        signedData.ReadSequence();
        if (signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
        {
            signedData.ReadEncodedValue();
        }

        if (signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
        {
            signedData.ReadEncodedValue();
        }

        var result = new List<CmsSignerData>();
        var signerInfos = signedData.ReadSetOf();
        while (signerInfos.HasData)
        {
            var signerInfo = signerInfos.ReadSequence();
            signerInfo.ReadInteger();
            signerInfo.ReadEncodedValue();
            var digestAlgorithm = signerInfo.ReadSequence().ReadObjectIdentifier();
            byte[]? signedAttributes = null;
            if (signerInfo.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                // The signature is computed on the DER encoding of the attributes with the tag of a SET OF (5.4)
                signedAttributes = signerInfo.ReadEncodedValue().ToArray();
                signedAttributes[0] = 0x31;
            }

            var signatureAlgorithm = signerInfo.ReadSequence();
            var algorithm = signatureAlgorithm.ReadObjectIdentifier();
            ReadOnlyMemory<byte>? parameters = signatureAlgorithm.HasData ? signatureAlgorithm.ReadEncodedValue() : null;
            var signature = signerInfo.ReadOctetString();
            result.Add(new CmsSignerData(digestAlgorithm, signedAttributes, algorithm, parameters, signature));
        }

        return result;
    }

    /// <summary>
    /// Checks the ESS signing certificate attribute (RFC 5035, RFC 2634): the digest of the first certificate it identifies
    /// is that of the certificate of the signer, and so are its issuer and serial number when they are given.
    /// <see langword="null"/> when the signature has no such attribute.
    /// </summary>
    public static bool? CheckSigningCertificate(SignerInfo signer, X509Certificate2? certificate)
    {
        bool? result = null;
        foreach (var attribute in signer.SignedAttributes)
        {
            var isVersion2 = attribute.Oid.Value == SigningCertificateV2Oid;
            if (!isVersion2 && attribute.Oid.Value != SigningCertificateOid)
                continue;

            // An attribute has one value (RFC 5035, 5.4)
            if (certificate is null || attribute.Values.Count != 1 || !MatchesSigningCertificate(attribute.Values[0].RawData, certificate, isVersion2))
                return false;

            result = true;
        }

        return result;
    }

    private static bool MatchesSigningCertificate(byte[] value, X509Certificate2 certificate, bool isVersion2)
    {
        try
        {
            var certificates = new AsnReader(value, AsnEncodingRules.BER).ReadSequence().ReadSequence();
            var id = certificates.ReadSequence();

            // ESSCertIDv2: the hash algorithm is SHA-256 by default; ESSCertID: SHA-1
            var algorithm = isVersion2 ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA1;
            if (isVersion2 && id.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
            {
                if (GetHashAlgorithm(id.ReadSequence().ReadObjectIdentifier()) is not { } named)
                    return false;

                algorithm = named;
            }

            var hash = id.ReadOctetString();
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms: the ESSCertID of RFC 2634 is SHA-1
            var expected = algorithm.Name switch
            {
                "SHA1" => SHA1.HashData(certificate.RawData),
                "SHA384" => SHA384.HashData(certificate.RawData),
                "SHA512" => SHA512.HashData(certificate.RawData),
                _ => SHA256.HashData(certificate.RawData),
            };
#pragma warning restore CA5350
            if (!CryptographicOperations.FixedTimeEquals(hash, expected))
                return false;

            if (!id.HasData)
                return true;

            // IssuerSerial: one of the general names is the directory name of the issuer
            var issuerSerial = id.ReadSequence();
            var names = issuerSerial.ReadSequence();
            var issuerMatches = false;
            while (names.HasData)
            {
                var tag = names.PeekTag();
                if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 4)))
                {
                    var name = names.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 4)).ReadEncodedValue();
                    issuerMatches |= name.Span.SequenceEqual(certificate.IssuerName.RawData);
                }
                else
                {
                    names.ReadEncodedValue();
                }
            }

            var serial = issuerSerial.ReadInteger();
            return issuerMatches && serial == new BigInteger(certificate.SerialNumberBytes.Span, isUnsigned: false, isBigEndian: true);
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    private static HashAlgorithmName? ReadPssHashAlgorithm(ReadOnlyMemory<byte>? parameters)
    {
        if (parameters is not { } value)
            return null;

        try
        {
            // RSASSA-PSS-params (RFC 4055, 3.1): the defaults are SHA-1, MGF1 with SHA-1, a salt of 20 bytes, trailer 1
            var reader = new AsnReader(value, AsnEncodingRules.BER).ReadSequence();
            var hash = Sha1Oid;
            var mask = Sha1Oid;
            var saltLength = 20;
            if (reader.HasData && reader.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                hash = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence().ReadObjectIdentifier();
            }

            if (reader.HasData && reader.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
            {
                var generator = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1)).ReadSequence();
                if (generator.ReadObjectIdentifier() != Mgf1Oid)
                    return null;

                mask = generator.ReadSequence().ReadObjectIdentifier();
            }

            if (reader.HasData && reader.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 2)))
            {
                if (!reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 2)).TryReadInt32(out saltLength))
                    return null;
            }

            if (reader.HasData && reader.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 3)))
            {
                if (!reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 3)).TryReadInt32(out var trailer) || trailer != 1)
                    return null;
            }

            // RSASignaturePadding.Pss uses MGF1 with the same hash, and a salt of the length of the hash
            if (mask != hash || GetHashAlgorithm(hash) is not { } algorithm || saltLength != GetHashLength(algorithm))
                return null;

            return algorithm;
        }
        catch (AsnContentException)
        {
            return null;
        }
    }

    private static int GetHashLength(HashAlgorithmName algorithm) => algorithm.Name switch
    {
        "SHA1" => 20,
        "SHA384" => 48,
        "SHA512" => 64,
        _ => 32,
    };

    private static byte[] EncodePssParameters(HashAlgorithmName hashAlgorithm)
    {
        var hash = GetOidValue(hashAlgorithm);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                WriteAlgorithm(writer, hash, parameters: null);
            }

            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1)))
            {
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(Mgf1Oid);
                    WriteAlgorithm(writer, hash, parameters: null);
                }
            }

            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 2)))
            {
                writer.WriteInteger(GetHashLength(hashAlgorithm));
            }
        }

        return writer.Encode();
    }

    private static byte[] EncodeAlgorithm(string oid, byte[]? parameters)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        WriteAlgorithm(writer, oid, parameters);
        return writer.Encode();
    }

    /// <summary>An algorithm identifier; the parameters of the SHA-2 digests are absent (RFC 5754, 2).</summary>
    private static void WriteAlgorithm(AsnWriter writer, string oid, byte[]? parameters)
    {
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(oid);
            if (parameters is not null)
            {
                writer.WriteEncodedValue(parameters);
            }
        }
    }

    private static void WriteAttribute(AsnWriter writer, string oid, byte[] value)
    {
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(oid);
            using (writer.PushSetOf())
            {
                writer.WriteEncodedValue(value);
            }
        }
    }

    private static byte[] EncodeObjectIdentifier(string oid)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteObjectIdentifier(oid);
        return writer.Encode();
    }

    private static byte[] EncodeOctetString(byte[] value)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteOctetString(value);
        return writer.Encode();
    }

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
