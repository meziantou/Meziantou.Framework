---
title: Encryption
description: Encrypt a document with a user and an owner password or for the certificates of its recipients, choose the algorithm and the permissions, open encrypted documents, and remove or change their encryption.
---

## Encrypt a document

Set [PdfDocument.Encryption](cref:Meziantou.Framework.Pdf.PdfDocument.Encryption) before saving: the strings and streams of the document are encrypted when it is written.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_Encryption;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
document.Pages.Add(PdfSize.A4).CreateContentBuilder()
    .DrawText("Confidential", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);

// AES-256 by default: the user password opens the document with the permissions, the owner password with all
document.Encryption = new PdfEncryption("user password", "owner password")
{
    Permissions = PdfPermissions.Print | PdfPermissions.PrintHighQuality,
};
var file = document.ToByteArray();

var read = PdfDocument.Open(file, new PdfReadOptions { Password = "user password" });
var text = read.Pages[0].ExtractText();
var canCopy = read.Permissions.HasFlag(PdfPermissions.CopyContent);
```
<!-- endref -->

- **The user password** opens the document with the permissions. An empty user password opens the document without a
  prompt: the content is still encrypted in the file, but the permissions are then the only protection.
- **The owner password** opens the document with every permission.
- **The permissions** ([PdfPermissions](cref:Meziantou.Framework.Pdf.PdfPermissions): [Print](cref:Meziantou.Framework.Pdf.PdfPermissions.Print), [PrintHighQuality](cref:Meziantou.Framework.Pdf.PdfPermissions.PrintHighQuality), [Modify](cref:Meziantou.Framework.Pdf.PdfPermissions.Modify), [CopyContent](cref:Meziantou.Framework.Pdf.PdfPermissions.CopyContent), [Annotate](cref:Meziantou.Framework.Pdf.PdfPermissions.Annotate), [FillForms](cref:Meziantou.Framework.Pdf.PdfPermissions.FillForms),
  [ExtractForAccessibility](cref:Meziantou.Framework.Pdf.PdfPermissions.ExtractForAccessibility), [Assemble](cref:Meziantou.Framework.Pdf.PdfPermissions.Assemble)) are those the user password allows; the default is [All](cref:Meziantou.Framework.Pdf.PdfPermissions.All). Readers honor them,
  but nothing in the file enforces them, and this library does not either: do not rely on them to protect the content.

## Algorithms

| [Algorithm](cref:Meziantou.Framework.Pdf.PdfEncryption.Algorithm) | Version | Notes |
| --- | --- | --- |
| [AesWith256BitKey](cref:Meziantou.Framework.Pdf.PdfEncryptionAlgorithm.AesWith256BitKey) (the default) | PDF 2.0 | Revision 6 of the standard security handler |
| [AesWith128BitKey](cref:Meziantou.Framework.Pdf.PdfEncryptionAlgorithm.AesWith128BitKey) | PDF 1.6 | Deprecated by PDF 2.0 |
| [Rc4With128BitKey](cref:Meziantou.Framework.Pdf.PdfEncryptionAlgorithm.Rc4With128BitKey) | PDF 1.4 | Not secure; deprecated by PDF 2.0 |
| [Rc4With40BitKey](cref:Meziantou.Framework.Pdf.PdfEncryptionAlgorithm.Rc4With40BitKey) | PDF 1.1 | Not secure: only for very old readers |

Use the default unless the document must open in old readers. The version of the document is raised to what the
algorithm needs; saving with a [PdfSaveOptions.Version](cref:Meziantou.Framework.Pdf.PdfSaveOptions.Version) that is too old for it throws `InvalidOperationException`.
[EncryptMetadata](cref:Meziantou.Framework.Pdf.PdfEncryption.EncryptMetadata) set to `false` leaves the XMP metadata in clear, so that search engines can index it; the RC4
algorithms always encrypt it.

## Open, decrypt and change the passwords

A document read from an encrypted file keeps its encryption: a full save writes it encrypted with the keys of the file.
Set [Encryption](cref:Meziantou.Framework.Pdf.PdfDocument.Encryption) to `null` to save it in clear, or to a new [PdfEncryption](cref:Meziantou.Framework.Pdf.PdfEncryption) to change its passwords:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_ChangePassword;format=md-fenced-code-block;dedent=true -->
```csharp
// The owner password opens the document with every permission
var document = PdfDocument.Open(file, new PdfReadOptions { Password = "owner password" });

// Saved without encryption...
document.Encryption = null;
var clear = document.ToByteArray();

// ...or with new passwords and another algorithm
document.Encryption = new PdfEncryption("new user password", "new owner password")
{
    Algorithm = PdfEncryptionAlgorithm.AesWith128BitKey,
};
var protectedAgain = document.ToByteArray();
```
<!-- endref -->

- [IsEncrypted](cref:Meziantou.Framework.Pdf.PdfDocument.IsEncrypted) tells whether the file was encrypted, and [Permissions](cref:Meziantou.Framework.Pdf.PdfDocument.Permissions) what the password the document was opened with
  allows: every operation for the owner password or a file that is not encrypted.
- A wrong password throws [PdfPasswordException](cref:Meziantou.Framework.Pdf.PdfPasswordException).
- An incremental save keeps the encryption of the file: it cannot remove or change it.
- PDF/A documents cannot be encrypted.

The standard security handler is supported, revisions 2 to 6.

## Encrypt for certificates

A document can be encrypted for the certificates of its recipients instead of passwords (the public-key security
handler, `/Adobe.PubSec`): the public key of each certificate encrypts the key of the document, and the private key of
one of them opens it. Pass [PdfEncryptionRecipient](cref:Meziantou.Framework.Pdf.PdfEncryptionRecipient) instances to the [PdfEncryption](cref:Meziantou.Framework.Pdf.PdfEncryption) constructor, and open the document
with [PdfReadOptions.Certificate](cref:Meziantou.Framework.Pdf.PdfReadOptions.Certificate):

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_CertificateEncryption;format=md-fenced-code-block;dedent=true -->
```csharp
// The certificate of the recipient: its public key encrypts the document, its private key opens it
using var key = RSA.Create(2048);
using var certificate = new CertificateRequest("CN=Recipient", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
    .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

var document = new PdfDocument();
document.Pages.Add(PdfSize.A4).CreateContentBuilder()
    .DrawText("Confidential", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);
document.Encryption = new PdfEncryption(new PdfEncryptionRecipient(certificate));
var file = document.ToByteArray();

var read = PdfDocument.Open(file, new PdfReadOptions { Certificate = certificate });
var text = read.Pages[0].ExtractText();
```
<!-- endref -->

Each recipient can have its own [Permissions](cref:Meziantou.Framework.Pdf.PdfEncryptionRecipient.Permissions); the recipients without them get the [Permissions](cref:Meziantou.Framework.Pdf.PdfEncryption.Permissions) of the
encryption. Encrypting only needs the certificates, not their private keys:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_CertificateEncryptionRecipients;format=md-fenced-code-block;dedent=true -->
```csharp
// The certificates, without their private keys, are enough to encrypt the document
using var manager = X509CertificateLoader.LoadCertificateFromFile(managerFile);
using var reviewer = X509CertificateLoader.LoadCertificateFromFile(reviewerFile);

// Each recipient has its own permissions; those of the encryption apply to the others
document.Encryption = new PdfEncryption(
    new PdfEncryptionRecipient(manager),
    new PdfEncryptionRecipient(reviewer) { Permissions = PdfPermissions.Print })
{
    Algorithm = PdfEncryptionAlgorithm.AesWith256BitKey,
};
var file = document.ToByteArray();
```
<!-- endref -->

- **The certificates** must have an RSA key: the key of the document is encrypted with RSA (PKCS #1 v1.5) for each of
  them, in a PKCS #7 enveloped data encrypted with AES-256. Elliptic-curve certificates (key agreement) are not
  supported: [PdfEncryptionRecipient](cref:Meziantou.Framework.Pdf.PdfEncryptionRecipient) throws `ArgumentException`, and opening a document encrypted for one throws
  [UnsupportedPdfFeatureException](cref:Meziantou.Framework.Pdf.UnsupportedPdfFeatureException).
- **The algorithms** are those of the passwords. The document is written with the subfilter `adbe.pkcs7.s5` and a
  crypt filter that holds the recipients; the algorithms other than AES-256 need PDF 1.5 or later.
- **Opening** needs the certificate of a recipient with its private key, for instance from
  `X509CertificateLoader.LoadPkcs12FromFile`. A missing certificate, a certificate without private key or a
  certificate that is not a recipient throws [PdfPasswordException](cref:Meziantou.Framework.Pdf.PdfPasswordException). [Permissions](cref:Meziantou.Framework.Pdf.PdfDocument.Permissions) are those of the recipient, and
  [UsesCertificates](cref:Meziantou.Framework.Pdf.PdfEncryption.UsesCertificates) tells that the document is encrypted for certificates.
- The documents of other writers are read with the subfilters `adbe.pkcs7.s3`, `adbe.pkcs7.s4` and `adbe.pkcs7.s5`,
  RSA recipients (PKCS #1 v1.5 or OAEP), and an enveloped data encrypted with AES, triple DES, DES or RC2. When the
  recipients are in several crypt filters, those of the streams are used.
- A document read from a file keeps its encryption like a password-encrypted one: it is saved with the keys of the file,
  and [Recipients](cref:Meziantou.Framework.Pdf.PdfEncryption.Recipients) is empty. Set a new [PdfEncryption](cref:Meziantou.Framework.Pdf.PdfEncryption) to change the recipients, or to encrypt it with passwords.
