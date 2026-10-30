---
title: Encryption
description: Encrypt a document with a user and an owner password, choose the algorithm and the permissions, open encrypted documents, and remove or change their passwords.
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

The standard security handler is supported, revisions 2 to 6. The public-key security handlers, which encrypt for
certificates instead of passwords, are not: opening such a document throws [UnsupportedPdfFeatureException](cref:Meziantou.Framework.Pdf.UnsupportedPdfFeatureException).
