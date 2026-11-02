---
title: Digital signatures
description: Sign a document with a certificate (PAdES B-B), add a time stamp (PAdES B-T), and verify the signatures of a document and whether its signer is trusted.
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
| [FieldName](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.FieldName) | `Signature1`, `Signature2`... | The name of the signature field |
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
| [IsValid](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.IsValid) | The signature is intact, cryptographically valid, and its time stamp is valid when it has one |
| [IsDocumentIntact](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.IsDocumentIntact), [IsSignatureValid](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.IsSignatureValid), [IsTimestampValid](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.IsTimestampValid) | Each of these checks; [IsTimestampValid](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.IsTimestampValid) is `null` without a time stamp |
| [CoversWholeDocument](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.CoversWholeDocument) | Nothing was appended to the file after the signature. When it is `false`, the document was changed after it was signed: the signature covers an earlier revision only |
| [Certificate](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.Certificate), [Certificates](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.Certificates) | The certificate of the signer, and the certificates the signature embeds |
| [SigningTime](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.SigningTime), [TimestampTime](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.TimestampTime) | The time the signer claims, and the time a time stamp proves |
| [Reason](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.Reason), `Location`, [ContactInfo](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.ContactInfo), [SignerName](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.SignerName), [FieldName](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.FieldName) | The information of the signature |
| [SubFilter](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.SubFilter), [IsDocumentTimestamp](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.IsDocumentTimestamp) | The format: `ETSI.CAdES.detached`, `adbe.pkcs7.detached`, `adbe.pkcs7.sha1`, or `ETSI.RFC3161` for a document time stamp |
| [Error](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureInfo.Error) | Why the signature could not be checked |

The signatures of other tools are verified as well, in the formats above.
