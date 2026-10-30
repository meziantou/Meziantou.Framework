---
title: Opening documents
description: Open a document from a file, a stream or bytes, give the password of an encrypted document, set the read limits, and know when a file was repaired.
---

## Open a document

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_ReadDocument;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(file);
var pageCount = document.Pages.Count;
var text = document.Pages[0].ExtractText();
```
<!-- endref -->

| Method | Reads |
| --- | --- |
| `PdfDocument.Open(path)`, `OpenAsync(path)` | A file, read in full: the file is not kept open |
| `PdfDocument.Open(stream)`, `OpenAsync(stream)` | A stream, read to its end; it is not closed |
| `PdfDocument.Open(byte[])`, `Open(ReadOnlyMemory<byte>)` | Bytes in memory, which the document keeps: they must not change while the document is used |

Every method takes `PdfReadOptions`. A document supports one operation at a time: do not use it from several threads at
once.

## Encrypted documents

Give the password in the options. Either password opens the document: the user password with the permissions of the
file, the owner password with every permission.

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

- Without a password, the empty password is tried: many encrypted documents have an empty user password, and open
  without one.
- A wrong password throws `PdfPasswordException`.
- `IsEncrypted` tells whether the file was encrypted, and `Permissions` what the password allows. The library does not
  enforce the permissions: an application that shows or prints documents for users decides what it lets them do.

See [Encryption](../07-Security/02-Encryption.md) to remove or change the passwords.

## Read options

| Option | Default | Limits |
| --- | --- | --- |
| `Password` | The empty password | The user or the owner password of an encrypted document |
| `MaxDecodedStreamLength` | 256 MiB | The length of the decoded data of one stream, checked as it is decoded |
| `MaxNestingDepth` | 256 | The depth of nested arrays and dictionaries |

A file beyond a limit throws `PdfResourceLimitException`. Lower the limits for the files of untrusted users: see
[Errors and limits](06-ErrorsAndLimits.md).

## Repaired files

Many files in circulation are damaged: they were truncated, edited by hand, or written by a tool with bugs. The reader
accepts what PDF viewers accept, and when the cross-reference information of a file is wrong, it rebuilds it by scanning
the file. `IsRepaired` is then `true`.

- A repaired document is read and saved in full like any other, but it cannot be saved incrementally.
- A reference to an object that does not exist is the null object, as the specification says.
- A damaged compressed stream gives the data before the damage. A stream that cannot be decoded at all throws when its
  data is read.

A file that cannot be repaired throws `InvalidPdfContentException`.
