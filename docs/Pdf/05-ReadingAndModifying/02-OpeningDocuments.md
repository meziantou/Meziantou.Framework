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
| [PdfDocument.OpenSeekable](cref:Meziantou.Framework.Pdf.PdfDocument.OpenSeekable) | A seekable stream, read when the objects are first used: the stream stays open while the document is used |

Every method takes [PdfReadOptions](cref:Meziantou.Framework.Pdf.PdfReadOptions). A document supports one operation at a time: do not use it from several threads at
once.

## Large files

[OpenSeekable](cref:Meziantou.Framework.Pdf.PdfDocument.OpenSeekable) reads a document without copying the file to memory: the objects are read from the stream
when they are first used, and the data of a stream when it is decoded. The memory then grows with what the application
reads, not with the size of the file, and a file can be larger than 2 GB.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_OpenSeekable;format=md-fenced-code-block;dedent=true -->
```csharp
// The objects are read from the stream when they are first used: it stays open while the document is used
using var stream = File.OpenRead(path);
var document = PdfDocument.OpenSeekable(stream);
var text = document.Pages[0].ExtractText();
```
<!-- endref -->

- The stream belongs to the application: the document does not close it. Keep it open and unchanged while the document
  is used, and do not use it for anything else meanwhile, since the document moves its position.
- The errors of the stream, such as `IOException` or `ObjectDisposedException`, are not caught: they can occur
  whenever an object is first used.
- `Open(stream)` and `OpenAsync(stream)` read the stream once, into an array of its length, and do not need it after.
- The saves read the stream too: the data of the streams the application did not read, and the original file of an
  incremental update, are copied from it in chunks, without being kept in memory. A file larger than 2 GB can then be
  saved in full or incrementally, and the memory a save takes does not grow with the size of the file. Keep the stream
  open until the save completes, including the asynchronous saves, which read it when they write.

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
- A wrong password throws [PdfPasswordException](cref:Meziantou.Framework.Pdf.PdfPasswordException).
- [IsEncrypted](cref:Meziantou.Framework.Pdf.PdfDocument.IsEncrypted) tells whether the file was encrypted, and [Permissions](cref:Meziantou.Framework.Pdf.PdfDocument.Permissions) what the password allows. The library does not
  enforce the permissions: an application that shows or prints documents for users decides what it lets them do.

See [Encryption](../07-Security/02-Encryption.md) to remove or change the passwords.

## Open a remote file

[OpenAsync](cref:Meziantou.Framework.Pdf.PdfDocument.OpenAsync) with an `HttpClient` and a URI opens a file over HTTP and downloads only the parts it reads, with range requests:

<!-- ref:../../../samples/Meziantou.Framework.Pdf.Samples/Program.cs;region=Pdf_OpenRemote;format=md-fenced-code-block;dedent=true -->
```csharp
// The client sends the range requests: it must stay usable while the document is read
using var httpClient = new HttpClient();

// Only the parts of the file the document reads are downloaded
var document = await PdfDocument.OpenAsync(httpClient, uri);

// Download what a page needs before reading it, so that reading it sends no request
await document.PrefetchPageAsync(0);
var text = document.Pages[0].ExtractText();
```
<!-- endref -->

- The file is downloaded in blocks of 16 KiB, each once. Opening the document downloads its start, its end and its
  cross-reference sections; the first page of a linearized file comes with the first requests
  ([Linearize](cref:Meziantou.Framework.Pdf.PdfSaveOptions.Linearize) writes such files).
- [PrefetchPageAsync](cref:Meziantou.Framework.Pdf.PdfDocument.PrefetchPageAsync) downloads what a page needs: its content, fonts, images and annotations. Without it, an
  object read for the first time is downloaded synchronously, which the browser does not support.
- A server that does not support range requests sends the whole file, which is read from memory.
- A damaged file is repaired by scanning it, which downloads it whole: set
  [AllowFullDownload](cref:Meziantou.Framework.Pdf.PdfReadOptions.AllowFullDownload) to `false` to refuse such files instead.
- The `HttpClient` must stay usable, and the file unchanged, while the document is used.

## Read options

| Option | Default | Limits |
| --- | --- | --- |
| [Password](cref:Meziantou.Framework.Pdf.PdfReadOptions.Password) | The empty password | The user or the owner password of an encrypted document |
| [MaxDecodedStreamLength](cref:Meziantou.Framework.Pdf.PdfReadOptions.MaxDecodedStreamLength) | 256 MiB | The length of the decoded data of one stream, checked as it is decoded |
| [MaxNestingDepth](cref:Meziantou.Framework.Pdf.PdfReadOptions.MaxNestingDepth) | 256 | The depth of nested arrays and dictionaries |
| [AllowFullDownload](cref:Meziantou.Framework.Pdf.PdfReadOptions.AllowFullDownload) | `true` | Whether a file opened over HTTP can be downloaded whole, to be repaired |
| [LoadAllObjects](cref:Meziantou.Framework.Pdf.PdfReadOptions.LoadAllObjects) | `false` | Read every object and stream when the file is opened, so that [Diagnostics](cref:Meziantou.Framework.Pdf.PdfDocument.Diagnostics) is complete |

A file beyond a limit throws [PdfResourceLimitException](cref:Meziantou.Framework.Pdf.PdfResourceLimitException). Lower the limits for the files of untrusted users: see
[Errors and limits](06-ErrorsAndLimits.md).

## Repaired files

Many files in circulation are damaged: they were truncated, edited by hand, or written by a tool with bugs. The reader
accepts what PDF viewers accept, and when the cross-reference information of a file is wrong, it rebuilds it by scanning
the file. [IsRepaired](cref:Meziantou.Framework.Pdf.PdfDocument.IsRepaired) is then `true`.

- A repaired document is read and saved in full like any other, but it cannot be saved incrementally.
- A reference to an object that does not exist is the null object, as the specification says.
- An older cross-reference table (reached by the `/Prev` of a newer one) whose trailer is damaged keeps its entries:
  the file is not repaired, and the trailer gives no entry to the newer ones.
- In a repaired file, a reference whose generation is not the generation of the object resolves to the object when
  the file defines one generation of it only; when it defines several, the reference is the null object.
- The bytes of a stream whose length is right, such as an uncompressed embedded PDF document, are not taken for objects
  of the file.
- The data of a stream whose length is wrong ends at its `endstream` keyword, or at the `endobj` keyword of its object
  when that comes first: an incremental update appended to the file does not change the data of the streams before it.
- The objects are not read while the file is repaired, except the object streams, the trailers and the catalogs: the
  others are read when they are first used, as in a file that is not damaged.
- A damaged compressed stream gives the data before the damage. A stream that cannot be decoded at all throws when its
  data is read.

A file that cannot be repaired throws [InvalidPdfContentException](cref:Meziantou.Framework.Pdf.InvalidPdfContentException).

## List what was repaired

[Diagnostics](cref:Meziantou.Framework.Pdf.PdfDocument.Diagnostics) lists the problems the reader found and how it read the file anyway, whether or not the
cross-reference information was rebuilt:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfReaderTests.cs;region=Pdf_Diagnostics;format=md-fenced-code-block;dedent=true -->
```csharp
// Read the whole file when it is opened, so that every problem is listed
var document = PdfDocument.Open(data, new PdfReadOptions { LoadAllObjects = true });
var problems = document.Diagnostics
    .Where(diagnostic => diagnostic.Severity >= PdfDiagnosticSeverity.Warning)
    .Select(diagnostic => diagnostic.ToString())
    .ToList();
```
<!-- endref -->

- Each [PdfDiagnostic](cref:Meziantou.Framework.Pdf.PdfDiagnostic) has a [Kind](cref:Meziantou.Framework.Pdf.PdfDiagnostic.Kind), a [Severity](cref:Meziantou.Framework.Pdf.PdfDiagnostic.Severity), a message, and the offset and the object
  number where the problem is, when it has them.
- The severity tells what the repair can change: `Information` when nothing is lost (bytes before the header, a table
  that starts at 1), `Warning` when the reader made a guess (a wrong offset, a stream that ends at `endstream`), `Error`
  when something is lost (an object that cannot be found, the end of a damaged stream).
- The objects are read when they are first used, so the list grows as the document is read. Open the document with
  [LoadAllObjects](cref:Meziantou.Framework.Pdf.PdfReadOptions.LoadAllObjects), or call [ValidateStructure](cref:Meziantou.Framework.Pdf.PdfDocument.ValidateStructure), to read every object, the page tree and the data of every
  stream first; a stream that cannot be decoded is then reported instead of thrown.
- A problem is reported once per object and offset, and the list is limited to 1,000 problems.
