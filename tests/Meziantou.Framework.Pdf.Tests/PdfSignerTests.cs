using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering;
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
    public void CertificateEncryptedDocumentsAreSigned()
    {
        using var certificate = CreateCertificate("rsa");
        var document = CreateDocument();
        document.Encryption = new PdfEncryption(new PdfEncryptionRecipient(certificate)) { Algorithm = PdfEncryptionAlgorithm.AesWith128BitKey };

        // The new encryption is reopened with the certificate of the recipient, which has its private key
        var signed = PdfSigner.Sign(document, new PdfSignatureOptions(certificate) { Reason = "Encrypted" });

        var signature = PdfSignatureVerifier.Verify(signed, new PdfReadOptions { Certificate = certificate }).Single();
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

    [Fact]
    public void SignatureMetadataIsReadFromTheSignedRevision()
    {
        using var certificate = CreateCertificate("rsa");
        var signed = PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate) { SignerName = "Alice", Reason = "Approved" });

        // The signature dictionary is changed by an update: the signed bytes are intact
        var document = PdfDocument.Open(signed);
        var dictionary = GetSignatureDictionary(document);
        dictionary[PdfName.Create("Name")] = PdfString.FromText("Mallory");
        dictionary[PdfName.Create("Reason")] = PdfString.FromText("Rejected");
        var changed = SaveIncremental(document);

        var signature = PdfSignatureVerifier.Verify(changed).Single();
        Assert.True(signature.IsValid, signature.Error);
        Assert.False(signature.CoversWholeDocument);
        Assert.Equal("Alice", signature.SignerName);
        Assert.Equal("Approved", signature.Reason);
        var modification = signature.Modifications.Single();
        Assert.Equal(PdfModificationKind.Signature, modification.Kind);
        Assert.Equal("Signature1", modification.FieldName);
        Assert.False(modification.IsNewObject);

        // A byte range changed after the signature is reported
        dictionary[PdfName.Create("ByteRange")] = PdfArray.FromNumbers(0, 10, 20, 30);
        Assert.NotNull(PdfSignatureVerifier.Verify(SaveIncremental(document)).Single().Error);
    }

    [Fact]
    public void FieldNamesAreUnique()
    {
        using var certificate = CreateCertificate("rsa");

        // A text field named Approval, a field without name whose kid is Signature1, and an unsigned signature field
        var document = CreateDocument();
        var text = document.Objects.Add(new PdfDictionary { [PdfName.Create("FT")] = PdfName.Create("Tx"), [PdfName.Create("T")] = PdfString.FromText("Approval") });
        var kid = document.Objects.Add(new PdfDictionary { [PdfName.Create("FT")] = PdfName.Create("Tx"), [PdfName.Create("T")] = PdfString.FromText("Signature1") });
        var parent = document.Objects.Add(new PdfDictionary { [PdfNames.Kids] = new PdfArray(kid) });
        var empty = document.Objects.Add(new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Annot,
            [PdfNames.Subtype] = PdfName.Create("Widget"),
            [PdfName.Create("FT")] = PdfName.Create("Sig"),
            [PdfName.Create("T")] = PdfString.FromText("Empty"),
            [PdfNames.Rect] = PdfArray.FromNumbers(0, 0, 0, 0),
            [PdfNames.P] = document.Pages[0].Reference,
        });
        document.Pages[0].Dictionary[PdfNames.Annots] = new PdfArray(empty);
        document.Catalog[PdfNames.AcroForm] = new PdfDictionary { [PdfName.Create("Fields")] = new PdfArray(text, parent, empty) };
        var file = document.ToByteArray();

        Assert.Throws<ArgumentException>(() => PdfSigner.Sign(PdfDocument.Open(file), new PdfSignatureOptions(certificate) { FieldName = "Approval" }));
        Assert.Throws<ArgumentException>(() => PdfSigner.Sign(PdfDocument.Open(file), new PdfSignatureOptions(certificate) { FieldName = "Signature1" }));
        Assert.Throws<ArgumentException>(() => PdfSigner.Sign(PdfDocument.Open(file), new PdfSignatureOptions(certificate) { FieldName = "a.b" }));

        // The generated name is not the name of the kid
        var generated = PdfSignatureVerifier.Verify(PdfSigner.Sign(PdfDocument.Open(file), new PdfSignatureOptions(certificate))).Single();
        Assert.Equal("Signature2", generated.FieldName);

        // The unsigned signature field is signed, once
        var filled = PdfSigner.Sign(PdfDocument.Open(file), new PdfSignatureOptions(certificate) { FieldName = "Empty" });
        var signature = PdfSignatureVerifier.Verify(filled).Single();
        Assert.True(signature.IsValid, signature.Error);
        Assert.Equal("Empty", signature.FieldName);
        Assert.HasCount(3, PdfDocument.Open(filled).Catalog.Get<PdfDictionary>(PdfNames.AcroForm)!.Get<PdfArray>(PdfName.Create("Fields"))!);
        Assert.Throws<ArgumentException>(() => PdfSigner.Sign(PdfDocument.Open(filled), new PdfSignatureOptions(certificate) { FieldName = "Empty" }));
    }

    [Theory]
    [InlineData(PdfCertificationLevel.NoChanges)]
    [InlineData(PdfCertificationLevel.FormFilling)]
    [InlineData(PdfCertificationLevel.FormFillingAndAnnotations)]
    public void CertificationPermissionsAreCheckedBeforeSigning(PdfCertificationLevel level)
    {
        using var certificate = CreateCertificate("rsa");
        var certified = PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate) { Certification = level });

        // The DocMDP transform of the signature, and the permissions of the catalog (12.8.2.2, 12.8.4.2)
        var document = PdfDocument.Open(certified);
        var dictionary = GetSignatureDictionary(document);
        var reference = (PdfDictionary)dictionary.Get<PdfArray>(PdfName.Create("Reference"))!.GetResolved(0);
        Assert.Equal("DocMDP", reference.GetName(PdfName.Create("TransformMethod"))!.Value);
        Assert.Equal((long)level, reference.Get<PdfDictionary>(PdfName.Create("TransformParams"))!.GetInteger(PdfNames.P));
        Assert.Same(dictionary, document.Catalog.Get<PdfDictionary>(PdfName.Create("Perms"))!.Get<PdfDictionary>(PdfName.Create("DocMDP")));
        Assert.Equal(level, PdfSignatureVerifier.Verify(certified).Single().CertificationLevel);

        if (level == PdfCertificationLevel.NoChanges)
        {
            Assert.Throws<InvalidOperationException>(() => PdfSigner.Sign(PdfDocument.Open(certified), new PdfSignatureOptions(certificate)));
            return;
        }

        var signatures = PdfSignatureVerifier.Verify(PdfSigner.Sign(PdfDocument.Open(certified), new PdfSignatureOptions(certificate)));
        Assert.All(signatures, signature => Assert.True(signature.IsValid, signature.Error));
        Assert.True(signatures[0].AreModificationsPermitted);
        Assert.Equal([PdfModificationKind.Signature], signatures[0].Modifications.Select(modification => modification.Kind).Distinct());
        Assert.Null(signatures[1].CertificationLevel);

        // A certification signature is the first signature
        Assert.Throws<InvalidOperationException>(() => PdfSigner.Sign(PdfDocument.Open(certified), new PdfSignatureOptions(certificate) { Certification = level }));
    }

    [Theory]
    [InlineData(PdfCertificationLevel.NoChanges, false, false)]
    [InlineData(PdfCertificationLevel.FormFilling, true, false)]
    [InlineData(PdfCertificationLevel.FormFillingAndAnnotations, true, true)]
    public void ChangesAfterACertificationAreChecked(PdfCertificationLevel level, bool formFillingAllowed, bool annotationAllowed)
    {
        using var certificate = CreateCertificate("rsa");
        var certified = PdfSigner.Sign(CreateDocumentWithTextField(), new PdfSignatureOptions(certificate) { Certification = level });

        var filled = PdfDocument.Open(certified);
        GetField(filled, "Name")[PdfName.Create("V")] = PdfString.FromText("Filled");
        var signature = PdfSignatureVerifier.Verify(SaveIncremental(filled)).Single();
        Assert.Equal(PdfModificationKind.FormFilling, signature.Modifications.Single().Kind);
        Assert.Equal("Name", signature.Modifications.Single().FieldName);
        Assert.Equal(formFillingAllowed, signature.AreModificationsPermitted);
        Assert.Equal(formFillingAllowed, signature.IsValid);

        var annotated = PdfDocument.Open(certified);
        annotated.Pages[0].Annotations.AddLink(new PdfRectangle(10, 10, 30, 30), new Uri("https://example.com/"));
        signature = PdfSignatureVerifier.Verify(SaveIncremental(annotated)).Single();
        Assert.Contains(PdfModificationKind.Annotation, signature.Modifications.Select(modification => modification.Kind));
        Assert.DoesNotContain(PdfModificationKind.Other, signature.Modifications.Select(modification => modification.Kind));
        Assert.Equal(annotationAllowed, signature.AreModificationsPermitted);

        // The content of a page is never allowed to change
        var drawn = PdfDocument.Open(certified);
        drawn.Pages[0].CreateContentBuilder().DrawText("Added", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 60, 600);
        signature = PdfSignatureVerifier.Verify(SaveIncremental(drawn)).Single();
        Assert.Contains(PdfModificationKind.Other, signature.Modifications.Select(modification => modification.Kind));
        Assert.False(signature.AreModificationsPermitted);
        Assert.False(signature.IsValid);
        Assert.True(signature.IsDocumentIntact);
    }

    [Fact]
    public void LockedFieldsCannotChange()
    {
        using var certificate = CreateCertificate("rsa");
        var document = CreateDocumentWithTextField();
        var other = document.Objects.Add(new PdfDictionary { [PdfName.Create("FT")] = PdfName.Create("Tx"), [PdfName.Create("T")] = PdfString.FromText("Other") });
        document.Catalog.Get<PdfDictionary>(PdfNames.AcroForm)!.Get<PdfArray>(PdfName.Create("Fields"))!.Add(other);
        var signed = PdfSigner.Sign(document, new PdfSignatureOptions(certificate) { FieldLock = new PdfFieldLock(PdfFieldLockAction.Include, ["Name"]) });

        // The FieldMDP transform of the signature and the lock of its field (12.8.2.4, 12.7.5.5)
        var read = PdfDocument.Open(signed);
        var reference = (PdfDictionary)GetField(read, "Signature1").Get<PdfDictionary>(PdfName.Create("V"))!.Get<PdfArray>(PdfName.Create("Reference"))!.GetResolved(0);
        Assert.Equal("FieldMDP", reference.GetName(PdfName.Create("TransformMethod"))!.Value);
        Assert.Equal("Include", reference.Get<PdfDictionary>(PdfName.Create("TransformParams"))!.GetName(PdfName.Create("Action"))!.Value);
        Assert.Equal("SigFieldLock", GetField(read, "Signature1").Get<PdfDictionary>(PdfName.Create("Lock"))!.GetName(PdfNames.Type)!.Value);
        var info = PdfSignatureVerifier.Verify(signed).Single();
        Assert.Equal(PdfFieldLockAction.Include, info.FieldLock!.Action);
        Assert.Equal(["Name"], info.FieldLock.Fields);

        GetField(read, "Other")[PdfName.Create("V")] = PdfString.FromText("Allowed");
        Assert.True(PdfSignatureVerifier.Verify(SaveIncremental(read)).Single().IsValid);

        var locked = PdfDocument.Open(signed);
        GetField(locked, "Name")[PdfName.Create("V")] = PdfString.FromText("Changed");
        info = PdfSignatureVerifier.Verify(SaveIncremental(locked)).Single();
        Assert.False(info.AreModificationsPermitted);
        Assert.False(info.IsValid);

        Assert.Throws<ArgumentException>(() => new PdfFieldLock(PdfFieldLockAction.Include));
    }

    [Fact]
    public void ModificationsAfterASignatureAreReported()
    {
        using var certificate = CreateCertificate("rsa");
        var signed = PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate));

        // Nothing changed: the same objects saved again are not changes
        var unchanged = PdfDocument.Open(signed);
        unchanged.Catalog[PdfNames.Type] = PdfNames.Catalog;
        Assert.Empty(PdfSignatureVerifier.Verify(SaveIncremental(unchanged)).Single().Modifications);

        var second = PdfSigner.Sign(PdfDocument.Open(signed), new PdfSignatureOptions(certificate));
        var modifications = PdfSignatureVerifier.Verify(second)[0].Modifications;
        Assert.NotEmpty(modifications);
        Assert.All(modifications, modification => Assert.Equal(PdfModificationKind.Signature, modification.Kind));
        Assert.Contains(modifications, modification => modification.IsNewObject && modification.FieldName == "Signature2");

        var information = PdfDocument.Open(signed);
        information.Information.Title = "Changed";
        Assert.Equal([PdfModificationKind.Other], PdfSignatureVerifier.Verify(SaveIncremental(information)).Single().Modifications.Select(modification => modification.Kind).Distinct());
    }

    [Theory]
    [InlineData("SHA256")]
    [InlineData("SHA512")]
    public void RsaPssSignaturesAreCreatedAndVerified(string hashAlgorithm)
    {
        using var certificate = CreateCertificate("rsa");
        var signed = PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate) { HashAlgorithm = new HashAlgorithmName(hashAlgorithm), RsaSignaturePadding = RSASignaturePadding.Pss });

        var signature = PdfSignatureVerifier.Verify(signed).Single();
        Assert.True(signature.IsValid, signature.Error);

        // RSASSA-PSS (RFC 4055), checked by SignedCms with the signed bytes
        var cms = DecodeWithContent(signed, signature);
        Assert.Equal("1.2.840.113549.1.1.10", cms.SignerInfos[0].SignatureAlgorithm.Value);
        cms.CheckSignature(verifySignatureOnly: true);

        using var ecdsa = CreateCertificate("ecdsa");
        Assert.Throws<ArgumentException>(() => PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(ecdsa) { RsaSignaturePadding = RSASignaturePadding.Pss }));
    }

    [Fact]
    public void TheSigningCertificateAttributeIsChecked()
    {
        // Two certificates of the same key: the signature is valid for both, the ESS attribute names the first one
        using var key = RSA.Create(2048);
        var now = DateTimeOffset.UtcNow;
        using var first = new CertificateRequest("CN=First", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        using var substitute = new CertificateRequest("CN=Substitute", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        var signed = PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(first));
        Assert.True(PdfSignatureVerifier.Verify(signed).Single().IsSigningCertificateValid);

        var substituted = ReplaceSignature(signed, content =>
        {
            var cms = new SignedCms(new ContentInfo(content), detached: true);
            var signer = new CmsSigner(substitute) { DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), IncludeOption = X509IncludeOption.EndCertOnly };
            signer.SignedAttributes.Add(new AsnEncodedData("1.2.840.113549.1.9.16.2.47", EncodeSigningCertificateV2(first)));
            cms.ComputeSignature(signer, silent: true);
            return cms.Encode();
        });

        var signature = PdfSignatureVerifier.Verify(substituted).Single();
        Assert.True(signature.IsDocumentIntact);
        Assert.True(signature.IsSignatureValid);
        Assert.False(signature.IsSigningCertificateValid);
        Assert.False(signature.IsValid);
        Assert.Equal(substitute.Thumbprint, signature.Certificate!.Thumbprint);

        // A PAdES signature without the attribute is not valid
        var withoutAttribute = ReplaceSignature(signed, content => SignWithAttributes(content, first, includeSigningTime: true));
        signature = PdfSignatureVerifier.Verify(withoutAttribute).Single();
        Assert.True(signature.IsSignatureValid);
        Assert.False(signature.IsSigningCertificateValid);
    }

    [Fact]
    public void ASignatureHasOneSigner()
    {
        using var certificate = CreateCertificate("rsa");
        using var other = CreateCertificate("ecdsa");
        var signed = PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate));

        var twoSigners = ReplaceSignature(signed, content =>
        {
            var cms = new SignedCms(new ContentInfo(content), detached: true);
            cms.ComputeSignature(new CmsSigner(certificate), silent: true);
            cms.ComputeSignature(new CmsSigner(other), silent: true);
            return cms.Encode();
        });

        var signature = PdfSignatureVerifier.Verify(twoSigners).Single();
        Assert.Contains("2 signers", signature.Error, StringComparison.Ordinal);
        Assert.False(signature.IsValid);
    }

    [Fact]
    public async Task EveryTimestampOfASignatureIsChecked()
    {
        using var certificate = CreateCertificate("rsa");
        using var authority = new TestTimestampAuthority(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        using var httpClient = new HttpClient(authority);
        var signed = await PdfSigner.SignAsync(CreateDocument(), new PdfSignatureOptions(certificate) { TimestampAuthority = new Uri("http://timestamp.example.com/"), HttpClient = httpClient }, XunitCancellationToken);
        var signature = PdfSignatureVerifier.Verify(signed).Single();
        var cms = DecodeWithContent(signed, signature);
        var valid = cms.SignerInfos[0].UnsignedAttributes[0].Values[0].RawData;

        // A second valid token, and a token of other data: the invalid one is not hidden by the valid ones
        var invalid = await RequestTokenAsync(httpClient, SHA256.HashData("other data"u8));
        Assert.True(PdfSignatureVerifier.Verify(AddTimestamps(signed, signature, valid)).Single().IsTimestampValid);
        var withInvalid = PdfSignatureVerifier.Verify(AddTimestamps(signed, signature, invalid)).Single();
        Assert.False(withInvalid.IsTimestampValid);
        Assert.False(withInvalid.IsValid);
        Assert.Equal(authority.Time, withInvalid.TimestampTime);

        static byte[] AddTimestamps(byte[] signed, PdfSignatureInfo signature, byte[] token)
        {
            var cms = DecodeWithContent(signed, signature);
            cms.SignerInfos[0].AddUnsignedAttribute(new AsnEncodedData("1.2.840.113549.1.9.16.2.14", token));
            var encoded = cms.Encode();
            return ReplaceSignature(signed, _ => encoded);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetachedSignaturesWithAndWithoutSignedAttributesAreVerified(bool signedAttributes)
    {
        using var certificate = CreateCertificate("rsa");
        var signed = PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate));

        // adbe.pkcs7.detached has the same length as ETSI.CAdES.detached: the offsets do not change
        var pkcs7 = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(signed).Replace("/ETSI.CAdES.detached", "/adbe.pkcs7.detached", StringComparison.Ordinal));
        var replaced = ReplaceSignature(pkcs7, content =>
        {
            if (signedAttributes)
                return SignWithAttributes(content, certificate, includeSigningTime: true);

            var cms = new SignedCms(new ContentInfo(content), detached: true);
            cms.ComputeSignature(new CmsSigner(certificate) { DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1") }, silent: true);
            Assert.Empty(cms.SignerInfos[0].SignedAttributes);
            return cms.Encode();
        });

        var signature = PdfSignatureVerifier.Verify(replaced).Single();
        Assert.Equal("adbe.pkcs7.detached", signature.SubFilter);
        Assert.True(signature.IsValid, signature.Error);
        Assert.Null(signature.IsSigningCertificateValid);

        // A byte of the signed content
        var tampered = (byte[])replaced.Clone();
        tampered[Encoding.Latin1.GetString(tampered).IndexOf("%%EOF", StringComparison.Ordinal) + 1] = (byte)'X';
        signature = PdfSignatureVerifier.Verify(tampered).Single();
        Assert.False(signature.IsDocumentIntact);
        Assert.False(signature.IsValid);
    }

    [Fact]
    public void Example_VisibleSignature()
    {
        using var certificate = CreateCertificate("rsa");
        var document = CreateDocument();

        #region PdfSigning_VisibleSignature
        // The widget is shown on the first page, at the bottom left; the drawing is in the coordinates of the rectangle
        var options = new PdfSignatureOptions(certificate)
        {
            Reason = "I approve this contract",
            Appearance = new PdfSignatureAppearance(pageIndex: 0, PdfRectangle.FromSize(60, 60, 200, 50))
            {
                Draw = builder => builder
                    .DrawText("Signed by Alice", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 8, 30)
                    .DrawText("I approve this contract", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 9, 8, 12),
            },
        };
        var signed = PdfSigner.Sign(document, options);
        #endregion

        Assert.True(PdfSignatureVerifier.Verify(signed).Single().IsValid);
        var widget = GetField(PdfDocument.Open(signed), "Signature1");
        Assert.Equal(new PdfRectangle(60, 60, 260, 110), PdfRectangle.TryRead(widget.Get<PdfArray>(PdfNames.Rect), out var rectangle) ? rectangle : default);
        var appearance = widget.Get<PdfDictionary>(PdfName.Create("AP"))!.Get<PdfStream>(PdfNames.N)!;
        Assert.Contains("Signed by Alice", Encoding.Latin1.GetString(appearance.GetDecodedData()), StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate) { Appearance = new PdfSignatureAppearance(3, PdfRectangle.FromSize(0, 0, 10, 10)) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfSignatureAppearance(0, new PdfRectangle(0, 0, 0, 10)));
    }

    [Fact]
    public void Example_DefaultSignatureAppearance()
    {
        using var certificate = CreateCertificate("rsa");
        var document = CreateDocument();
        using var bitmap = Image.ImportPixelData<Rgba32>([new Rgba32(255, 0, 0, 255), new Rgba32(255, 0, 0, 255)], 2, 1);
        var image = PdfImage.FromImage(bitmap);

        #region PdfSigning_DefaultAppearance
        // Without a drawing, the library draws the name of the signer, the date, the reason and the location, and the image at their left
        var options = new PdfSignatureOptions(certificate)
        {
            Reason = "I approve this contract",
            Location = "Paris",
            SigningTime = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            Appearance = new PdfSignatureAppearance(pageIndex: 0, PdfRectangle.FromSize(60, 60, 200, 50))
            {
                Image = image, // Such as PdfImage.Load("signature.png")
            },
        };
        var signed = PdfSigner.Sign(document, options);
        #endregion

        var signature = PdfSignatureVerifier.Verify(signed).Single();
        Assert.True(signature.IsValid, signature.Error);
        Assert.True(signature.CoversWholeDocument);

        var signedDocument = PdfDocument.Open(signed);
        var widget = GetField(signedDocument, "Signature1");
        Assert.Equal(new PdfRectangle(60, 60, 260, 110), PdfRectangle.TryRead(widget.Get<PdfArray>(PdfNames.Rect), out var rectangle) ? rectangle : default);
        Assert.Equal(4, widget.Get<PdfInteger>(PdfName.Create("F"))!.Value & 4); // Print (12.5.3)
        var annotations = signedDocument.Pages[0].Dictionary.Get<PdfArray>(PdfNames.Annots)!;
        Assert.Contains(annotations, item => ReferenceEquals(PdfReference.ResolveIfReference(item), widget));

        var appearance = widget.Get<PdfDictionary>(PdfName.Create("AP"))!.Get<PdfStream>(PdfNames.N)!;
        Assert.Equal(new PdfRectangle(0, 0, 200, 50), PdfRectangle.TryRead(appearance.Dictionary.Get<PdfArray>(PdfName.Create("BBox")), out var box) ? box : default);
        var content = Encoding.Latin1.GetString(appearance.GetDecodedData());
        Assert.Contains("(Digitally signed by RSA signer)", content, StringComparison.Ordinal);
        Assert.Contains("(Date: 2026-01-02 03:04:05 +00:00)", content, StringComparison.Ordinal);
        Assert.Contains("(Reason: I approve this contract)", content, StringComparison.Ordinal);
        Assert.Contains("(Location: Paris)", content, StringComparison.Ordinal);
        Assert.Contains(" Do", content, StringComparison.Ordinal);

        // The image fills the left 40% of the rectangle (2:1, centered vertically), the text is at its right
        using var page = PdfRenderer.RenderPage(signedDocument.Pages[0], new PdfRenderOptions { Dpi = 72, UseSystemFonts = false });
        var red = page.Frames[0][100, page.Height - 1 - 85];
        Assert.True(red.R > 200 && red.G < 50 && red.B < 50, $"The pixel is ({red.R}, {red.G}, {red.B})");
        Assert.True(CountDarkPixels(page, 145, 62, 258, 108) > 50);
        Assert.Equal(0, CountDarkPixels(page, 270, 50, 400, 120));

        static int CountDarkPixels(Image<Rgba32> image, int left, int bottom, int right, int top)
        {
            var count = 0;
            for (var y = bottom; y < top; y++)
            {
                for (var x = left; x < right; x++)
                {
                    var pixel = image.Frames[0][x, image.Height - 1 - y];
                    if (pixel.R < 128 && pixel.G < 128 && pixel.B < 128)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }

    [Fact]
    public void DefaultSignatureAppearanceLayout()
    {
        using var certificate = CreateCertificate("rsa");

        // The name of the options replaces the name of the certificate; the text shrinks to fit the rectangle
        var small = GetAppearanceContent(PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate)
        {
            SignerName = "Alice",
            Reason = "A long reason that does not fit on one line of a small rectangle",
            Appearance = new PdfSignatureAppearance(0, PdfRectangle.FromSize(10, 10, 80, 40)),
        }));
        Assert.Contains("(Digitally signed by", small, StringComparison.Ordinal);
        Assert.Contains("Alice", small, StringComparison.Ordinal);
        Assert.DoesNotContain("RSA signer", small, StringComparison.Ordinal);
        Assert.DoesNotContain("Location", small, StringComparison.Ordinal);
        Assert.DoesNotContain(" 10 Tf", small, StringComparison.Ordinal);

        // A font size of the options; a rectangle taller than wide puts the image above the text
        using var bitmap = Image.ImportPixelData<Rgba32>([new Rgba32(0, 0, 255, 255)], 1, 1);
        var tall = GetAppearanceContent(PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate)
        {
            Appearance = new PdfSignatureAppearance(0, PdfRectangle.FromSize(10, 10, 100, 200)) { FontSize = 7, Image = PdfImage.FromImage(bitmap) },
        }));
        Assert.Contains(" 7 Tf", tall, StringComparison.Ordinal);
        Assert.Contains("76.8 0 0 76.8 11.6 119.2 cm", tall, StringComparison.Ordinal);

        // A drawing that draws nothing is an empty appearance
        Assert.Equal("", GetAppearanceContent(PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate)
        {
            Appearance = new PdfSignatureAppearance(0, PdfRectangle.FromSize(10, 10, 100, 50)) { Draw = _ => { } },
        })));

        // Helvetica cannot show Cyrillic: the default layout needs a font that has the characters
        var exception = Assert.Throws<ArgumentException>(() => PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate)
        {
            SignerName = "\u0410\u043B\u0438\u0441\u0430",
            Appearance = new PdfSignatureAppearance(0, PdfRectangle.FromSize(10, 10, 100, 50)),
        }));
        Assert.Contains(nameof(PdfSignatureAppearance.Font), exception.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfSignatureAppearance(0, PdfRectangle.FromSize(0, 0, 10, 10)) { FontSize = -1 });

        static string GetAppearanceContent(byte[] signed)
        {
            Assert.True(PdfSignatureVerifier.Verify(signed).Single().IsValid);
            var widget = GetField(PdfDocument.Open(signed), "Signature1");
            return Encoding.Latin1.GetString(widget.Get<PdfDictionary>(PdfName.Create("AP"))!.Get<PdfStream>(PdfNames.N)!.GetDecodedData());
        }
    }

    [Fact]
    public async Task Example_DocumentTimestamp()
    {
        using var certificate = CreateCertificate("rsa");
        using var authority = new TestTimestampAuthority(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        using var httpClient = new HttpClient(authority);
        var signedFile = PdfSigner.Sign(CreateDocument(), new PdfSignatureOptions(certificate) { Certification = PdfCertificationLevel.NoChanges });

        #region PdfSigning_LongTermValidation
        // PAdES B-LT: the certificates and the revocation evidence that validate the signatures, in the document security store
        var validationData = new PdfValidationData();
        validationData.Certificates.Add(authority.Certificate);
        validationData.CertificateRevocationLists.Add(CertificateRevocationList);
        var withValidationData = PdfSigner.AddValidationData(PdfDocument.Open(signedFile), validationData);

        // PAdES B-LTA: a document time stamp protects the validation data
        var timestamped = await PdfSigner.TimestampAsync(PdfDocument.Open(withValidationData), new PdfTimestampOptions(new Uri("http://timestamp.example.com/"))
        {
            HttpClient = httpClient,
        }, XunitCancellationToken);
        #endregion

        var signatures = PdfSignatureVerifier.Verify(timestamped);
        Assert.HasCount(2, signatures);
        Assert.All(signatures, signature => Assert.True(signature.IsValid, signature.Error));
        Assert.True(signatures[1].IsDocumentTimestamp);
        Assert.Equal(authority.Time, signatures[1].TimestampTime);
        Assert.True(signatures[1].CoversWholeDocument);

        // The certification forbids changes, but not validation data and document time stamps
        Assert.True(signatures[0].AreModificationsPermitted);
        Assert.Equal([PdfModificationKind.ValidationData, PdfModificationKind.DocumentTimestamp], signatures[0].Modifications.Select(modification => modification.Kind).Distinct().Order());

        var data = PdfSignatureVerifier.GetValidationData(timestamped);
        Assert.Contains(authority.Certificate.Thumbprint, data.Certificates.Select(item => item.Thumbprint));
        Assert.Contains(certificate.Thumbprint, data.Certificates.Select(item => item.Thumbprint));
        Assert.Equal([CertificateRevocationList], data.CertificateRevocationLists);

        // The data the store has is not added again
        var again = PdfSigner.AddValidationData(PdfDocument.Open(timestamped), validationData);
        Assert.HasCount(data.Certificates.Count, PdfSignatureVerifier.GetValidationData(again).Certificates);
        Assert.Single(PdfSignatureVerifier.GetValidationData(again).CertificateRevocationLists);
    }

    private static readonly byte[] CertificateRevocationList = CreateCertificateRevocationList();

    private static byte[] CreateCertificateRevocationList()
    {
        using var key = RSA.Create(2048);
        var now = DateTimeOffset.UtcNow;
        var request = new CertificateRequest("CN=CRL issuer", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.KeyCertSign, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        using var issuer = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        return new CertificateRevocationListBuilder().Build(issuer, 1, now.AddDays(7), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, now);
    }

    private static PdfDocument CreateDocumentWithTextField()
    {
        var document = CreateDocument();
        var field = document.Objects.Add(new PdfDictionary { [PdfName.Create("FT")] = PdfName.Create("Tx"), [PdfName.Create("T")] = PdfString.FromText("Name") });
        document.Catalog[PdfNames.AcroForm] = document.Objects.Add(new PdfDictionary { [PdfName.Create("Fields")] = new PdfArray(field) });
        return document;
    }

    private static PdfDictionary GetField(PdfDocument document, string name)
    {
        foreach (var item in document.Catalog.Get<PdfDictionary>(PdfNames.AcroForm)!.Get<PdfArray>(PdfName.Create("Fields"))!)
        {
            if (PdfReference.ResolveIfReference(item) is PdfDictionary field && field.GetText(PdfName.Create("T")) == name)
                return field;
        }

        throw new InvalidOperationException("No field " + name);
    }

    private static byte[] SaveIncremental(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.SaveIncremental(stream);
        return stream.ToArray();
    }

    /// <summary>Replaces the CMS signature of a signed file by another one of the same signed bytes, built by the test.</summary>
    private static byte[] ReplaceSignature(byte[] signed, Func<byte[], byte[]> sign)
    {
        var ranges = PdfSignatureVerifier.Verify(signed).Single().ByteRanges;
        var content = signed.AsSpan(0, (int)ranges[0].Length).ToArray().Concat(signed.AsSpan((int)ranges[1].Offset).ToArray()).ToArray();
        var hex = Encoding.ASCII.GetBytes(Convert.ToHexString(sign(content)));
        var gap = (int)(ranges[1].Offset - ranges[0].Length) - 2;
        Assert.True(hex.Length - gap <= 0, "The signature does not fit");
        var result = (byte[])signed.Clone();
        result.AsSpan((int)ranges[0].Length + 1, gap).Fill((byte)'0');
        hex.CopyTo(result, (int)ranges[0].Length + 1);
        return result;
    }

    private static SignedCms DecodeWithContent(byte[] signed, PdfSignatureInfo signature)
    {
        var ranges = signature.ByteRanges;
        var content = signed.AsSpan(0, (int)ranges[0].Length).ToArray().Concat(signed.AsSpan((int)ranges[1].Offset).ToArray()).ToArray();
        var contents = Convert.FromHexString(Encoding.ASCII.GetString(signed, (int)ranges[0].Length + 1, (int)(ranges[1].Offset - ranges[0].Length) - 2));
        var cms = new SignedCms(new ContentInfo(content), detached: true);
        cms.Decode(contents.AsSpan(0, GetEncodedLength(contents)));
        return cms;
    }

    private static byte[] SignWithAttributes(byte[] content, X509Certificate2 certificate, bool includeSigningTime)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: true);
        var signer = new CmsSigner(certificate) { DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1") };
        if (includeSigningTime)
        {
            signer.SignedAttributes.Add(new Pkcs9SigningTime(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
        }

        cms.ComputeSignature(signer, silent: true);
        return cms.Encode();
    }

    private static async Task<byte[]> RequestTokenAsync(HttpClient httpClient, byte[] hash)
    {
        var request = Rfc3161TimestampRequest.CreateFromHash(hash, HashAlgorithmName.SHA256, requestSignerCertificates: true);
        using var content = new ByteArrayContent(request.Encode());
        using var response = await httpClient.PostAsync(new Uri("http://timestamp.example.com/"), content, XunitCancellationToken);
        return request.ProcessResponse(await response.Content.ReadAsByteArrayAsync(XunitCancellationToken), out _).AsSignedCms().Encode();
    }

    /// <summary>The ESS signing-certificate-v2 attribute of RFC 5035, with the issuer and the serial number.</summary>
    private static byte[] EncodeSigningCertificateV2(X509Certificate2 certificate)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence())
        using (writer.PushSequence())
        {
            writer.WriteOctetString(SHA256.HashData(certificate.RawData));
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true)))
                {
                    writer.WriteEncodedValue(certificate.IssuerName.RawData);
                }

                writer.WriteInteger(certificate.SerialNumberBytes.Span);
            }
        }

        return writer.Encode();
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

    private static PdfDictionary GetSignatureDictionary(byte[] file) => GetSignatureDictionary(PdfDocument.Open(file));

    private static PdfDictionary GetSignatureDictionary(PdfDocument document)
    {
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
