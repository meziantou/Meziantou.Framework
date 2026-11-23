using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email;

/// <summary>
/// Reads the signed and encrypted MIME entities of S/MIME ([RFC 8551]): checks the signatures of clear-signed
/// (<c>multipart/signed</c>) and opaque-signed entities, and decrypts the encrypted ones with the certificate of a
/// recipient. To read a signed or encrypted message, pass its <see cref="MimeMessage.Body"/>.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Email.Tests/SmimeTests.cs" ref:region="Smime_Usage" ref:dedent="true">
/// var message = MimeMessage.Load(bytes);
/// Assert.Equal(SmimeFormat.Encrypted, Smime.GetFormat(message.Body));
///
/// // Decrypt with the certificate of the recipient, which has its private key
/// var decrypted = Smime.Decrypt(message.Body, [certificate]);
/// Assert.Equal(SmimeFormat.ClearSigned, Smime.GetFormat(decrypted));
///
/// // Check the signature: verifySignatureOnly does not check the chain of the certificate
/// var signed = Smime.Verify(decrypted, verifySignatureOnly: true);
/// Assert.True(signed.IsValid);
/// Assert.Equal("alice@example.com", signed.Signatures.Single().Certificate?.GetNameInfo(X509NameType.EmailName, forIssuer: false));
/// Assert.Equal("Hello", ((MimePart)signed.Content).GetText().TrimEnd());
/// </code>
/// </example>
[UnsupportedOSPlatform("browser")]
public static class Smime
{
    private const string SignedDataOid = "1.2.840.113549.1.7.2";
    private const string EnvelopedDataOid = "1.2.840.113549.1.7.3";
    private const string AuthEnvelopedDataOid = "1.2.840.113549.1.9.16.1.23";

    /// <summary>Returns the S/MIME structure of an entity, from its content type, and from the content of its signature or encryption when the content type does not say it.</summary>
    /// <param name="entity">The entity, such as the body of a message.</param>
    /// <returns>The structure, or <see cref="SmimeFormat.None"/>.</returns>
    public static SmimeFormat GetFormat(MimeEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var contentType = entity.ContentType;
        if (entity is MimeMultipart && contentType.IsMimeType("multipart", "signed"))
        {
            var protocol = contentType.Parameters.TryGetValue("protocol", out var value) ? value.Trim() : "";
            return protocol.Equals("application/pkcs7-signature", StringComparison.OrdinalIgnoreCase) || protocol.Equals("application/x-pkcs7-signature", StringComparison.OrdinalIgnoreCase)
                ? SmimeFormat.ClearSigned
                : SmimeFormat.None;
        }

        if (entity is not MimePart part || !IsPkcs7Mime(contentType))
        {
            return SmimeFormat.None;
        }

        var type = contentType.Parameters.TryGetValue("smime-type", out var smimeType) ? smimeType.Trim() : "";
        if (type.Equals("signed-data", StringComparison.OrdinalIgnoreCase))
        {
            return SmimeFormat.OpaqueSigned;
        }

        if (type.Equals("enveloped-data", StringComparison.OrdinalIgnoreCase) || type.Equals("authEnveloped-data", StringComparison.OrdinalIgnoreCase))
        {
            return SmimeFormat.Encrypted;
        }

        // RFC 8551 3.2.2: a sender can omit smime-type; the type of the CMS content tells it
        try
        {
            return ContentInfo.GetContentType(part.GetContent()).Value switch
            {
                SignedDataOid => SmimeFormat.OpaqueSigned,
                EnvelopedDataOid or AuthEnvelopedDataOid => SmimeFormat.Encrypted,
                _ => SmimeFormat.None,
            };
        }
        catch (CryptographicException)
        {
            return SmimeFormat.None;
        }
    }

    /// <summary>Checks the signatures of a signed entity, and returns its content.</summary>
    /// <param name="entity">A clear-signed or opaque-signed entity (see <see cref="GetFormat"/>).</param>
    /// <param name="verifySignatureOnly"><see langword="false"/> to also check the certificates of the signers: their chain of trust, their validity and their usage; <see langword="true"/> to check only that the content was signed by the keys of the certificates.</param>
    /// <param name="extraStore">Certificates used to find the certificates of the signers and to build their chains, or <see langword="null"/>.</param>
    /// <param name="options">The options to read the content of an opaque signature, or <see langword="null"/> for the default options.</param>
    /// <returns>The content and the result of the verification of each signature.</returns>
    /// <exception cref="InvalidOperationException">The entity is not signed.</exception>
    /// <exception cref="InvalidEmailDataException">The signature is malformed.</exception>
    public static SmimeSignedContent Verify(MimeEntity entity, bool verifySignatureOnly = false, X509Certificate2Collection? extraStore = null, MimeReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        options ??= MimeReadOptions.Default;
        extraStore ??= [];
        switch (GetFormat(entity))
        {
            case SmimeFormat.ClearSigned:
                return VerifyDetached((MimeMultipart)entity, verifySignatureOnly, extraStore);

            case SmimeFormat.OpaqueSigned:
                var cms = new SignedCms();
                Decode(() => cms.Decode(((MimePart)entity).GetContent()));
                return new SmimeSignedContent(MimeParser.ParseEntity(cms.ContentInfo.Content, options), CheckSignatures(cms, verifySignatureOnly, extraStore));

            default:
                throw new InvalidOperationException("The entity is not signed with S/MIME: it is not a multipart/signed or an application/pkcs7-mime of signed data.");
        }
    }

    /// <summary>Decrypts an encrypted entity with the certificate of one of its recipients, and returns the entity it contains, which can be signed too.</summary>
    /// <param name="entity">An encrypted entity (see <see cref="GetFormat"/>).</param>
    /// <param name="certificates">Certificates with their private key; the certificates of the store of the current user are used too.</param>
    /// <param name="options">The options to read the decrypted entity, or <see langword="null"/> for the default options.</param>
    /// <returns>The decrypted entity.</returns>
    /// <exception cref="InvalidOperationException">The entity is not encrypted.</exception>
    /// <exception cref="InvalidEmailDataException">The encrypted content is malformed.</exception>
    /// <exception cref="UnsupportedEmailFeatureException">The content uses authenticated encryption (<c>authEnveloped-data</c>), which the library does not support.</exception>
    /// <exception cref="CryptographicException">No certificate can decrypt the content: none of them is the certificate of a recipient with its private key.</exception>
    public static MimeEntity Decrypt(MimeEntity entity, X509Certificate2Collection certificates, MimeReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(certificates);
        if (GetFormat(entity) != SmimeFormat.Encrypted)
        {
            throw new InvalidOperationException("The entity is not encrypted with S/MIME: it is not an application/pkcs7-mime of enveloped data.");
        }

        var content = ((MimePart)entity).GetContent();
        if (TryGetContentType(content) == AuthEnvelopedDataOid)
        {
            throw new UnsupportedEmailFeatureException("The content uses authenticated encryption (authEnveloped-data, RFC 5083), which is not supported.");
        }

        var cms = new EnvelopedCms();
        Decode(() => cms.Decode(content));
        cms.Decrypt(certificates);
        return MimeParser.ParseEntity(cms.ContentInfo.Content, options ?? MimeReadOptions.Default);
    }

    private static bool IsPkcs7Mime(MimeContentType contentType) => contentType.IsMimeType("application", "pkcs7-mime") || contentType.IsMimeType("application", "x-pkcs7-mime");

    private static string? TryGetContentType(byte[] content)
    {
        try
        {
            return ContentInfo.GetContentType(content).Value;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static SmimeSignedContent VerifyDetached(MimeMultipart multipart, bool verifySignatureOnly, X509Certificate2Collection extraStore)
    {
        // RFC 1847 2.1: two parts, the content then the signature
        if (multipart.Parts is not [var content, MimePart signature, ..])
        {
            throw new InvalidEmailDataException("The multipart/signed entity does not have a content and a signature.");
        }

        var signatureBytes = signature.GetContent();
        // The bytes the content was read from: writing it back can remove white space the signature covers, such as the
        // white space after a boundary delimiter
        var signed = content.RawData is { } raw ? raw.ToArray() : Serialize(content);
        var signatures = VerifyDetached(signed, signatureBytes, verifySignatureOnly, extraStore);
        if (!signatures.All(signature => signature.IsValid))
        {
            // RFC 8551 3.1.1: the content is signed in its canonical form, with CRLF line breaks; a message stored with LF
            // line breaks is checked in that form too
            var canonical = Canonicalize(signed);
            if (canonical.Length != signed.Length)
            {
                var canonicalSignatures = VerifyDetached(canonical, signatureBytes, verifySignatureOnly, extraStore);
                if (canonicalSignatures.Count(signature => signature.IsValid) > signatures.Count(signature => signature.IsValid))
                {
                    signatures = canonicalSignatures;
                }
            }
        }

        return new SmimeSignedContent(content, signatures);
    }

    private static List<SmimeSignature> VerifyDetached(byte[] content, byte[] signature, bool verifySignatureOnly, X509Certificate2Collection extraStore)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: true);
        Decode(() => cms.Decode(signature));
        return CheckSignatures(cms, verifySignatureOnly, extraStore);
    }

    private static List<SmimeSignature> CheckSignatures(SignedCms cms, bool verifySignatureOnly, X509Certificate2Collection extraStore)
    {
        var result = new List<SmimeSignature>();
        foreach (var signer in cms.SignerInfos)
        {
            string? error = null;
            try
            {
                signer.CheckSignature(extraStore, verifySignatureOnly);
            }
            catch (CryptographicException ex)
            {
                error = ex.Message;
            }

            // The details of a malformed signature are not known
            var certificate = TryGet(() => signer.Certificate) ?? TryGet(() => FindCertificate(signer, extraStore));
            var signingTime = TryGet(() => GetSigningTime(signer));
            result.Add(new SmimeSignature(certificate, signingTime, TryGet(() => signer.DigestAlgorithm.Value), error));
        }

        return result;
    }

    private static DateTimeOffset? GetSigningTime(SignerInfo signer)
    {
        foreach (var attribute in signer.SignedAttributes)
        {
            if (attribute.Values is [Pkcs9SigningTime time, ..])
            {
                return new DateTimeOffset(DateTime.SpecifyKind(time.SigningTime, DateTimeKind.Utc));
            }
        }

        return null;
    }

    private static T? TryGet<T>(Func<T?> getValue)
    {
        try
        {
            return getValue();
        }
        catch (CryptographicException)
        {
            return default;
        }
    }

    private static X509Certificate2? FindCertificate(SignerInfo signer, X509Certificate2Collection extraStore)
    {
        foreach (var certificate in extraStore)
        {
            if (signer.SignerIdentifier.MatchesCertificate(certificate))
            {
                return certificate;
            }
        }

        return null;
    }

    private static void Decode(Action decode)
    {
        try
        {
            decode();
        }
        catch (CryptographicException ex)
        {
            throw new InvalidEmailDataException("The S/MIME content is not valid CMS data.", ex);
        }
    }

    /// <summary>Returns the bytes of an entity as it is written, for an entity that was not read from a message.</summary>
    private static byte[] Serialize(MimeEntity entity)
    {
        using var stream = new MemoryStream();
        entity.WriteTo(stream);
        return stream.ToArray();
    }

    private static byte[] Canonicalize(byte[] data)
    {
        var result = new List<byte>(data.Length + (data.Length / 32));
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] == '\n' && (i == 0 || data[i - 1] != '\r'))
            {
                result.Add((byte)'\r');
            }

            result.Add(data[i]);
        }

        return [.. result];
    }
}
