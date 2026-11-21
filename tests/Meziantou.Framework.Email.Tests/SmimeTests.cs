using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MimeKit.Cryptography;

namespace Meziantou.Framework.Email.Tests;

/// <summary>Checks the signatures and decrypts the S/MIME messages that MimeKit, an independent implementation (with BouncyCastle), signs and encrypts.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The MimeKit entities are owned by the message they are added to, which disposes them.")]
public sealed class SmimeTests
{
    private static readonly X509Certificate2 Alice = CreateCertificate("alice@example.com");
    private static readonly X509Certificate2 Bob = CreateCertificate("bob@example.com");

    [Fact]
    public void Usage_Snippet()
    {
        var bytes = ToBytes(Encrypt(Sign(CreateBody("Hello"), Alice), Bob));
        var certificate = Bob;

        #region Smime_Usage
        var message = MimeMessage.Load(bytes);
        Assert.Equal(SmimeFormat.Encrypted, Smime.GetFormat(message.Body));

        // Decrypt with the certificate of the recipient, which has its private key
        var decrypted = Smime.Decrypt(message.Body, [certificate]);
        Assert.Equal(SmimeFormat.ClearSigned, Smime.GetFormat(decrypted));

        // Check the signature: verifySignatureOnly does not check the chain of the certificate
        var signed = Smime.Verify(decrypted, verifySignatureOnly: true);
        Assert.True(signed.IsValid);
        Assert.Equal("alice@example.com", signed.Signatures.Single().Certificate?.GetNameInfo(X509NameType.EmailName, forIssuer: false));
        Assert.Equal("Hello", ((MimePart)signed.Content).GetText().TrimEnd());
        #endregion
    }

    [Fact]
    public void ClearSigned_Valid()
    {
        var message = MimeMessage.Load(ToBytes(Sign(CreateBody("Signed content\r\nwith two lines"), Alice)));
        Assert.Equal(SmimeFormat.ClearSigned, Smime.GetFormat(message.Body));
        var result = Smime.Verify(message.Body, verifySignatureOnly: true);
        Assert.True(result.IsValid);
        var signature = Assert.Single(result.Signatures);
        Assert.Null(signature.Error);
        Assert.Equal(Alice.Thumbprint, signature.Certificate?.Thumbprint);
        Assert.Equal("2.16.840.1.101.3.4.2.1", signature.DigestAlgorithm);
        Assert.NotNull(signature.SigningTime);
        Assert.Equal("Signed content\r\nwith two lines", ((MimePart)result.Content).GetText().TrimEnd());

        // The certificate is self-signed: its chain is not trusted
        var trusted = Smime.Verify(message.Body);
        Assert.False(trusted.IsValid);
        Assert.NotNull(trusted.Signatures.Single().Error);
    }

    [Fact]
    public void ClearSigned_Modified()
    {
        var bytes = ToBytes(Sign(CreateBody("Pay 100 EUR"), Alice));
        var text = Encoding.ASCII.GetString(bytes);
        Assert.Contains("Pay 100 EUR", text, StringComparison.Ordinal);
        var message = MimeMessage.Load(Encoding.ASCII.GetBytes(text.Replace("Pay 100 EUR", "Pay 900 EUR", StringComparison.Ordinal)));
        var result = Smime.Verify(message.Body, verifySignatureOnly: true);
        Assert.False(result.IsValid);
        Assert.NotNull(result.Signatures.Single().Error);
    }

    [Fact]
    public void ClearSigned_LineFeeds()
    {
        // A message stored with LF line breaks (an mbox file on Unix) is checked in its canonical form, with CRLF
        var text = Encoding.ASCII.GetString(ToBytes(Sign(CreateBody("Line 1\r\nLine 2"), Alice))).Replace("\r\n", "\n", StringComparison.Ordinal);
        var message = MimeMessage.Load(Encoding.ASCII.GetBytes(text));
        Assert.True(Smime.Verify(message.Body, verifySignatureOnly: true).IsValid);
    }

    [Fact]
    public void ClearSigned_WhiteSpaceAfterBoundary()
    {
        // [RFC 2046] 5.1.1 allows white space after a boundary delimiter, which the signature covers
        var content = "Content-Type: multipart/mixed; boundary=inner\r\n\r\n--inner \r\nContent-Type: text/plain\r\n\r\nSigned\r\n--inner--\t\r\n"u8.ToArray();
        var cms = new System.Security.Cryptography.Pkcs.SignedCms(new System.Security.Cryptography.Pkcs.ContentInfo(content), detached: true);
        cms.ComputeSignature(new System.Security.Cryptography.Pkcs.CmsSigner(Alice));
        var signature = Convert.ToBase64String(cms.Encode());
        var text = "From: alice@example.com\r\nMIME-Version: 1.0\r\nContent-Type: multipart/signed; protocol=\"application/pkcs7-signature\"; micalg=sha-256; boundary=outer\r\n\r\n" +
            "--outer \r\n" + Encoding.ASCII.GetString(content) + "\r\n--outer\r\nContent-Type: application/pkcs7-signature; name=smime.p7s\r\nContent-Transfer-Encoding: base64\r\n\r\n" + signature + "\r\n--outer--\r\n";
        var message = MimeMessage.Load(Encoding.ASCII.GetBytes(text));
        var result = Smime.Verify(message.Body, verifySignatureOnly: true);
        Assert.True(result.IsValid);
        Assert.Equal("Signed", ((MimePart)((MimeMultipart)result.Content).Parts.Single()).GetText());
    }

    [Fact]
    public void OpaqueSigned_Valid()
    {
        using var signingContext = new TemporarySecureMimeContext();
        var entity = ApplicationPkcs7Mime.Sign(signingContext, new CmsSigner(Alice), CreateBody("Opaque"));
        var message = MimeMessage.Load(ToBytes(entity));
        Assert.Equal(SmimeFormat.OpaqueSigned, Smime.GetFormat(message.Body));
        var result = Smime.Verify(message.Body, verifySignatureOnly: true);
        Assert.True(result.IsValid);
        Assert.Equal("Opaque", ((MimePart)result.Content).GetText().TrimEnd());
    }

    [Fact]
    public void Encrypted_WrongCertificate()
    {
        var message = MimeMessage.Load(ToBytes(Encrypt(CreateBody("Secret"), Bob)));
        Assert.Throws<CryptographicException>(() => Smime.Decrypt(message.Body, [Alice]));
        Assert.Equal("Secret", ((MimePart)Smime.Decrypt(message.Body, [Bob])).GetText().TrimEnd());
    }

    [Fact]
    public void Format_WithoutSmimeType()
    {
        // RFC 8551 3.2.2: without smime-type, the type of the CMS content tells the structure
        var message = MimeMessage.Load(ToBytes(Encrypt(CreateBody("Secret"), Bob)));
        var contentType = message.Body.ContentType;
        contentType.Parameters.Remove("smime-type");
        message.Body.ContentType = contentType;
        Assert.Equal(SmimeFormat.Encrypted, Smime.GetFormat(message.Body));
    }

    [Fact]
    public void WrittenByTheLibrary()
    {
        // A clear-signed message written by the MIME writer, signed with the CMS of .NET: MimeKit checks its signature too
        var content = new MimePart();
        content.SetText("Hello\r\nworld\r\n");
        using var contentStream = new MemoryStream();
        content.WriteTo(contentStream);
        var cms = new System.Security.Cryptography.Pkcs.SignedCms(new System.Security.Cryptography.Pkcs.ContentInfo(contentStream.ToArray()), detached: true);
        cms.ComputeSignature(new System.Security.Cryptography.Pkcs.CmsSigner(Alice));
        var signature = new MimePart();
        signature.SetContent(cms.Encode());
        signature.ContentType = new MimeContentType("application", "pkcs7-signature");
        var signed = new MimeMultipart("signed");
        var contentType = signed.ContentType;
        contentType.Parameters["protocol"] = "application/pkcs7-signature";
        contentType.Parameters["micalg"] = "sha-256";
        signed.ContentType = contentType;
        signed.Parts.Add(content);
        signed.Parts.Add(signature);
        var bytes = new MimeMessage { Subject = "Signed", Body = signed }.ToByteArray();

        Assert.True(Smime.Verify(MimeMessage.Load(bytes).Body, verifySignatureOnly: true).IsValid);
        using var context = new TemporarySecureMimeContext();
        context.Import(Alice);
        var oracle = Assert.IsType<MultipartSigned>(MimeKit.MimeMessage.Load(new MemoryStream(bytes)).Body);
        Assert.True(oracle.Verify(context).Single().Verify(verifySignatureOnly: true));
    }

    [Fact]
    public void NotSmime()
    {
        var message = MimeMessage.Load("From: a@example.com\r\nContent-Type: multipart/signed; protocol=\"application/pgp-signature\"; boundary=b\r\n\r\n--b\r\n\r\nx\r\n--b\r\nContent-Type: application/pgp-signature\r\n\r\ny\r\n--b--\r\n"u8.ToArray());
        Assert.Equal(SmimeFormat.None, Smime.GetFormat(message.Body));
        Assert.Throws<InvalidOperationException>(() => Smime.Verify(message.Body));
        Assert.Throws<InvalidOperationException>(() => Smime.Decrypt(message.Body, [Bob]));

        var malformed = MimeMessage.Load("Content-Type: application/pkcs7-mime; smime-type=signed-data\r\nContent-Transfer-Encoding: base64\r\n\r\nAAAA\r\n"u8.ToArray());
        Assert.Equal(SmimeFormat.OpaqueSigned, Smime.GetFormat(malformed.Body));
        Assert.Throws<InvalidEmailDataException>(() => Smime.Verify(malformed.Body));
    }

    private static MimeKit.TextPart CreateBody(string text) => new MimeKit.TextPart("plain") { Text = text };

    private static MultipartSigned Sign(MimeKit.MimeEntity body, X509Certificate2 certificate)
    {
        using var context = new TemporarySecureMimeContext();
        return MultipartSigned.Create(context, new CmsSigner(certificate), body);
    }

    private static ApplicationPkcs7Mime Encrypt(MimeKit.MimeEntity body, X509Certificate2 certificate)
    {
        using var context = new TemporarySecureMimeContext();
        return ApplicationPkcs7Mime.Encrypt(context, new CmsRecipientCollection { new CmsRecipient(certificate) }, body);
    }

    private static byte[] ToBytes(MimeKit.MimeEntity body)
    {
        using var message = new MimeKit.MimeMessage();
        message.From.Add(new MimeKit.MailboxAddress("Alice", "alice@example.com"));
        message.To.Add(new MimeKit.MailboxAddress("Bob", "bob@example.com"));
        message.Subject = "S/MIME";
        message.Body = body;
        using var stream = new MemoryStream();

        // MimeKit writes the line breaks of the platform by default: LF on Unix
        var options = MimeKit.FormatOptions.Default.Clone();
        options.NewLineFormat = MimeKit.NewLineFormat.Dos;
        message.WriteTo(options, stream);
        return stream.ToArray();
    }

    private static X509Certificate2 CreateCertificate(string email)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + email, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.DigitalSignature | System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.KeyEncipherment, critical: false));
        var alternativeNames = new SubjectAlternativeNameBuilder();
        alternativeNames.AddEmailAddress(email);
        request.CertificateExtensions.Add(alternativeNames.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        // BouncyCastle, which MimeKit uses, needs a key it can export
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx, "test"), "test", X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
    }
}
