using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Email.Fuzz;

/// <summary>
/// The S/MIME reader recognizes any input or not, checks the signatures of any signed entity, and decrypts any encrypted
/// entity, or throws one of the documented exceptions. A verification has a result per signer, valid exactly when the
/// signature is, and the content of a signed or decrypted entity is checked the same way.
/// </summary>
public static partial class EmailFuzzTargets
{
    private static readonly Lazy<X509Certificate2> SmimeCertificate = new(CreateSmimeCertificate);

    private static readonly string[] SmimeTokens =
    [
        "Content-Type: multipart/signed; protocol=\"application/pkcs7-signature\"; micalg=sha-256; boundary=b\r\n", "Content-Type: application/pkcs7-signature\r\n",
        "Content-Type: application/pkcs7-mime; smime-type=signed-data\r\n", "Content-Type: application/pkcs7-mime; smime-type=enveloped-data\r\n", "smime-type=authEnveloped-data",
        "application/x-pkcs7-mime", "application/x-pkcs7-signature", "Content-Transfer-Encoding: base64\r\n", "--b\r\n", "--b--\r\n", "\r\n",
        BinaryToken([0x30, 0x82]), BinaryToken([0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x07, 0x02]), BinaryToken([0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x07, 0x03]),
    ];

    private static IReadOnlyList<FuzzTarget> SmimeTargets { get; } =
    [
        new("smime-read", SmimeRead) { Seeds = GetSmimeSeeds, Dictionary = SmimeTokens },
    ];

    private static void SmimeRead(ReadOnlySpan<byte> data)
    {
        var message = MimeMessage.Load(data.ToArray());
        CheckSmime(message.Body, depth: 0);
    }

    private static void CheckSmime(MimeEntity entity, int depth)
    {
        var format = Smime.GetFormat(entity);
        switch (format)
        {
            case SmimeFormat.ClearSigned or SmimeFormat.OpaqueSigned:
                SmimeSignedContent result;
                try
                {
                    result = Smime.Verify(entity, verifySignatureOnly: true);
                }
                catch (InvalidEmailDataException)
                {
                    return;
                }

                FuzzAssert.Equal(result.Signatures.Count > 0 && result.Signatures.All(signature => signature.Error is null), result.IsValid, "The result of the verification does not match its signatures");
                if (depth < 4)
                {
                    CheckSmime(result.Content, depth + 1);
                }

                break;

            case SmimeFormat.Encrypted:
                MimeEntity decrypted;
                try
                {
                    decrypted = Smime.Decrypt(entity, [SmimeCertificate.Value]);
                }
                catch (Exception ex) when (ex is InvalidEmailDataException or UnsupportedEmailFeatureException or CryptographicException)
                {
                    return;
                }

                if (depth < 4)
                {
                    CheckSmime(decrypted, depth + 1);
                }

                break;

            default:
                try
                {
                    Smime.Verify(entity);
                    FuzzAssert.Fail("An entity that is not signed was verified");
                }
                catch (InvalidOperationException)
                {
                }

                break;
        }
    }

    private static IEnumerable<FuzzSeed> GetSmimeSeeds()
    {
        var certificate = SmimeCertificate.Value;
        var content = new MimePart();
        content.SetText("Hello\r\nworld\r\n");
        var contentBytes = Serialize(content);

        // Clear-signed: the content, then the detached signature
        var detached = new SignedCms(new ContentInfo(contentBytes), detached: true);
        detached.ComputeSignature(new System.Security.Cryptography.Pkcs.CmsSigner(certificate));
        var signature = new MimePart();
        signature.SetContent(detached.Encode());
        signature.ContentType = new MimeContentType("application", "pkcs7-signature");
        var signed = new MimeMultipart("signed");
        var contentType = signed.ContentType;
        contentType.Parameters["protocol"] = "application/pkcs7-signature";
        contentType.Parameters["micalg"] = "sha-256";
        contentType.Boundary = "signed-boundary";
        signed.ContentType = contentType;
        signed.Parts.Add(content);
        signed.Parts.Add(signature);
        yield return new FuzzSeed("clear-signed", CreateMessage(signed));

        // Opaque-signed: the content inside the signature
        var opaque = new SignedCms(new ContentInfo(contentBytes));
        opaque.ComputeSignature(new System.Security.Cryptography.Pkcs.CmsSigner(certificate));
        yield return new FuzzSeed("opaque-signed", CreateMessage(CreatePkcs7Part(opaque.Encode(), "signed-data")));

        // Encrypted for the certificate, then a signed entity encrypted
        var envelope = new EnvelopedCms(new ContentInfo(contentBytes));
        envelope.Encrypt(new CmsRecipient(certificate));
        yield return new FuzzSeed("encrypted", CreateMessage(CreatePkcs7Part(envelope.Encode(), "enveloped-data")));

        var signedEnvelope = new EnvelopedCms(new ContentInfo(Serialize(signed)));
        signedEnvelope.Encrypt(new CmsRecipient(certificate));
        yield return new FuzzSeed("signed-encrypted", CreateMessage(CreatePkcs7Part(signedEnvelope.Encode(), "enveloped-data")));
    }

    private static MimePart CreatePkcs7Part(byte[] content, string smimeType)
    {
        var part = new MimePart();
        part.SetContent(content);
        var contentType = new MimeContentType("application", "pkcs7-mime");
        contentType.Parameters["smime-type"] = smimeType;
        contentType.Name = "smime.p7m";
        part.ContentType = contentType;
        return part;
    }

    private static byte[] CreateMessage(MimeEntity body)
    {
        var message = new MimeMessage { Subject = "S/MIME", Body = body };
        return message.ToByteArray();
    }

    private static byte[] Serialize(MimeEntity entity)
    {
        using var stream = new MemoryStream();
        entity.WriteTo(stream);
        return stream.ToArray();
    }

    private static X509Certificate2 CreateSmimeCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=fuzz@example.com", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx, "fuzz"), "fuzz", X509KeyStorageFlags.EphemeralKeySet);
    }
}
