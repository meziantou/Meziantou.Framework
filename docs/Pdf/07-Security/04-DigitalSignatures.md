---
title: Digital signatures
description: Sign a document with a certificate (PAdES B-B to B-LTA), certify it or lock fields, show the signature, and verify the signatures and the changes made after them.
---

A signature proves that the signed bytes of a document did not change since it was signed, and binds them to the
certificate of the signer. The types are in the [Meziantou.Framework.Pdf.Signing](cref:Meziantou.Framework.Pdf.Signing) namespace.

## Sign a document

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfSignerTests.cs;region=PdfSigning_Sign;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
document.Pages.Add(PdfSize.A4).CreateContentBuilder()
    .DrawText("Contract", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);

// The certificate has its private key: X509CertificateLoader.LoadPkcs12FromFile loads one from a .pfx file
var signed = PdfSigner.Sign(document, new PdfSignatureOptions(certificate) { Reason = "I approve this contract" });

var signature = PdfSignatureVerifier.Verify(signed).Single();
var isValid = signature.IsValid && signature.CoversWholeDocument;
```
<!-- endref -->

- [PdfSigner.Sign](cref:Meziantou.Framework.Pdf.Signing.PdfSigner.Sign) returns the signed file. The signature is a PAdES baseline B-B signature (a CAdES detached signature,
  ETSI EN 319 142-1), in an invisible signature field on the first page.
- The signature is appended to the file as an incremental update, so the signatures the document already has stay
  valid. A document that was not read from a file is saved in full first; the changes made to a document read from a
  file are saved in the update of the signature.
- The certificate must have its private key (RSA or ECDSA): `X509CertificateLoader.LoadPkcs12FromFile` loads one from
  a `.pfx` file. Encrypted documents are signed with their keys.

| [PdfSignatureOptions](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions) | Default | Sets |
| --- | --- | --- |
| [Reason](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.Reason), `Location`, [ContactInfo](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.ContactInfo), [SignerName](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.SignerName) | none | The information of the signature, shown by readers |
| [SigningTime](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.SigningTime) | Now | The time the signer claims; only a time stamp proves it |
| [Certificates](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.Certificates) | Empty | The other certificates of the chain of the signer, embedded so that the signature can be validated |
| [HashAlgorithm](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.HashAlgorithm) | SHA-256 | The digest algorithm: SHA-256, SHA-384 or SHA-512 |
| [RsaSignaturePadding](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.RsaSignaturePadding) | PKCS #1 v1.5 | `RSASignaturePadding.Pss` for an RSASSA-PSS signature (RSA keys only) |
| [FieldName](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.FieldName) | `Signature1`, `Signature2`... | The name of the signature field. An existing signature field that is not signed yet is signed; the name of any other field throws `ArgumentException` |
| [Appearance](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.Appearance) | Invisible | The page and the rectangle of a visible signature, and its drawing or the options of the default layout |
| [Certification](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.Certification), [FieldLock](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.FieldLock) | none | The changes a certification signature allows, and the fields the signature locks |
| [TimestampAuthority](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.TimestampAuthority), [HttpClient](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.HttpClient) | none | The time stamp authority of a B-T signature, and the client to call it |
| [ReservedSize](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.ReservedSize) | Computed | The bytes reserved for the signature in the file |

## Time stamp a signature

A time stamp from a trusted authority (RFC 3161) proves when the document was signed (PAdES B-T). It needs a request to
the authority, so it is done by [SignAsync](cref:Meziantou.Framework.Pdf.Signing.PdfSigner.SignAsync); [Sign](cref:Meziantou.Framework.Pdf.Signing.PdfSigner.Sign) throws `ArgumentException` when a time stamp is asked:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfSignerTests.cs;region=PdfSigning_Timestamp;format=md-fenced-code-block;dedent=true -->
```csharp
// PAdES B-T: the signature is stamped by an RFC 3161 time stamp authority
var options = new PdfSignatureOptions(certificate)
{
    TimestampAuthority = new Uri("http://timestamp.example.com/"),
    HttpClient = httpClient,
};
var signed = await PdfSigner.SignAsync(document, options, XunitCancellationToken);
```
<!-- endref -->

[SignAsync](cref:Meziantou.Framework.Pdf.Signing.PdfSigner.SignAsync) throws `HttpRequestException` when the authority cannot be reached.

## Show the signature on a page

By default the signature field is invisible. Set [Appearance](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.Appearance) to show it in a rectangle of a page, given in the
default user space of the page (points, from the bottom left of the page). The widget is printed with the page, and
readers show it in the list of annotations of the page.

Without a drawing, the library draws a default layout: the name of the signer ([SignerName](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.SignerName), or the name of the
certificate), the date ([SigningTime](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.SigningTime), as `yyyy-MM-dd HH:mm:ss` and its offset), the reason and the location, with an
optional image, such as a scan of a handwritten signature or a logo:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfSignerTests.cs;region=PdfSigning_DefaultAppearance;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

| [PdfSignatureAppearance](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureAppearance) | Default | Sets |
| --- | --- | --- |
| [Image](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureAppearance.Image) | none | The image, scaled to fit the left 40% of the rectangle (the top 40% when the rectangle is taller than wide), keeping its proportions |
| [Font](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureAppearance.Font) | Helvetica | The font of the text. Helvetica shows the characters of `WinAnsiEncoding`: use an embedded font ([PdfFont.Load](cref:Meziantou.Framework.Pdf.PdfFont.Load)) for the others, or [Sign](cref:Meziantou.Framework.Pdf.Signing.PdfSigner.Sign) throws `ArgumentException` |
| [FontSize](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureAppearance.FontSize) | Fitted | The font size; by default the largest size, up to 10, at which the text fits in the rectangle |

To draw the appearance yourself, set [Draw](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureAppearance.Draw): it receives the content builder of the appearance, whose coordinates are
those of the rectangle, from (0, 0) at its bottom left. A drawing that draws nothing gives an empty appearance.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfSignerTests.cs;region=PdfSigning_VisibleSignature;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

## Certify a document or lock fields

A certification signature is the first signature of a document: [Certification](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.Certification) sets the changes it allows after it
(DocMDP). [PdfCertificationLevel.NoChanges](cref:Meziantou.Framework.Pdf.Signing.PdfCertificationLevel.NoChanges) forbids any other signature, [FormFilling](cref:Meziantou.Framework.Pdf.Signing.PdfCertificationLevel.FormFilling) allows filling in
the form and signing, and [FormFillingAndAnnotations](cref:Meziantou.Framework.Pdf.Signing.PdfCertificationLevel.FormFillingAndAnnotations) also allows annotations. Validation data and document time
stamps are allowed at every level. [Sign](cref:Meziantou.Framework.Pdf.Signing.PdfSigner.Sign) throws `InvalidOperationException` when the certification of the document
does not allow another signature, or when a certification is requested for a document that is already signed.

[FieldLock](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.FieldLock) locks fields (FieldMDP): `new PdfFieldLock(PdfFieldLockAction.Include, ["Name"])` locks the field `Name`
and its descendants; [PdfFieldLockAction.All](cref:Meziantou.Framework.Pdf.Signing.PdfFieldLockAction.All) locks every field. When a signature field that is not signed yet has a
lock dictionary, its lock is used.

## Long-term validation

A signature can be validated years later only with the certificates and the revocation evidence of the time it was
made. [PdfSigner.AddValidationData](cref:Meziantou.Framework.Pdf.Signing.PdfSigner.AddValidationData) writes them to the document security store (PAdES B-LT), with the certificates the
signatures and their time stamps embed; [PdfSigner.TimestampAsync](cref:Meziantou.Framework.Pdf.Signing.PdfSigner.TimestampAsync) then adds a document time stamp that protects them
(PAdES B-LTA). Repeat both steps before the time stamp authority's certificate expires to extend the validity.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfSignerTests.cs;region=PdfSigning_LongTermValidation;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

- The data the store already has is not added again. This method writes no VRI dictionaries.

[PdfSigner.AddValidationDataAsync](cref:Meziantou.Framework.Pdf.Signing.PdfSigner.AddValidationDataAsync) gathers the validation data itself: it builds the chains of the signers, of the
time stamp authorities and of the document time stamps from the certificates the signatures embed, those of the store
and those of [PdfValidationDataOptions](cref:Meziantou.Framework.Pdf.Signing.PdfValidationDataOptions), and finds the revocation evidence of each certificate of the chains that is
not self-signed among the revocation lists and the OCSP responses of the options and of the store. With
[DownloadMissingData](cref:Meziantou.Framework.Pdf.Signing.PdfValidationDataOptions.DownloadMissingData), it downloads what is missing with the HTTP client of the options: the issuers from the
addresses of the certificates (`caIssuers`), then an OCSP response from the responder of each certificate, or else the
revocation list of one of its distribution points. [IncludeVri](cref:Meziantou.Framework.Pdf.Signing.PdfValidationDataOptions.IncludeVri) adds a VRI dictionary for each signature, which lists
the certificates and the revocation evidence of its chains.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfSignerTests.cs;region=PdfSigning_DownloadValidationData;format=md-fenced-code-block;dedent=true -->
```csharp
// PAdES B-LT: the chains of the signatures are completed, and the missing revocation evidence is downloaded
// from the addresses of the certificates (OCSP responders, revocation lists, issuers)
var options = new PdfValidationDataOptions
{
    DownloadMissingData = true,
    HttpClient = httpClient,
    IncludeVri = true,
};
options.Certificates.Add(trustedRoot);
var withValidationData = await PdfSigner.AddValidationDataAsync(PdfDocument.Open(signedFile), options, XunitCancellationToken);
```
<!-- endref -->

- Nothing is downloaded unless [DownloadMissingData](cref:Meziantou.Framework.Pdf.Signing.PdfValidationDataOptions.DownloadMissingData) is set; [AddValidationData](cref:Meziantou.Framework.Pdf.Signing.PdfSigner.AddValidationData) with
  [PdfValidationDataOptions](cref:Meziantou.Framework.Pdf.Signing.PdfValidationDataOptions) does the same work offline. Pass an `HttpClient` configured with the proxy and the
  timeouts of the application.
- Give the root certificates in [Certificates](cref:Meziantou.Framework.Pdf.Signing.PdfValidationDataOptions.Certificates): the chains end where no issuer is found.
- The library does not check the downloaded data: a revocation list or an OCSP response is stored when it comes from
  the issuer of the certificate, whatever its status. Validate the signatures with the trust policy of the
  application. When no address of a certificate answers, the method throws an `HttpRequestException`.
- The revocation evidence of the certificates of the OCSP responders is not downloaded: they usually have the
  `id-pkix-ocsp-nocheck` extension (RFC 6960, 4.2.2.2.1).
- [PdfSignatureVerifier.GetValidationData](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureVerifier.GetValidationData) reads the store back, for instance to build the chains offline with
  `X509ChainPolicy.ExtraStore`.

## Verify the signatures

[PdfSignatureVerifier.Verify](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureVerifier.Verify) finds the signatures of a file and checks each of them: the bytes it covers are those that
were signed, the cryptographic signature matches the certificate it embeds, and its time stamp is valid. It does not
decide whether the signer is trusted: build the chain of the certificate with the trust anchors and the revocation
policy of the application. Pass [PdfReadOptions](cref:Meziantou.Framework.Pdf.PdfReadOptions) with the password to verify an encrypted document.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfSignerTests.cs;region=PdfSigning_Verify;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

| [PdfSignatureInfo](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo) | Tells |
| --- | --- |
| [IsValid](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.IsValid) | The signature is intact, cryptographically valid, its time stamps are valid, its ESS signing certificate names the certificate of the signer, and no change after it breaks its certification or its field lock |
| [IsDocumentIntact](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.IsDocumentIntact), [IsSignatureValid](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.IsSignatureValid), [IsTimestampValid](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.IsTimestampValid), [IsSigningCertificateValid](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.IsSigningCertificateValid) | Each of these checks; [IsTimestampValid](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.IsTimestampValid) is `null` without a time stamp, and `false` when any of several time stamps is invalid; [IsSigningCertificateValid](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.IsSigningCertificateValid) is `null` without the attribute, except for `ETSI.CAdES.detached`, which requires it |
| [Modifications](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.Modifications), [AreModificationsPermitted](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.AreModificationsPermitted) | The objects the revisions appended after the signature added or changed, with what each change is (a signature, validation data, a document time stamp, form filling, an annotation, or another change), and whether the certification and the field lock of the signature allow them |
| [CertificationLevel](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.CertificationLevel), [FieldLock](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.FieldLock) | The certification and the field lock of the signature |
| [CoversWholeDocument](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.CoversWholeDocument) | Nothing was appended to the file after the signature. When it is `false`, the document was changed after it was signed: the signature covers an earlier revision only |
| [Certificate](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.Certificate), [Certificates](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.Certificates) | The certificate of the signer, and the certificates the signature embeds |
| [SigningTime](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.SigningTime), [TimestampTime](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.TimestampTime) | The time the signer claims, and the time a time stamp proves |
| [Reason](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.Reason), `Location`, [ContactInfo](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.ContactInfo), [SignerName](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.SignerName), [FieldName](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.FieldName) | The information of the signature |
| [SubFilter](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.SubFilter), [IsDocumentTimestamp](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.IsDocumentTimestamp) | The format: `ETSI.CAdES.detached`, `adbe.pkcs7.detached`, `adbe.pkcs7.sha1`, or `ETSI.RFC3161` for a document time stamp |
| [Error](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.Error) | Why the signature could not be checked |

- The information of a signature (its name, reason, time and format) is read from the revision it signs: a later
  change of the signature dictionary is reported in [Modifications](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.Modifications), not returned as the information.
- A signature has one signer: CMS signed data with more signers has an [Error](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.Error).
- [Modifications](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.Modifications) is a summary built from the structure of the document (the fields, the annotations of the pages,
  the document security store), not a proof that the pages look the same: review it, and treat a
  [PdfModificationKind.Other](cref:Meziantou.Framework.Pdf.Signing.PdfModificationKind.Other) change as a change of the content.

The signatures of other tools are verified as well, in the formats above, with or without signed attributes, and RSA
signatures with PKCS #1 v1.5 or PSS padding.
