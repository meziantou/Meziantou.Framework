using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Signing;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfSignerTests
{
    [Fact]
    public void Example_Sign()
    {
        using var certificate = CreateCertificate("rsa");

        #region PdfSigning_Sign
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder()
            .DrawText("Contract", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);

        // The certificate has its private key: X509CertificateLoader.LoadPkcs12FromFile loads one from a .pfx file
        var signed = PdfSigner.Sign(document, new PdfSignatureOptions(certificate) { Reason = "I approve this contract" });

        var signature = PdfSignatureVerifier.Verify(signed).Single();
        var isValid = signature.IsValid && signature.CoversWholeDocument;
        #endregion

        Assert.True(isValid);
        Assert.Equal("I approve this contract", signature.Reason);
        Assert.Equal("Signature1", signature.FieldName);
        Assert.Equal("ETSI.CAdES.detached", signature.SubFilter);
        Assert.Equal(certificate.Thumbprint, signature.Certificate!.Thumbprint);
    }

    [Fact]
    public async Task Example_Timestamp()
    {
        using var certificate = CreateCertificate("rsa");
        using var authority = new TestTimestampAuthority(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        using var httpClient = new HttpClient(authority);
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4);

        #region PdfSigning_Timestamp
        // PAdES B-T: the signature is stamped by an RFC 3161 time stamp authority
        var options = new PdfSignatureOptions(certificate)
        {
            TimestampAuthority = new Uri("http://timestamp.example.com/"),
            HttpClient = httpClient,
        };
        var signed = await PdfSigner.SignAsync(document, options, XunitCancellationToken);
        #endregion

        var signature = PdfSignatureVerifier.Verify(signed).Single();
        Assert.True(signature.IsValid);
        Assert.True(signature.IsTimestampValid);
        Assert.Equal(authority.Time, signature.TimestampTime);
        Assert.Equal(1, authority.RequestCount);
        Assert.Throws<ArgumentException>(() => PdfSigner.Sign(document, options));
    }

    [Theory]
    [InlineData("rsa", "SHA256")]
    [InlineData("rsa", "SHA384")]
    [InlineData("rsa", "SHA512")]
    [InlineData("ecdsa", "SHA256")]
    public void SignaturesAreCadesDetached(string key, string hashAlgorithm)
    {
        using var certificate = CreateCertificate(key);
        var signingTime = new DateTimeOffset(2026, 4, 5, 6, 7, 8, TimeSpan.FromHours(2));
        var signed = PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate)
        {
            HashAlgorithm = new HashAlgorithmName(hashAlgorithm),
            SigningTime = signingTime,
            SignerName = "Signer",
            Location = "Montreal",
            ContactInfo = "signer@example.com",
            FieldName = "Approval",
        });

        var signature = PdfSignatureVerifier.Verify(signed).Single();
        Assert.True(signature.IsValid, signature.Error);
        Assert.Equal("Approval", signature.FieldName);
        Assert.Equal(signingTime, signature.SigningTime);
        Assert.Equal("Signer", signature.SignerName);
        Assert.Equal("Montreal", signature.Location);
        Assert.Equal("signer@example.com", signature.ContactInfo);
        Assert.Null(signature.IsTimestampValid);
        Assert.True(signature.CoversWholeDocument);

        // PAdES: the ESS signing certificate is signed, the signing time is the /M of the dictionary only
        var contents = GetSignatureDictionary(signed).Get<PdfString>(PdfNames.Contents)!.Span;
        var cms = new SignedCms();
        cms.Decode(contents[..GetEncodedLength(contents)]);
        var attributes = cms.SignerInfos[0].SignedAttributes.OfType<CryptographicAttributeObject>().Select(attribute => attribute.Oid.Value).ToList();
        Assert.Contains("1.2.840.113549.1.9.16.2.47", attributes);
        Assert.DoesNotContain("1.2.840.113549.1.9.5", attributes);
        Assert.Equal(hashAlgorithm switch { "SHA384" => "2.16.840.1.101.3.4.2.2", "SHA512" => "2.16.840.1.101.3.4.2.3", _ => "2.16.840.1.101.3.4.2.1" }, cms.SignerInfos[0].DigestAlgorithm.Value);
    }

    [Fact]
    public void ASecondSignatureKeepsTheFirstValid()
    {
        using var certificate = CreateCertificate("rsa");
        var first = PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate) { Reason = "Author" });
        var document = PdfDocument.Open(first);
        document.Information.Title = "Reviewed";

        var second = PdfSigner.Sign(document, new PdfSignatureOptions(certificate) { Reason = "Reviewer" });

        Assert.Equal(first, second.AsSpan(0, first.Length).ToArray());
        var signatures = PdfSignatureVerifier.Verify(second);
        Assert.Equal(["Signature1", "Signature2"], signatures.Select(signature => signature.FieldName));
        Assert.All(signatures, signature => Assert.True(signature.IsValid, signature.Error));
        Assert.False(signatures[0].CoversWholeDocument);
        Assert.True(signatures[1].CoversWholeDocument);
        Assert.Equal("Reviewed", PdfDocument.Open(second).Information.Title);
    }

    [Fact]
    public void ChangesAreDetected()
    {
        using var certificate = CreateCertificate("rsa");
        var signed = PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate));

        // A byte of the signed content
        var modified = (byte[])signed.Clone();
        var text = Encoding.Latin1.GetString(modified);
        modified[text.IndexOf("%%EOF", StringComparison.Ordinal) + 1] = (byte)'X';
        var signature = PdfSignatureVerifier.Verify(modified).Single();
        Assert.False(signature.IsDocumentIntact);
        Assert.False(signature.IsValid);

        // Bytes appended after the signature: they are not signed
        var appended = signed.Concat("\n% comment\n"u8.ToArray()).ToArray();
        signature = PdfSignatureVerifier.Verify(appended).Single();
        Assert.True(signature.IsValid);
        Assert.False(signature.CoversWholeDocument);
    }

    [Fact]
    public void EncryptedDocumentsAreSigned()
    {
        using var certificate = CreateCertificate("rsa");
        var document = CreateDocument();
        document.Encryption = new PdfEncryption("user", "owner") { Algorithm = PdfEncryptionAlgorithm.AesWith128BitKey };
        var encrypted = PdfDocument.Open(document.ToByteArray(), new PdfReadOptions { Password = "user" });

        var signed = PdfSigner.Sign(encrypted, new PdfSignatureOptions(certificate) { Reason = "Encrypted" });

        var signature = PdfSignatureVerifier.Verify(signed, new PdfReadOptions { Password = "user" }).Single();
        Assert.True(signature.IsValid, signature.Error);
        Assert.Equal("Encrypted", signature.Reason);
        Assert.Throws<PdfPasswordException>(() => PdfSignatureVerifier.Verify(signed));
    }

    [Fact]
    public void TheCertificateAndTheReservedSpaceAreChecked()
    {
        using var certificate = CreateCertificate("rsa");
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);

        Assert.Throws<ArgumentException>(() => PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(publicOnly)));
        Assert.Throws<InvalidOperationException>(() => PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate) { ReservedSize = 16 }));
        Assert.Throws<InvalidOperationException>(() => PdfSigner.Sign(new PdfDocument(), new PdfSignatureOptions(certificate)));
    }

    [Fact]
    public void InvalidSignaturesAreReported()
    {
        using var certificate = CreateCertificate("rsa");
        var signed = Encoding.Latin1.GetString(PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate)));

        // The format is renamed in place: the offsets of the file do not change
        var unknownFormat = Encoding.Latin1.GetBytes(signed.Replace("/ETSI.CAdES.detached", "/ETSI.CAdES.unknown1", StringComparison.Ordinal));
        Assert.Contains("is not supported", PdfSignatureVerifier.Verify(unknownFormat).Single().Error, StringComparison.Ordinal);

        // The byte ranges must be in the file, in order
        var shifted = Encoding.Latin1.GetBytes(signed.Replace("/ByteRange [0 ", "/ByteRange [9 ", StringComparison.Ordinal));
        Assert.False(PdfSignatureVerifier.Verify(shifted).Single().IsValid);
        var outOfFile = Encoding.Latin1.GetBytes(signed.Replace("/ByteRange [0 ", "/ByteRange [0 9", StringComparison.Ordinal).Remove(signed.IndexOf("/ByteRange [0 ", StringComparison.Ordinal) + 30, 1));
        Assert.NotNull(PdfSignatureVerifier.Verify(outOfFile).Single().Error);
        Assert.Empty(PdfSignatureVerifier.Verify(CreateDocument().ToByteArray()));

        // The bytes left out must be the signature itself: a gap that also leaves out other bytes could hide them
        var ranges = PdfSignatureVerifier.Verify(Encoding.Latin1.GetBytes(signed)).Single().ByteRanges;
        var original = $"[0 {ranges[0].Length} {ranges[1].Offset} {ranges[1].Length}";
        var wider = $"[0 {ranges[0].Length - 2} {ranges[1].Offset} {ranges[1].Length}".PadRight(original.Length);
        var wrapped = Encoding.Latin1.GetBytes(signed.Replace("/ByteRange " + original, "/ByteRange " + wider, StringComparison.Ordinal));
        Assert.Contains("are not the /Contents", PdfSignatureVerifier.Verify(wrapped).Single().Error, StringComparison.Ordinal);
    }

    private static PdfDocument CreateDocument()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Signed", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);
        return document;
    }

    private static X509Certificate2 CreateCertificate(string key)
    {
        var now = DateTimeOffset.UtcNow;
        if (key == "ecdsa")
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return new CertificateRequest("CN=ECDSA signer", ecdsa, HashAlgorithmName.SHA256).CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=RSA signer, O=Meziantou", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
        return request.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
    }

    private static PdfDictionary GetSignatureDictionary(byte[] file)
    {
        var document = PdfDocument.Open(file);
        var field = (PdfDictionary)document.Catalog.Get<PdfDictionary>(PdfNames.AcroForm)!.Get<PdfArray>(PdfName.Create("Fields"))!.GetResolved(0)!;
        return field.Get<PdfDictionary>(PdfName.Create("V"))!;
    }

    private static int GetEncodedLength(ReadOnlySpan<byte> data)
    {
        AsnDecoder.ReadEncodedValue(data, AsnEncodingRules.BER, out _, out _, out var length);
        return length;
    }

    [Fact]
    public void Example_IsSignedByTrustedSigner()
    {
        using var certificate = CreateCertificate("ecdsa");
        using var other = CreateCertificate("ecdsa");
        var signed = PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate));

        Assert.True(IsSignedByTrustedSigner(signed, certificate));
        Assert.False(IsSignedByTrustedSigner(signed, other));

        // A change appended after the signature: the signature no longer covers the whole file
        var document = PdfDocument.Open(signed);
        document.Information.Title = "Changed";
        using var changed = new MemoryStream();
        document.SaveIncremental(changed);
        Assert.False(IsSignedByTrustedSigner(changed.ToArray(), certificate));
    }

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region PdfSigning_Verify
    public static bool IsSignedByTrustedSigner(byte[] file, X509Certificate2 trustedRoot)
    {
        foreach (var signature in PdfSignatureVerifier.Verify(file))
        {
            // Intact and cryptographically valid, and nothing was appended to the file after it
            if (!signature.IsValid || !signature.CoversWholeDocument || signature.Certificate is null)
                continue;

            // The library does not decide whether the signer is trusted: the application builds the chain
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(trustedRoot);
            chain.ChainPolicy.ExtraStore.AddRange(signature.Certificates);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // a private root without revocation lists
            if (chain.Build(signature.Certificate))
                return true;
        }

        return false;
    }
    #endregion
#pragma warning restore xUnit1013
}
