using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Security;

/// <summary>
/// The public-key security handler (ISO 32000-2, 7.6.5): a 20-byte seed and the permissions, encrypted in a PKCS #7
/// enveloped data for the certificates of the recipients. The file key is the SHA-1 (SHA-256 for AES-256) of the seed and
/// of every enveloped data of the document.
/// </summary>
internal sealed partial class SecurityHandler
{
    private const string PublicKeyFilter = "Adobe.PubSec";
    private const string DefaultCryptFilter = "DefaultCryptFilter";
    private const int SeedLength = 20;

    // Bit 1 is reserved: Acrobat expects it set. Bit 2 allows a recipient to change the encryption (table 24)
    private const int PublicKeyPermissionsReservedBits = PermissionsReservedBits | 1;
    private const int ChangeEncryptionPermission = 1 << 1;

    private static readonly PdfName RecipientsName = PdfName.Create("Recipients");
    private static readonly PdfName EncryptMetadataName = PdfName.Create("EncryptMetadata");

    /// <summary>The algorithms of the key transport of the recipients that decrypt the seed: RSA with PKCS #1 v1.5 or OAEP padding.</summary>
    private static readonly HashSet<string> KeyEncryptionAlgorithms = new(StringComparer.Ordinal) { "1.2.840.113549.1.1.1", "1.2.840.113549.1.1.7" };

    /// <summary>The algorithms of the content of the enveloped data: RC2, DES, triple DES and AES (128, 192, 256 bits) in CBC mode.</summary>
    private static readonly HashSet<string> ContentEncryptionAlgorithms = new(StringComparer.Ordinal)
    {
        "1.2.840.113549.3.2",
        "1.3.14.3.2.7",
        "1.2.840.113549.3.7",
        "2.16.840.1.101.3.4.1.2",
        "2.16.840.1.101.3.4.1.22",
        "2.16.840.1.101.3.4.1.42",
    };

    /// <summary>Reads a public-key encryption dictionary (7.6.5) and decrypts the seed with the private key of the certificate.</summary>
    private static SecurityHandler OpenPublicKey(PdfDictionary dictionary, X509Certificate2? certificate)
    {
        var subFilter = dictionary.GetName(PdfName.Create("SubFilter"))?.Value;
        if (subFilter is not ("adbe.pkcs7.s3" or "adbe.pkcs7.s4" or "adbe.pkcs7.s5"))
            throw new UnsupportedPdfFeatureException($"The public-key security handler with the subfilter '{subFilter}' is not supported: only adbe.pkcs7.s3, adbe.pkcs7.s4 and adbe.pkcs7.s5 are");

        var version = (int)dictionary.GetInteger(PdfName.Create("V"), 0);
        if (version is not (1 or 2 or 4 or 5))
            throw new UnsupportedPdfFeatureException($"The public-key security handler of version {version} is not supported");

        var (cryptFilters, stringMethod, streamMethod, embeddedFileMethod, keyLength) = ReadCryptFilters(dictionary, version);

        // The recipients of adbe.pkcs7.s5 are in the crypt filters (7.6.6, table 27); those of s3 and s4 in the encryption
        // dictionary. The library has one file key: it is the key of the crypt filter of the streams, the strings or the
        // embedded files, the first that has recipients
        PdfDictionary? recipientFilter = null;
        if (version >= 4 && dictionary.GetResolved(PdfName.Create("CF")) is PdfDictionary filters)
        {
            foreach (var key in (ReadOnlySpan<string>)["StmF", "StrF", "EFF"])
            {
                if (dictionary.GetName(PdfName.Create(key)) is { } name && filters.GetResolved(name) is PdfDictionary filter && filter.ContainsKey(RecipientsName))
                {
                    recipientFilter = filter;
                    break;
                }
            }
        }

        var recipients = ReadRecipients(recipientFilter ?? dictionary);
        if (recipients.Count == 0)
            throw new InvalidPdfContentException("The public-key encryption dictionary has no /Recipients");

        var encryptMetadata = version < 4 || (recipientFilter is not null && recipientFilter.ContainsKey(EncryptMetadataName)
            ? recipientFilter.GetBoolean(EncryptMetadataName, defaultValue: true)
            : dictionary.GetBoolean(EncryptMetadataName, defaultValue: true));

        var seed = DecryptSeed(recipients, certificate, out var permissions);
        var isAes256 = stringMethod == CryptMethod.AesV3 || streamMethod == CryptMethod.AesV3 || embeddedFileMethod == CryptMethod.AesV3;
        var fileKey = ComputePublicKeyFileKey(seed, recipients, encryptMetadata, isAes256 ? 32 : keyLength);
        var algorithm = (stringMethod, streamMethod) switch
        {
            _ when isAes256 => PdfEncryptionAlgorithm.AesWith256BitKey,
            (CryptMethod.AesV2, _) or (_, CryptMethod.AesV2) => PdfEncryptionAlgorithm.AesWith128BitKey,
            _ when keyLength == 5 => PdfEncryptionAlgorithm.Rc4With40BitKey,
            _ => PdfEncryptionAlgorithm.Rc4With128BitKey,
        };

        // The permissions are those of the recipient: no recipient is the owner
        return new SecurityHandler(CopyDirect(dictionary), fileKey, algorithm, permissions, encryptMetadata, stringMethod, streamMethod, embeddedFileMethod, cryptFilters, isOwner: false, isPublicKey: true);
    }

    /// <summary>Creates a public-key encryption dictionary, adbe.pkcs7.s5, with an enveloped data per set of permissions.</summary>
    private static SecurityHandler CreatePublicKey(PdfEncryption encryption)
    {
        var (version, method, keyLength, cryptFilterMethod) = encryption.Algorithm switch
        {
            PdfEncryptionAlgorithm.Rc4With40BitKey => (4, CryptMethod.Rc4, 5, "V2"),
            PdfEncryptionAlgorithm.Rc4With128BitKey => (4, CryptMethod.Rc4, 16, "V2"),
            PdfEncryptionAlgorithm.AesWith128BitKey => (4, CryptMethod.AesV2, 16, "AESV2"),
            PdfEncryptionAlgorithm.AesWith256BitKey => (5, CryptMethod.AesV3, 32, "AESV3"),
            _ => throw new ArgumentOutOfRangeException(nameof(encryption), encryption.Algorithm, "The encryption algorithm is not valid."),
        };

        // The recipients that have the same permissions share an enveloped data: its content is the seed, then the
        // permissions, most significant byte first (7.6.5.2)
        var seed = RandomNumberGenerator.GetBytes(SeedLength);
        var recipients = new List<byte[]>();
        foreach (var group in encryption.Recipients.GroupBy(recipient => recipient.Permissions ?? encryption.Permissions))
        {
            var content = new byte[SeedLength + 4];
            seed.CopyTo(content, 0);
            var permissions = group.Key & PdfPermissions.All;
            BinaryPrimitives.WriteInt32BigEndian(content.AsSpan(SeedLength), PublicKeyPermissionsReservedBits | (int)permissions | (permissions == PdfPermissions.All ? ChangeEncryptionPermission : 0));
            var envelope = new EnvelopedCms(new ContentInfo(content), new AlgorithmIdentifier(new Oid("2.16.840.1.101.3.4.1.42")));
            var certificates = new CmsRecipientCollection();
            foreach (var recipient in group)
            {
                certificates.Add(new CmsRecipient(SubjectIdentifierType.IssuerAndSerialNumber, recipient.Certificate));
            }

            envelope.Encrypt(certificates);
            recipients.Add(envelope.Encode());
        }

        var encryptMetadata = encryption.EncryptMetadata;
        var fileKey = ComputePublicKeyFileKey(seed, recipients, encryptMetadata, keyLength);
        var recipientArray = new PdfArray();
        foreach (var recipient in recipients)
        {
            recipientArray.Add(PdfString.FromBytes(recipient, hexadecimal: true));
        }

        // The length of a public-key crypt filter is in bits (table 25)
        var cryptFilter = new PdfDictionary
        {
            [PdfNames.Type] = PdfName.Create("CryptFilter"),
            [PdfName.Create("CFM")] = PdfName.Create(cryptFilterMethod),
            [PdfNames.Length] = PdfInteger.Get(keyLength * 8),
            [RecipientsName] = recipientArray,
        };
        if (!encryptMetadata)
        {
            cryptFilter[EncryptMetadataName] = PdfBoolean.False;
        }

        var dictionary = new PdfDictionary
        {
            [PdfNames.Filter] = PdfName.Create(PublicKeyFilter),
            [PdfName.Create("SubFilter")] = PdfName.Create("adbe.pkcs7.s5"),
            [PdfName.Create("V")] = PdfInteger.Get(version),
            [PdfNames.Length] = PdfInteger.Get(keyLength * 8),
            [PdfName.Create("CF")] = new PdfDictionary { [PdfName.Create(DefaultCryptFilter)] = cryptFilter },
            [PdfName.Create("StmF")] = PdfName.Create(DefaultCryptFilter),
            [PdfName.Create("StrF")] = PdfName.Create(DefaultCryptFilter),
        };

        var cryptFilters = new Dictionary<string, CryptMethod>(StringComparer.Ordinal)
        {
            ["Identity"] = CryptMethod.None,
            [DefaultCryptFilter] = method,
        };
        return new SecurityHandler(dictionary, fileKey, encryption.Algorithm, PublicKeyPermissionsReservedBits | (int)(encryption.Permissions & PdfPermissions.All), encryptMetadata, method, method, method, cryptFilters, isOwner: true, isPublicKey: true);
    }

    /// <summary>The DER-encoded enveloped data of the <c>/Recipients</c> entry: an array of strings, or one string.</summary>
    private static List<byte[]> ReadRecipients(PdfDictionary dictionary)
    {
        var result = new List<byte[]>();
        switch (dictionary.GetResolved(RecipientsName))
        {
            case PdfArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array.TryGetResolved<PdfString>(i, out var recipient))
                    {
                        result.Add(recipient.Span.ToArray());
                    }
                }

                break;

            case PdfString recipient:
                result.Add(recipient.Span.ToArray());
                break;
        }

        return result;
    }

    /// <summary>The seed and the permissions of the first enveloped data that has the certificate as a recipient.</summary>
    private static byte[] DecryptSeed(List<byte[]> recipients, X509Certificate2? certificate, out int permissions)
    {
        if (certificate is null)
            throw new PdfPasswordException("The document is encrypted for certificates: set PdfReadOptions.Certificate to the certificate of a recipient, with its private key");

        if (!certificate.HasPrivateKey)
            throw new PdfPasswordException("The certificate of PdfReadOptions.Certificate has no private key: it cannot decrypt the document");

        foreach (var recipient in recipients)
        {
            var envelope = new EnvelopedCms();
            try
            {
                envelope.Decode(recipient);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidPdfContentException("A recipient of the encryption dictionary is not a PKCS #7 enveloped data", ex);
            }

            foreach (var info in envelope.RecipientInfos)
            {
                if (!IsRecipient(info.RecipientIdentifier, certificate))
                    continue;

                var keyAlgorithm = info.KeyEncryptionAlgorithm.Oid.Value;
                if (info.Type != RecipientInfoType.KeyTransport || keyAlgorithm is null || !KeyEncryptionAlgorithms.Contains(keyAlgorithm))
                    throw new UnsupportedPdfFeatureException($"The key of the document is encrypted for the certificate with the algorithm '{keyAlgorithm}': only RSA key transport (PKCS #1 v1.5 or OAEP) is supported");

                var contentAlgorithm = envelope.ContentEncryptionAlgorithm.Oid.Value;
                if (contentAlgorithm is null || !ContentEncryptionAlgorithms.Contains(contentAlgorithm))
                    throw new UnsupportedPdfFeatureException($"The seed of the document is encrypted with the algorithm '{contentAlgorithm}': only AES, triple DES, DES and RC2 are supported");

                try
                {
                    using var key = certificate.GetRSAPrivateKey() ?? throw new UnsupportedPdfFeatureException("The certificate has no RSA private key: only RSA key transport is supported");
                    envelope.Decrypt(info, key);
                }
                catch (PlatformNotSupportedException ex)
                {
                    throw new UnsupportedPdfFeatureException($"The seed of the document cannot be decrypted on this platform: {ex.Message}", ex);
                }
                catch (CryptographicException ex)
                {
                    throw new PdfPasswordException("The private key of the certificate does not decrypt the key of the document", ex);
                }

                var content = envelope.ContentInfo.Content;
                if (content.Length < SeedLength)
                    throw new InvalidPdfContentException($"The seed of the recipient has {content.Length} bytes instead of {SeedLength}");

                // The permissions follow the seed, most significant byte first; without them, every operation is allowed
                permissions = content.Length >= SeedLength + 4 ? BinaryPrimitives.ReadInt32BigEndian(content.AsSpan(SeedLength)) : PermissionsReservedBits | (int)PdfPermissions.All;
                return content[..SeedLength];
            }
        }

        throw new PdfPasswordException("The certificate is not a recipient of the document");
    }

    private static bool IsRecipient(SubjectIdentifier identifier, X509Certificate2 certificate)
    {
        switch (identifier.Value)
        {
            case X509IssuerSerial issuerSerial:
                // Both names are formatted by X500DistinguishedName from their DER encoding
                return string.Equals(issuerSerial.SerialNumber.TrimStart('0'), certificate.SerialNumber.TrimStart('0'), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(issuerSerial.IssuerName, certificate.IssuerName.Name, StringComparison.Ordinal);

            case string subjectKeyIdentifier when identifier.Type == SubjectIdentifierType.SubjectKeyIdentifier:
                foreach (var extension in certificate.Extensions)
                {
                    if (extension is X509SubjectKeyIdentifierExtension { SubjectKeyIdentifier: { } value })
                        return string.Equals(value, subjectKeyIdentifier, StringComparison.OrdinalIgnoreCase);
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>Algorithm of 7.6.5.3: the hash of the seed, of every enveloped data, and of <c>FFFFFFFF</c> when the metadata is not encrypted.</summary>
    private static byte[] ComputePublicKeyFileKey(byte[] seed, List<byte[]> recipients, bool encryptMetadata, int keyLength)
    {
        // AES-256 uses SHA-256 for its 32-byte key (ISO 32000-2), the other algorithms SHA-1
#pragma warning disable CA5350 // Do not use weak cryptographic algorithms: ISO 32000 defines the key of these algorithms with SHA-1
        using var hash = IncrementalHash.CreateHash(keyLength == 32 ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA1);
#pragma warning restore CA5350
        hash.AppendData(seed);
        foreach (var recipient in recipients)
        {
            hash.AppendData(recipient);
        }

        if (!encryptMetadata)
        {
            hash.AppendData([0xFF, 0xFF, 0xFF, 0xFF]);
        }

        var digest = hash.GetHashAndReset();
        return digest[..Math.Min(keyLength, digest.Length)];
    }
}
