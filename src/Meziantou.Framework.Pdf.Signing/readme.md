# Meziantou.Framework.Pdf.Signing

Digital signatures of PDF documents (ISO 32000-2, 12.8) for `Meziantou.Framework.Pdf`: PAdES baseline signatures
(ETSI EN 319 142-1) and the verification of the signatures of a document.

- **Signing**: B-B, a CAdES detached signature (`ETSI.CAdES.detached`) with the ESS signing certificate, and B-T, its
  RFC 3161 time stamp; RSA and ECDSA keys, SHA-256, SHA-384 and SHA-512; the signature is appended as an incremental
  update, so that the signatures of the document stay valid; encrypted documents are signed with their keys
- **Verification**: the integrity of the bytes each signature covers, the CMS signature, the time stamps, and whether
  the signature covers the whole file (`adbe.pkcs7.detached`, `ETSI.CAdES.detached`, `adbe.pkcs7.sha1`, document time
  stamps `ETSI.RFC3161`)

## Sign a document

<!-- ref:../../tests/Meziantou.Framework.Pdf.Signing.Tests/PdfSignerTests.cs;region=PdfSigning_Sign;format=md-fenced-code-block;dedent=true -->
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

The signature field is invisible, on the first page. `PdfSignatureOptions` sets the information of the signature
dictionary (reason, location, contact, name, time) and the other certificates of the chain to embed.

## Time stamp a signature (PAdES B-T)

<!-- ref:../../tests/Meziantou.Framework.Pdf.Signing.Tests/PdfSignerTests.cs;region=PdfSigning_Timestamp;format=md-fenced-code-block;dedent=true -->
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

## Trust

`PdfSignatureVerifier` checks that the document was not modified and that the signature matches the certificate it
embeds; it does not decide whether the signer is trusted. Build the chain of `PdfSignatureInfo.Certificate` with
`X509Chain`, with the trust anchors and the revocation policy of the application, and check `CoversWholeDocument`:
a signature that does not cover the whole file was followed by incremental updates.
